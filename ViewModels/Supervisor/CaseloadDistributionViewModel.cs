using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Supervisor
{
    /// <summary>
    /// One consumer on the distribution list, with its own selection state and the outcome of
    /// the last attempt to move it.
    /// </summary>
    /// <remarks>
    /// <see cref="Revision"/> is the token read when the list loaded. It is deliberately not
    /// refreshed just before a transfer: a revision fetched moments before it is asserted
    /// checks nothing. If somebody edited the profile after this list was drawn, the supervisor
    /// should be told rather than silently win.
    /// </remarks>
    public partial class DistributableConsumerViewModel(int personId, string fullName, int revision)
        : ObservableObject
    {
        public int PersonId { get; } = personId;
        public string FullName { get; } = fullName;
        public int Revision { get; private set; } = revision;

        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private string? outcome;
        [ObservableProperty] private bool failed;

        /// <summary>
        /// Raised when this row's checkbox changes, so the parent can recount.
        ///
        /// <para>
        /// An event rather than a <c>Checked</c>/<c>Unchecked</c> handler in the view's
        /// code-behind: the rows are their own objects, and the parent needs to know its
        /// aggregate moved. Routing that through the view would put view-model bookkeeping in
        /// a place that has no business knowing about it.
        /// </para>
        /// </summary>
        public event Action? SelectionChanged;

        public bool HasOutcome => !string.IsNullOrWhiteSpace(Outcome);

        partial void OnOutcomeChanged(string? value) => OnPropertyChanged(nameof(HasOutcome));

        partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke();

        /// <summary>Accessible description, so the outcome is not conveyed by colour alone.</summary>
        public string AutomationDescription => HasOutcome
            ? $"{FullName}. {Outcome}"
            : FullName;

        public void ClearOutcome()
        {
            Outcome = null;
            Failed = false;
            OnPropertyChanged(nameof(AutomationDescription));
        }

        public void RecordOutcome(string message, bool failed)
        {
            Outcome = message;
            Failed = failed;
            OnPropertyChanged(nameof(AutomationDescription));
        }
    }

    /// <summary>
    /// Hands consumers from the signed-in supervisor's own caseload to the case managers they
    /// supervise.
    ///
    /// <para>
    /// This is the second half of the Credible import flow described in
    /// <c>CREDIBLE_IMPORT_DESIGN.md</c>: import lands a batch on the importer's caseload, and
    /// this is where it gets distributed. It is equally the answer to staff turnover, so it is
    /// not scoped to imported consumers and has no notion of a batch.
    /// </para>
    ///
    /// <para>
    /// Every eligibility decision shown here is presentation only. The target list is filtered
    /// to make the sensible choice easy, not to make the wrong one impossible —
    /// <c>CaseloadTransferRules</c> decides that behind <see cref="IPersonService"/>, in both
    /// the API and the local service. UI visibility is not security.
    /// </para>
    /// </summary>
    public partial class CaseloadDistributionViewModel : ObservableObject
    {
        private readonly IPersonService _personService;
        private readonly IUserService _userService;
        private readonly ISessionService _sessionService;
        private readonly LatestRequestTracker _loads = new();

        public CaseloadDistributionViewModel(
            IPersonService personService,
            IUserService userService,
            ISessionService sessionService)
        {
            _personService = personService;
            _userService = userService;
            _sessionService = sessionService;
        }

        /// <summary>Raised after at least one consumer moved, so the dashboard can rebuild.</summary>
        public event Action? CaseloadsChanged;

        [ObservableProperty] private User? selectedTarget;
        [ObservableProperty] private string statusMessage = string.Empty;
        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private bool isLoading;

        public ObservableCollection<DistributableConsumerViewModel> Consumers { get; } = [];
        public ObservableCollection<User> Targets { get; } = [];

        public int SelectedCount => Consumers.Count(consumer => consumer.IsSelected);
        public bool HasConsumers => Consumers.Count > 0;
        public bool HasTargets => Targets.Count > 0;

        public string SelectionLabel => SelectedCount == 1
            ? "1 consumer selected"
            : $"{SelectedCount} consumers selected";

        public bool CanDistribute =>
            !IsBusy && SelectedCount > 0 && SelectedTarget is not null;

        partial void OnSelectedTargetChanged(User? value) => NotifyDistributionState();
        partial void OnIsBusyChanged(bool value) => NotifyDistributionState();

        private void NotifyDistributionState()
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectionLabel));
            OnPropertyChanged(nameof(CanDistribute));
            DistributeCommand.NotifyCanExecuteChanged();
        }


        public async Task InitializeAsync()
        {
            var request = _loads.Begin();
            IsLoading = true;
            try
            {
                var actor = _sessionService.CurrentUser;
                if (actor is null)
                {
                    StatusMessage = "Sign in to distribute a caseload.";
                    return;
                }

                var mine = await _personService.GetPeopleForSummaryAsync(actor.Id);
                var users = await _userService.GetAllAsync();

                // A navigation-triggered load, so a slower earlier request must not publish
                // over a newer one. See the LatestRequestTracker rule in CLAUDE.md.
                if (!_loads.IsCurrent(request))
                    return;

                foreach (var existing in Consumers)
                    existing.SelectionChanged -= NotifyDistributionState;
                Consumers.Clear();

                foreach (var person in mine.OrderBy(person => person.LastName)
                                           .ThenBy(person => person.FirstName))
                {
                    var row = new DistributableConsumerViewModel(
                        person.Id, person.FullName, person.Revision);
                    row.SelectionChanged += NotifyDistributionState;
                    Consumers.Add(row);
                }

                Targets.Clear();
                foreach (var candidate in EligibleTargets(actor, users))
                    Targets.Add(candidate);

                SelectedTarget = null;
                StatusMessage = Consumers.Count == 0
                    ? "You are not holding any consumers to distribute."
                    : string.Empty;

                OnPropertyChanged(nameof(HasConsumers));
                OnPropertyChanged(nameof(HasTargets));
                NotifyDistributionState();
            }
            finally
            {
                if (_loads.IsCurrent(request))
                    IsLoading = false;
            }
        }

        /// <summary>
        /// Who this supervisor may sensibly hand a consumer to.
        ///
        /// <para>
        /// Calls <see cref="CaseloadTransferRules.CanReachCaseloadOf"/> rather than restating
        /// its conditions, so the list cannot come to disagree with the server that enforces
        /// them. Note in particular that agency-wide reach is implied by Administration and is
        /// not the raw <c>HasAgencyWideSupervision</c> flag — reimplementing this filter by
        /// hand is how an administrator would quietly stop seeing their own agency.
        /// </para>
        /// </summary>
        private static IEnumerable<User> EligibleTargets(User actor, IEnumerable<User> all)
        {
            var agencyActor = new AgencyActor(actor.Id, actor.AgencyId, actor.Permissions);
            return all.Where(candidate =>
                          candidate.Id != actor.Id &&
                          CaseloadTransferRules.CanReachCaseloadOf(
                              agencyActor,
                              new CaseloadParticipant(
                                  candidate.Id,
                                  candidate.AgencyId,
                                  candidate.Permissions,
                                  candidate.SupervisorId)))
                      .OrderBy(candidate => candidate.DisplayName);
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var consumer in Consumers)
                consumer.IsSelected = true;
            NotifyDistributionState();
        }

        [RelayCommand]
        private void ClearSelection()
        {
            foreach (var consumer in Consumers)
                consumer.IsSelected = false;
            NotifyDistributionState();
        }

        /// <summary>
        /// Moves the selected consumers, one call each.
        ///
        /// <para>
        /// Sequential and per-record on purpose. Each transfer is independently authorized,
        /// audited, and versioned, and each can fail on its own — most usefully with a stale
        /// revision, when somebody edited that consumer's profile after this list was drawn.
        /// A batch that succeeded or failed as a unit would either abandon good moves or hide
        /// the one that did not happen.
        /// </para>
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanDistribute))]
        private async Task Distribute()
        {
            var target = SelectedTarget;
            if (target is null)
                return;

            var selected = Consumers.Where(consumer => consumer.IsSelected).ToList();
            if (selected.Count == 0)
                return;

            IsBusy = true;
            var moved = 0;
            var failed = 0;
            try
            {
                foreach (var consumer in selected)
                {
                    consumer.ClearOutcome();
                    try
                    {
                        await _personService.TransferOwnershipAsync(
                            consumer.PersonId, target.Id, consumer.Revision);
                        consumer.RecordOutcome($"Moved to {target.DisplayName}.", failed: false);
                        moved++;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // The message is the service's own, which for the local path is the
                        // PersonValidationException text and for the cloud path the API's
                        // problem detail. Neither carries consumer content.
                        consumer.RecordOutcome(exception.Message, failed: true);
                        failed++;
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }

            StatusMessage = Describe(moved, failed, target.DisplayName);

            if (moved > 0)
            {
                // Reload rather than removing the moved rows by hand: the consumers that moved
                // are no longer this supervisor's, and the ones that failed need the revision
                // they actually have now, not the one that was just rejected.
                CaseloadsChanged?.Invoke();
                var outcomes = Consumers
                    .Where(consumer => consumer.HasOutcome)
                    .ToDictionary(consumer => consumer.PersonId,
                                  consumer => (consumer.Outcome!, consumer.Failed));
                var summary = StatusMessage;

                await InitializeAsync();

                foreach (var consumer in Consumers)
                {
                    if (outcomes.TryGetValue(consumer.PersonId, out var outcome))
                        consumer.RecordOutcome(outcome.Item1, outcome.Item2);
                }
                StatusMessage = summary;
            }

            NotifyDistributionState();
        }

        private static string Describe(int moved, int failed, string targetName)
        {
            if (failed == 0)
            {
                return moved == 1
                    ? $"1 consumer moved to {targetName}."
                    : $"{moved} consumers moved to {targetName}.";
            }

            if (moved == 0)
            {
                return failed == 1
                    ? "1 consumer could not be moved. See the message beside it."
                    : $"{failed} consumers could not be moved. See the messages beside them.";
            }

            return $"{moved} moved to {targetName}; {failed} could not be moved. " +
                   "See the messages beside them.";
        }
    }
}
