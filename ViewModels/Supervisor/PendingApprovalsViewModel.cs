using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using Sati.Contracts.V1;
using Sati.Services;
using Sati.Data.Cloud;
using System.Net;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;

namespace Sati.ViewModels.Supervisor
{
    public partial class PendingApprovalsViewModel : ObservableObject
    {
        private readonly ISupervisorService _supervisorService;
        private readonly ISessionService _sessionService;

        public PendingApprovalsViewModel(
            ISupervisorService supervisorService,
            ISessionService sessionService)
        {
            _supervisorService = supervisorService;
            _sessionService = sessionService;
        }

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        // Notes whose consumers pass the compliance gate — ready for content review.
        public ObservableCollection<PendingNoteViewModel> PendingNotes { get; } = [];

        // Notes whose consumers fail the compliance gate — waiting for compliance
        // to be met, or for a supervisor override with written justification.
        public ObservableCollection<PendingNoteViewModel> NonCompliantNotes { get; } = [];
        public ObservableCollection<NoteReviewCaseManagerOption> CaseManagerOptions { get; } = [];
        public ObservableCollection<NoteReviewClientOption> ClientOptions { get; } = [];
        private IReadOnlyList<NoteReviewClientOption> _allClientOptions = [];

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private PendingNoteViewModel? selectedNote;
        [ObservableProperty] private string? returnReason;
        [ObservableProperty] private bool isReturnDialogVisible;
        [ObservableProperty] private NoteReviewCaseManagerOption? selectedCaseManager;
        [ObservableProperty] private NoteReviewClientOption? selectedClient;
        [ObservableProperty] private DateTime? fromDate;
        [ObservableProperty] private DateTime? toDate;
        [ObservableProperty] private string searchTerm = string.Empty;
        [ObservableProperty] private bool areFiltersAvailable = true;
        [ObservableProperty] private string filterStatusMessage = string.Empty;

        // Override dialog state
        [ObservableProperty] private PendingNoteViewModel? overrideNote;
        [ObservableProperty] private string? overrideReason;
        [ObservableProperty] private bool isOverrideDialogVisible;

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------

        public bool HasPending => PendingNotes.Count > 0;
        public bool HasNonCompliant => NonCompliantNotes.Count > 0;
        public string EmptyStateMessage => IsLoading ? "Loading notes..." :
            HasMore ? "No compliant notes among the notes loaded so far." :
            HasNonCompliant ? "No notes are ready for ordinary approval. Review Held for Compliance below." :
            "No notes pending approval.";
        public string NonCompliantEmptyMessage => IsLoading ? "Loading notes..." :
            HasMore ? "No compliance holds among the notes loaded so far." : "No notes held for compliance.";

        // -------------------------------------------------------------------------
        // Load
        // -------------------------------------------------------------------------

        private readonly LatestRequestTracker _loads = new();
        private int _generation;
        private NoteReviewQuery _activeFilter = new();
        private int? _nextAfterId;
        private int? _throughId;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private bool isBatchApproving;
        [ObservableProperty] private bool hasMore;
        [ObservableProperty] private string statusMessage = string.Empty;
        [ObservableProperty] private string maximumUnitsText = NoteReviewRules.DefaultMaximumUnits.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public bool CanLoadMore => HasMore && !IsLoading && !IsBatchApproving;
        public bool CanApplyFilters => AreFiltersAvailable && !IsLoading && !IsBatchApproving;
        public bool CanBatchApprove => !IsLoading && !IsBatchApproving &&
            int.TryParse(MaximumUnitsText, out var limit) && NoteReviewRules.ValidThreshold(limit);
        partial void OnIsLoadingChanged(bool value) => NotifyActions();
        partial void OnIsBatchApprovingChanged(bool value) => NotifyActions();
        partial void OnHasMoreChanged(bool value) => NotifyActions();
        partial void OnMaximumUnitsTextChanged(string value) => NotifyActions();
        partial void OnAreFiltersAvailableChanged(bool value) => NotifyActions();
        private void NotifyActions()
        {
            OnPropertyChanged(nameof(EmptyStateMessage));
            OnPropertyChanged(nameof(NonCompliantEmptyMessage));
            LoadMoreCommand.NotifyCanExecuteChanged();
            BatchApproveCommand.NotifyCanExecuteChanged();
            ApplyFiltersCommand.NotifyCanExecuteChanged();
            ClearFiltersCommand.NotifyCanExecuteChanged();
        }

        public void Deactivate()
        {
            _generation = _loads.Begin();
            IsLoading = false;
        }

        public void ClearForAccountSwitch()
        {
            Deactivate();
            SelectedNote = null;
            OverrideNote = null;
            IsReturnDialogVisible = false;
            IsOverrideDialogVisible = false;
            ReturnReason = null;
            OverrideReason = null;
            SelectedCaseManager = null;
            SelectedClient = null;
            FromDate = null;
            ToDate = null;
            SearchTerm = string.Empty;
            PendingNotes.Clear();
            NonCompliantNotes.Clear();
            CaseManagerOptions.Clear();
            ClientOptions.Clear();
            _allClientOptions = [];
            _activeFilter = new();
            _nextAfterId = null;
            _throughId = null;
            HasMore = false;
            StatusMessage = string.Empty;
            FilterStatusMessage = string.Empty;
            NotifyActions();
        }

        public async Task LoadAsync(int? filterByUserId = null)
        {
            var actor = _sessionService.CurrentUser;
            if (actor is null) return;
            _generation = _loads.Begin();
            var optionsGeneration = _generation;
            try
            {
                var options = await _supervisorService.GetReviewFilterOptionsAsync(actor.Id);
                if (!_loads.IsCurrent(optionsGeneration) || _sessionService.CurrentUser != actor) return;
                CaseManagerOptions.Clear();
                foreach (var option in options.CaseManagers) CaseManagerOptions.Add(option);
                _allClientOptions = options.Clients;
                SelectedCaseManager = CaseManagerOptions.FirstOrDefault(option => option.UserId == filterByUserId);
                SelectedClient = null;
                FromDate = ToDate = null;
                SearchTerm = string.Empty;
                RefreshClientOptions();
                AreFiltersAvailable = true;
                FilterStatusMessage = string.Empty;
            }
            catch (Exception)
            {
                if (!_loads.IsCurrent(optionsGeneration) || _sessionService.CurrentUser != actor) return;
                AreFiltersAvailable = false;
                FilterStatusMessage = "Filters are unavailable because their choices could not be loaded. " +
                    "If you are using Demo, this version of Sati may not match the Demo service. " +
                    "Install the matching update or contact support. The approval queue remains available.";
            }
            _activeFilter = BuildFilter();
            await ReloadAsync();
        }

        [RelayCommand(CanExecute = nameof(CanApplyFilters))]
        private async Task ApplyFilters()
        {
            if (FromDate?.Date > ToDate?.Date)
            {
                StatusMessage = "The start date must be on or before the end date.";
                return;
            }
            _activeFilter = BuildFilter();
            await ReloadAsync();
        }

        [RelayCommand(CanExecute = nameof(CanApplyFilters))]
        private async Task ClearFilters()
        {
            SelectedCaseManager = null;
            SelectedClient = null;
            FromDate = ToDate = null;
            SearchTerm = string.Empty;
            RefreshClientOptions();
            _activeFilter = new();
            await ReloadAsync();
        }

        partial void OnSelectedCaseManagerChanged(NoteReviewCaseManagerOption? value)
        {
            if (SelectedClient is not null && value is not null && SelectedClient.UserId != value.UserId)
                SelectedClient = null;
            RefreshClientOptions();
        }

        private void RefreshClientOptions()
        {
            ClientOptions.Clear();
            foreach (var option in _allClientOptions.Where(option =>
                         SelectedCaseManager is null || option.UserId == SelectedCaseManager.UserId))
                ClientOptions.Add(option);
        }

        private NoteReviewQuery BuildFilter() => new(
            SelectedCaseManager?.UserId,
            SelectedClient?.PersonId,
            FromDate?.Date,
            ToDate?.Date,
            SearchTerm);

        private async Task ReloadAsync()
        {
            _generation = _loads.Begin();
            _nextAfterId = 0;
            _throughId = null;
            PendingNotes.Clear();
            NonCompliantNotes.Clear();
            IsReturnDialogVisible = IsOverrideDialogVisible = false;
            SelectedNote = OverrideNote = null;
            HasMore = true;
            StatusMessage = string.Empty;
            NotifyCounts();
            await FetchPageAsync(_generation);
        }

        [RelayCommand(CanExecute = nameof(CanLoadMore))]
        private Task LoadMore() => FetchPageAsync(_generation);

        private async Task FetchPageAsync(int generation)
        {
            var actor = _sessionService.CurrentUser;
            if (actor is null || _nextAfterId is not int after) return;
            IsLoading = true;
            try
            {
                var page = await _supervisorService.GetReviewPageAsync(actor.Id, after, _throughId, _activeFilter);
                if (!_loads.IsCurrent(generation) || _sessionService.CurrentUser != actor) return;
                foreach (var note in page.Notes)
                {
                    var target = note.ComplianceFailureReasons.Count == 0 ? PendingNotes : NonCompliantNotes;
                    if (!target.Any(existing => existing.NoteId == note.Id))
                        target.Add(new PendingNoteViewModel(note,
                            CaseManagerOptions.FirstOrDefault(option => option.UserId == note.Person.UserId)?.DisplayName));
                }
                _throughId = page.ThroughId;
                _nextAfterId = page.NextAfterId;
                HasMore = page.NextAfterId.HasValue;
                StatusMessage = HasMore ? "Scroll down or choose Load more for the next 10 notes." : "All notes in this queue have been loaded.";
                if (AreFiltersAvailable)
                {
                    var loaded = PendingNotes.Count + NonCompliantNotes.Count;
                    FilterStatusMessage = HasActiveFilter(_activeFilter)
                        ? $"Filters applied. {loaded} matching note{(loaded == 1 ? string.Empty : "s")} loaded" +
                          (HasMore ? " so far." : ".")
                        : "No filters applied.";
                }
                NotifyCounts();
            }
            catch (Exception)
            {
                if (_loads.IsCurrent(generation) && _sessionService.CurrentUser == actor)
                    StatusMessage = "The next notes could not be loaded. Choose Load more to retry.";
            }
            finally
            {
                if (_loads.IsCurrent(generation)) IsLoading = false;
            }
        }

        private void NotifyCounts()
        {
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(HasNonCompliant));
            OnPropertyChanged(nameof(EmptyStateMessage));
            OnPropertyChanged(nameof(NonCompliantEmptyMessage));
        }

        private static bool HasActiveFilter(NoteReviewQuery filter) =>
            filter.UserId.HasValue || filter.PersonId.HasValue || filter.FromDate.HasValue ||
            filter.ToDate.HasValue || !string.IsNullOrWhiteSpace(filter.SearchTerm);

        [RelayCommand(CanExecute = nameof(CanBatchApprove))]
        private async Task BatchApprove()
        {
            if (!int.TryParse(MaximumUnitsText, out var limit) || !NoteReviewRules.ValidThreshold(limit)) return;
            var actor = _sessionService.CurrentUser;
            if (actor is null) return;
            var generation = _generation;
            var filter = _activeFilter;
            IsBatchApproving = true;
            var approved = 0;
            var skipped = 0;
            var stopped = false;
            try
            {
                int? cursor = 0;
                int? ceiling = null;
                while (cursor is int after && _loads.IsCurrent(generation) && _sessionService.CurrentUser == actor)
                {
                    var page = await _supervisorService.GetReviewPageAsync(actor.Id, after, ceiling, filter);
                    ceiling = page.ThroughId;
                    cursor = page.NextAfterId;
                    foreach (var note in page.Notes)
                    {
                        if (!_loads.IsCurrent(generation) || _sessionService.CurrentUser != actor) return;
                        if (note.ComplianceFailureReasons.Count != 0)
                        {
                            skipped++;
                            continue;
                        }
                        try
                        {
                            // The service rechecks threshold, validity, current compliance,
                            // revision, and reviewer scope before each individual commit.
                            await _supervisorService.ApproveNoteAsync(note.Id, actor.Id, note.Revision, limit);
                            approved++;
                        }
                        catch (NoteConcurrencyException) { skipped++; }
                        catch (InvalidOperationException) { skipped++; }
                        catch (CloudApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict || ex.StatusCode == HttpStatusCode.NotFound)
                        { skipped++; }
                        if (!_loads.IsCurrent(generation) || _sessionService.CurrentUser != actor) return;
                        StatusMessage = $"Approved {approved}; skipped {skipped}. Working...";
                    }
                }
            }
            catch (Exception)
            {
                stopped = true;
            }
            finally
            {
                IsBatchApproving = false;
            }
            if (!_loads.IsCurrent(generation) || _sessionService.CurrentUser != actor) return;
            await ReloadAsync();
            var reloadGeneration = _generation;
            if (_loads.IsCurrent(reloadGeneration) && _sessionService.CurrentUser == actor)
                StatusMessage = $"Approved {approved}; skipped {skipped}. " +
                    (stopped ? "Stopped after an error. Reload before retrying; the last save may be unconfirmed."
                        : "Batch complete. Skipped notes remain for individual review.");
        }

        // -------------------------------------------------------------------------
        // Approval commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private async Task Approve(PendingNoteViewModel note)
        {
            try
            {
                var supervisor = _sessionService.CurrentUser!;
                await _supervisorService.ApproveNoteAsync(note.NoteId, supervisor.Id, note.Revision);
                PendingNotes.Remove(note);
                OnPropertyChanged(nameof(HasPending));
            }
            catch (NoteConcurrencyException)
            {
                await HandleNoteConflictAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Approve failed: {ex.Message}");
            }
        }

        // Opens the override dialog for a non-compliant note.
        // Supervisor must provide written justification before the override
        // is submitted — the dialog enforces this via ConfirmOverride.
        [RelayCommand]
        private void OpenOverrideDialog(PendingNoteViewModel note)
        {
            OverrideNote = note;
            OverrideReason = string.Empty;
            IsOverrideDialogVisible = true;
        }

        [RelayCommand]
        private async Task ConfirmOverride()
        {
            if (OverrideNote is null || string.IsNullOrWhiteSpace(OverrideReason))
                return;

            try
            {
                var supervisor = _sessionService.CurrentUser!;
                await _supervisorService.ApproveWithOverrideAsync(
                    OverrideNote.NoteId,
                    supervisor.Id,
                    OverrideReason,
                    OverrideNote.Revision);

                NonCompliantNotes.Remove(OverrideNote);
                IsOverrideDialogVisible = false;
                OverrideNote = null;
                OverrideReason = string.Empty;
                OnPropertyChanged(nameof(HasNonCompliant));
            }
            catch (NoteConcurrencyException)
            {
                await HandleNoteConflictAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Override failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void CancelOverride()
        {
            IsOverrideDialogVisible = false;
            OverrideNote = null;
            OverrideReason = string.Empty;
        }

        // -------------------------------------------------------------------------
        // Return commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private void OpenReturnDialog(PendingNoteViewModel note)
        {
            SelectedNote = note;
            ReturnReason = string.Empty;
            IsReturnDialogVisible = true;
        }

        [RelayCommand]
        private async Task ConfirmReturn()
        {
            if (SelectedNote is null || string.IsNullOrWhiteSpace(ReturnReason))
                return;

            try
            {
                var supervisor = _sessionService.CurrentUser!;
                await _supervisorService.ReturnNoteAsync(
                    SelectedNote.NoteId,
                    supervisor.Id,
                    ReturnReason,
                    SelectedNote.Revision);

                PendingNotes.Remove(SelectedNote);
                NonCompliantNotes.Remove(SelectedNote);
                OnPropertyChanged(nameof(HasNonCompliant));
                IsReturnDialogVisible = false;
                SelectedNote = null;
                ReturnReason = string.Empty;
                OnPropertyChanged(nameof(HasPending));
            }
            catch (NoteConcurrencyException)
            {
                await HandleNoteConflictAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Return failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void CancelReturn()
        {
            IsReturnDialogVisible = false;
            SelectedNote = null;
            ReturnReason = string.Empty;
        }

        private async Task HandleNoteConflictAsync()
        {
            IsOverrideDialogVisible = false;
            IsReturnDialogVisible = false;
            OverrideNote = null;
            SelectedNote = null;
            await ReloadAsync();
            System.Windows.MessageBox.Show(
                "This note changed after you opened the approval queue. The queue has been refreshed; review the latest copy before acting.",
                "Note Updated",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    // -------------------------------------------------------------------------
    // Row view-model
    // -------------------------------------------------------------------------

    public class PendingNoteViewModel
    {
        public int NoteId { get; }
        public int Revision { get; }
        public string ClientName { get; }
        public int PersonId { get; }
        public int CaseManagerUserId { get; }
        public string CaseManagerName { get; }
        public DateTime? EventDate { get; }
        public NoteType? NoteType { get; }
        public decimal? Units { get; }
        public string Narrative { get; }
        public IReadOnlyList<string> ComplianceFailureReasons { get; }
        public bool HasComplianceFailures => ComplianceFailureReasons.Count > 0;
        public bool IsComplianceException => false; // set by non-compliant queue context

        public PendingNoteViewModel(Note note, string? caseManagerName = null)
        {
            NoteId = note.Id;
            Revision = note.Revision;
            ClientName = note.Person.FullName;
            PersonId = note.PersonId;
            CaseManagerUserId = note.Person.UserId;
            CaseManagerName = string.IsNullOrWhiteSpace(caseManagerName) ? "Case manager" : caseManagerName;
            EventDate = note.EventDate;
            NoteType = note.NoteType;
            Units = note.Units;
            Narrative = note.Narrative;
            ComplianceFailureReasons = note.ComplianceFailureReasons;
        }
    }
}
