using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Identity.Client;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Sati.ViewModels.Children;
using Sati.ViewModels.ClientDocuments;
using Sati.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace Sati.ViewModels
{
    public partial class CaseManagerDashboardViewModel : ObservableObject
    {

        // -------------------------------------------------------------------------
        // Services & private state
        // -------------------------------------------------------------------------

        private readonly IPersonService _personService;
        private readonly INoteService _noteService;
        private readonly ISettingsService _settingsService;
        private readonly IIncentiveService _incentiveService;
        private readonly ISessionService _sessionService;
        private readonly IUpcomingEventService _upcomingEventService;
        private readonly IFormService _formService;
        private readonly IExemptDateService _exemptDateService;
        private readonly ConsumerPickerSortPreferenceService? _consumerPickerSortPreferences;
        private Settings? _settings;
        private Incentive? _incentive;
        private List<Note> _monthlyNotes = [];
        private DateTime _lastAbandonmentCheck = DateTime.Now;
        private int _remainingEligibleDays;
        private List<ExemptDate> _exemptDatesForMonth = [];
        private readonly LatestRequestTracker _notesLoadRequests = new();
        private readonly LatestRequestTracker _upcomingEventLoadRequests = new();
        private readonly LatestRequestTracker _peopleLoadRequests = new();
        private readonly LatestRequestTracker _annualReminderRequests = new();
        private readonly IAnnualDocumentService? _annualDocuments;
        [ObservableProperty] private string annualDocumentReminderText = "";
        private DispatcherTimer? _abandonmentTimer;
        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public CaseManagerDashboardViewModel(
            IPersonService personService,
            INoteService noteService,
            ISettingsService settingsService,
            IIncentiveService incentiveService,
            ISessionService sessionService,
            IUpcomingEventService upcomingEventService,
IFormService formService,
            NoteEntryViewModel noteEntryViewModel,
            NotesWindowViewModel notesWindowViewModel,
            NewClientViewModel newClientViewModel,
CalendarViewModel calendarViewModel,
           IExemptDateService exemptDateService,
            StatisticsViewModel statisticsViewModel,
            ReviewsViewModel reviewsViewModel,
            ProvidersViewModel providersViewModel,
            ATRequestViewModel atRequestViewModel,
            GuidanceViewModel guidance,
            HelperReferenceViewModel reference,
            IAnnualDocumentService? annualDocuments = null,
            ConsumerPickerSortPreferenceService? consumerPickerSortPreferences = null
            )
        {
            _personService = personService;
            _noteService = noteService;
            _settingsService = settingsService;
            _incentiveService = incentiveService;
            _sessionService = sessionService;
            _upcomingEventService = upcomingEventService;
            _formService = formService;
            _exemptDateService = exemptDateService;
            _annualDocuments = annualDocuments;
            _consumerPickerSortPreferences = consumerPickerSortPreferences;
            NoteEntry = noteEntryViewModel; NotesView = CollectionViewSource.GetDefaultView(Notes);
            NotesView.Filter = FilterNotes;
            NotesLog = notesWindowViewModel;
            Calendar = calendarViewModel;
            Clients = newClientViewModel;
            Statistics = statisticsViewModel;
            Reviews = reviewsViewModel;
            Providers = providersViewModel;
            ATRequests = atRequestViewModel;
            Guidance = guidance;
            Reference = reference;
            Attestation = new FormAttestationViewModel(formService)
            {
                AttestationChangedAsync = AfterFormComplianceChangedAsync
            };
            AuthorizedRepresentative = new ClientDocumentHubViewModel(
                Clients, ClientDocumentHubMode.AuthorizedRepresentative);
            Releases = new ClientDocumentHubViewModel(
                Clients, ClientDocumentHubMode.Releases);

            // Mirror the module's client selection onto the dashboard. One-way:
            // the CLIENT combobox lives in the module now, but the notes grid and
            // compliance checkboxes still key off this VM's SelectedPerson.
            // Instance assignment is safe — SetPeople hands the module the same
            // Person instances this VM holds.
            noteEntryViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NoteEntryViewModel.SelectedPerson))
                    SelectedPerson = NoteEntry.SelectedPerson;
            };

            // New Note (button or Escape) resets the panel; the notes grid drops
            // its highlight to match. SelectedPerson deliberately survives — it
            // scopes this whole page, and ReturnToNewNote leaves the client alone.
            noteEntryViewModel.EditorCleared += (s, e) => SelectedNote = null;

            // Saving any note refreshes the dashboard. Form-tagged notes have no
            // compliance side effect; the notes reload recomputes the derived
            // pending-attestation suggestions from persisted evidence.
            noteEntryViewModel.NoteSaved += async (s, e) => await OnNoteSavedAsync();

            // Journal reminders. Either note-entry instance can write one — this
            // VM's own module, or the one inside the notes log — and both write the
            // same column the client page's journal box is bound to. Wiring lives
            // here because this VM is the only place that owns all three.
            WireJournalReminders(noteEntryViewModel, newClientViewModel);
            WireJournalReminders(notesWindowViewModel.NoteEntry, newClientViewModel);

            newClientViewModel.FormComplianceChangedAsync = async () =>
            {
                await ReloadAfterExternalFormComplianceChangedAsync();
            };
            reviewsViewModel.FormComplianceChangedAsync = ReloadAfterExternalFormComplianceChangedAsync;

            notesWindowViewModel.NoteStatusChanged += async (s, e) =>
            {
                await LoadMonthlyNotesAsync();
                await Calendar.RefreshCommand.ExecuteAsync(null);
                if (Scratchpad is not null)
                    await Scratchpad.RefreshScheduledWorkAsync();
            };

            calendarViewModel.ExemptDateChanged += async () =>
            {
                await LoadExemptDatesAsync();
                await LoadMonthlyNotesAsync();
            };

            // Re-sorts the already-loaded list in place rather than waiting for the next full
            // reload. Toggling the Settings checkbox and seeing nothing happen until the next
            // sign-in is exactly the "doesn't affect the lists" report this closes.
            if (_consumerPickerSortPreferences is not null)
            {
                _consumerPickerSortPreferences.PreferenceChanged += (_, sortByLastName) =>
                {
                    var resorted = ApplyConsumerPickerSort(People.ToList(), sortByLastName);
                    People.Clear();
                    foreach (var person in resorted)
                        People.Add(person);
                    NoteEntry.SetPeople(People);
                    NoteEntry.SetSortsPickersByLastName(sortByLastName);
                };
            }
        }

        // Order is the contract, not a detail: the client page commits any pending
        // journal edit BEFORE the writer prepends the entry, and adopts the journal
        // the writer returned afterward. Reversing these loses one of the two texts.
        private static void WireJournalReminders(NoteEntryViewModel entry, NewClientViewModel clients)
        {
            entry.JournalWriteStartingAsync = clients.FlushJournalIfCurrentAsync;
            entry.ReminderAdded += (s, e) =>
                clients.ApplyExternalJournal(e.PersonId, e.Journal);
        }

        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        // -------------------------------------------------------------------------
        // Properties
        // -------------------------------------------------------------------------

        public NoteEntryViewModel NoteEntry { get; }
        public NotesWindowViewModel NotesLog { get; }
        public NewClientViewModel Clients { get; }
        public CaseloadMatrixViewModel? Matrix { get; private set; }

        /// <summary>
        /// The shell's Work Agenda, handed down so Overview can render the same live
        /// drafts in its adaptive center host.
        /// <para>
        /// Handed down rather than injected: <c>ScratchpadViewModel</c> is registered
        /// transient, so resolving one here would produce a second, independent
        /// scratchpad. The shell saves its own instance on close and on user switch,
        /// and anything typed into a different one would be discarded without a word.
        /// </para>
        /// </summary>
        public ScratchpadViewModel? Scratchpad { get; private set; }

        internal void AttachScratchpad(ScratchpadViewModel scratchpad)
        {
            Scratchpad = scratchpad;
            OnPropertyChanged(nameof(Scratchpad));
        }

        public bool IsDashboardSubActive => CurrentSubViewModel is null;
        public bool IsClientsSubActive => CurrentSubViewModel is NewClientViewModel;
        public bool IsNotesLogSubActive => CurrentSubViewModel is NotesWindowViewModel;
        public bool IsMatrixSubActive => CurrentSubViewModel is CaseloadMatrixViewModel;
        public bool IsSubViewActive => CurrentSubViewModel is not null;
        public bool IsCalendarSubActive => CurrentSubViewModel is CalendarViewModel;
        public bool IsStatisticsSubActive => CurrentSubViewModel is StatisticsViewModel;
        public bool IsReviewsSubActive => CurrentSubViewModel is ReviewsViewModel;
        public bool IsProvidersSubActive => CurrentSubViewModel is ProvidersViewModel;
        public bool IsATRequestsSubActive => CurrentSubViewModel is ATRequestViewModel;
        public bool IsAuthorizedRepresentativeSubActive =>
            ReferenceEquals(CurrentSubViewModel, AuthorizedRepresentative);
        public bool IsReleasesSubActive => ReferenceEquals(CurrentSubViewModel, Releases);
        public GuidanceViewModel Guidance { get; }
        public HelperReferenceViewModel Reference { get; }
        public bool IsGuidanceSubActive => ReferenceEquals(CurrentSubViewModel, Guidance);
        public bool IsReferenceSubActive => ReferenceEquals(CurrentSubViewModel, Reference);
        public bool IsHelpSubActive => IsGuidanceSubActive || IsReferenceSubActive;
        public bool IsDocumentsSubActive => IsATRequestsSubActive || IsAuthorizedRepresentativeSubActive || IsReleasesSubActive;
        public ReviewsViewModel Reviews { get; }
        public ProvidersViewModel Providers { get; }
        public ATRequestViewModel ATRequests { get; }
        public ClientDocumentHubViewModel AuthorizedRepresentative { get; }
        public ClientDocumentHubViewModel Releases { get; }
        public FormAttestationViewModel Attestation { get; }
        public CalendarViewModel Calendar { get; }
        public StatisticsViewModel Statistics { get; }
        [ObservableProperty] private object? currentSubViewModel;
        [ObservableProperty] private User? loggedInUser;
        [ObservableProperty] private Person? selectedPerson;
        [ObservableProperty] private Note? selectedNote;
        [ObservableProperty] private string? searchText;
        [ObservableProperty] private NoteStatus? filterStatus;
        [ObservableProperty] private bool sortByDate = true;
        [ObservableProperty] private bool showOverdue;
        [ObservableProperty] private BoardTab selectedTab = BoardTab.All;
        [ObservableProperty] private BoardDateFilter dateFilter = BoardDateFilter.TwoWeeks;
        [ObservableProperty] private bool isNotesStateVisible = true;
        [ObservableProperty] private string notesStateMessage = "Select a client to view notes.";
        [ObservableProperty] private bool isBoardStateVisible = true;
        [ObservableProperty] private string boardStateMessage = "Loading deadlines...";
        private bool _hasLoadedDeadlineData;
        private string? _deadlineLoadFailure;


        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnCurrentSubViewModelChanged(object? value)
        {
            OnPropertyChanged(nameof(IsDashboardSubActive));
            OnPropertyChanged(nameof(IsClientsSubActive));
            OnPropertyChanged(nameof(IsNotesLogSubActive));
            OnPropertyChanged(nameof(IsMatrixSubActive));
            OnPropertyChanged(nameof(IsCalendarSubActive));
            OnPropertyChanged(nameof(IsStatisticsSubActive));
            OnPropertyChanged(nameof(IsReviewsSubActive));
            OnPropertyChanged(nameof(IsProvidersSubActive));
            OnPropertyChanged(nameof(IsATRequestsSubActive));
            OnPropertyChanged(nameof(IsAuthorizedRepresentativeSubActive));
            OnPropertyChanged(nameof(IsReleasesSubActive));
            OnPropertyChanged(nameof(IsSubViewActive));
            OnPropertyChanged(nameof(IsGuidanceSubActive));
            OnPropertyChanged(nameof(IsReferenceSubActive));
            OnPropertyChanged(nameof(IsHelpSubActive));
            OnPropertyChanged(nameof(IsDocumentsSubActive));
        }



        partial void OnSelectedPersonChanged(Person? value)
        {
            _annualReminderRequests.Invalidate(); AnnualDocumentReminderText = "";
            Attestation.CancelCommand.Execute(null);
            _ = LoadNotesForPersonAsync(value);
            RefreshComplianceFlags();
            PendingAttestations.Clear();
            OnPropertyChanged(nameof(HasPendingAttestations));
        }

        partial void OnSortByDateChanged(bool value)
        {
            OnPropertyChanged(nameof(AllEvents));
        }

        partial void OnShowOverdueChanged(bool value)
        {
            OnPropertyChanged(nameof(AllEvents));
        }
        partial void OnSelectedTabChanged(BoardTab value)
        {
            OnPropertyChanged(nameof(BoardItems));
            OnPropertyChanged(nameof(BoardGroups));
            OnPropertyChanged(nameof(IsTaskListTab));
            OnPropertyChanged(nameof(IsEffectiveDatesTab));
            OnPropertyChanged(nameof(TabHasOverdue));
            RefreshBoardState();
        }

        // The dot is per-tab and independent of the window, so it does not change
        // here — only the visible set does.
        partial void OnDateFilterChanged(BoardDateFilter value)
        {
            OnPropertyChanged(nameof(BoardItems));
            OnPropertyChanged(nameof(BoardGroups));
            OnPropertyChanged(nameof(DateFilterLabel));
            RefreshBoardState();
        }

        partial void OnSearchTextChanged(string? value)
        {
            NotesView.Refresh();
            RefreshNotesState();
        }

        partial void OnFilterStatusChanged(NoteStatus? value)
        {
            NotesView.Refresh();
            RefreshNotesState();
        }



        // -------------------------------------------------------------------------
        // Collections & computed properties
        // -------------------------------------------------------------------------

        public ObservableCollection<Note> Notes { get; } = [];
        public ObservableCollection<Person> People { get; } = [];
        public ObservableCollection<UpcomingEvent> UpcomingEvents { get; } = [];
        public ObservableCollection<PendingAttestation> PendingAttestations { get; } = [];
        public bool HasPendingAttestations => PendingAttestations.Count > 0;
        public record EffectiveDateGroup(string Label, bool IsCurrent, List<string> ClientNames);
        public double DailyAverageUnits
        {
            get
            {
                var billedDays = _monthlyNotes
                    .Where(n => n.Status is NoteStatus.Pending or NoteStatus.Logged
                             && n.EventDate.HasValue)
                    .Select(n => n.EventDate!.Value.Date)
                    .Distinct()
                    .Count();
                if (billedDays <= 0) return 0;
                var total = (PendingUnits ?? 0) + (LoggedUnits ?? 0);
                return Math.Round((double)total / billedDays, 1);
            }
        }
        public ICollectionView NotesView { get; }

        public static Array NoteStatusOptions => Enum.GetValues(typeof(NoteStatus));
        public IEnumerable<UpcomingEvent> AllEvents
        {
            get
            {
                var source = ShowOverdue
                    ? UpcomingEvents.Where(e => e.Kind == UpcomingEventKind.LateReview)
                    : UpcomingEvents.Where(e => e.Kind != UpcomingEventKind.LateReview);
                return SortByDate
                    ? source.OrderBy(e => e.Date)
                    : source.OrderBy(e => e.Kind).ThenBy(e => e.Date);
            }
        }

        public int OverdueCount => UpcomingEvents.Count(e => e.Kind == UpcomingEventKind.LateReview);
        public bool HasOverdueEvents => OverdueCount > 0;
        // -------------------------------------------------------------------------
        // Task board
        // -------------------------------------------------------------------------

        // Heterogeneous by design: FormTaskRow for the form tabs, UpcomingEvent for
        // Appointments, both for All. The XAML picks a template per item type.
        private IEnumerable<object> UnfilteredBoardItems() => SelectedTab switch
        {
            BoardTab.CompAssessments => BuildFormRows(FormType.ComprehensiveAssessment),
            BoardTab.Reclasses => BuildFormRows(FormType.Reclassification),
            BoardTab.Pcps => BuildFormRows(FormType.PCP),
            BoardTab.Releases => BuildFormRows(FormType.Release_Agency, FormType.Release_DHHS, FormType.Release_Medical),
            BoardTab.Reviews => BuildFormRows(FormType.Q1R, FormType.Q2R, FormType.Q3R, FormType.Q4R),
            BoardTab.Appointments => ScheduledEvents(),
            BoardTab.All => AllBoardItems(),
            _ => []
        };

        public IEnumerable<object> BoardItems =>
            UnfilteredBoardItems().Where(i => PassesDateFilter(BoardItemDate(i), DateTime.Today));

        // Preserve urgency at the group level: the person with the earliest item
        // appears first, while each person's own rows remain date-ordered.
        public IReadOnlyList<BoardPersonGroup> BoardGroups => BoardItems
            .GroupBy(BoardItemClientName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Name = group.Key,
                Items = group.OrderBy(BoardItemDate).ToList(),
                EarliestDate = group.Min(BoardItemDate)
            })
            .OrderBy(group => group.EarliestDate)
            .ThenBy(group => group.Name)
            .Select((group, index) => new BoardPersonGroup(
                group.Name,
                group.Items,
                IsAlternate: index % 2 == 1))
            .ToList();

        private static string BoardItemClientName(object item) => item switch
        {
            FormTaskRow row => row.ClientName,
            UpcomingEvent e => e.ClientName,
            _ => "Other"
        };

        // The board is heterogeneous, so the filter needs one date per item regardless
        // of type. Type pattern in a switch expression; the discard arm returns
        // MaxValue so an unrecognized item is never treated as overdue and never
        // disappears from a narrow window.
        private static DateTime BoardItemDate(object item) => item switch
        {
            FormTaskRow row => row.DueDate,
            UpcomingEvent e => e.Date,
            _ => DateTime.MaxValue
        };

        // Upper bound only, by design — see BoardDateFilter. Overdue is the one arm
        // with a lower bound and no upper. Since BuildFormRows no longer caps its
        // lookahead, this is the only thing bounding how far ahead the board sees.
        private bool PassesDateFilter(DateTime date, DateTime today) => DateFilter switch
        {
            BoardDateFilter.Overdue => date.Date < today,
            BoardDateFilter.TwoWeeks => date.Date <= today.AddDays(14),
            BoardDateFilter.FourWeeks => date.Date <= today.AddDays(28),
            BoardDateFilter.SixWeeks => date.Date <= today.AddDays(42),
            BoardDateFilter.EightWeeks => date.Date <= today.AddDays(56),
            BoardDateFilter.TenWeeks => date.Date <= today.AddDays(70),
            BoardDateFilter.TwelveWeeks => date.Date <= today.AddDays(84),
            _ => true
        };

        public string DateFilterLabel => DateFilter switch
        {
            BoardDateFilter.Overdue => "Overdue",
            BoardDateFilter.TwoWeeks => "2 wks",
            BoardDateFilter.FourWeeks => "4 wks",
            BoardDateFilter.SixWeeks => "6 wks",
            BoardDateFilter.EightWeeks => "8 wks",
            BoardDateFilter.TenWeeks => "10 wks",
            BoardDateFilter.TwelveWeeks => "12 wks",
            _ => "All"
        };

        // Counted against the UNFILTERED tab: the dot's job is to tell you there's
        // overdue work you can't currently see. Counting the filtered set would make
        // the dot vanish exactly when it's most useful.
        public bool TabHasOverdue
        {
            get
            {
                if (SelectedTab == BoardTab.EffectiveDates)
                    return false;

                var today = DateTime.Today;
                return UnfilteredBoardItems().Any(i => BoardItemDate(i).Date < today);
            }
        }

        public bool IsEffectiveDatesTab => SelectedTab == BoardTab.EffectiveDates;
        public bool IsTaskListTab => SelectedTab != BoardTab.EffectiveDates;

// Per client, per type: the soonest-due incomplete form scoped to the
        // current/next cycle. No lookahead cap — DateFilter owns the forward bound
        // now. One row per client per type is the ceiling, so All stays bounded.
        // Completed forms drop out, so this lands on next-cycle renewals and
        // outstanding reviews.
        private IEnumerable<FormTaskRow> BuildFormRows(params FormType[] types)
        {
            if (_settings is null)
                return [];

            var today = DateTime.Today;
            var rows = new List<FormTaskRow>();

            foreach (var person in People)
            {
                if (person.EffectiveDate is null)
                    continue;

                var boundaries = person.GetCurrentCycleBoundaries(today);
                if (boundaries is null)
                    continue;

                var (cycleStart, _) = boundaries.Value;

                foreach (var type in types)
                {
                    var form = person.Forms
                                            .Where(f => f.Type == type && f.CompletedDate is null && f.DueDate >= cycleStart)
                                            .OrderBy(f => f.DueDate)
                                            .FirstOrDefault();
                    if (form is null)
                        continue;
                    var openDaysBefore = Person.GetOpenDaysBefore(type, _settings);
                    var openByDate = form.DueDate.AddDays(-openDaysBefore);
                    rows.Add(new FormTaskRow(form, person.FullName,
                        Person.FormDisplayName(type), openByDate, today));
                }
            }

            return rows.OrderBy(r => r.DueDate);
        }

        private IEnumerable<UpcomingEvent> ScheduledEvents() =>
            UpcomingEvents
                .Where(e => e.Kind is UpcomingEventKind.ScheduledVisit
                                  or UpcomingEventKind.ScheduledContact
                                  or UpcomingEventKind.ScheduledPhone
                                  or UpcomingEventKind.ScheduledEmail
                                  or UpcomingEventKind.ScheduledForm
                                  or UpcomingEventKind.ScheduledReminder
                                  or UpcomingEventKind.ScheduledOther)
                .OrderBy(e => e.Date);

        private IEnumerable<object> AllBoardItems()
        {
            var formRows = BuildFormRows(Enum.GetValues<FormType>());
            return formRows
                .Cast<object>()
                .Concat(ScheduledEvents().Cast<object>())
                .OrderBy(item => item is FormTaskRow row ? row.DueDate : ((UpcomingEvent)item).Date);
        }
        public int Threshold
        {
            get
            {
                if (_incentive is null) return 0;
                var effectiveDays = _incentive.DaysScheduled - _exemptDatesForMonth.Count;
                return Math.Max(0, effectiveDays) * _incentive.UnitsPerDay;
            }
        }
        public int SafeThreshold => Threshold > 0 ? Threshold : 1;

        public decimal? PendingUnits => _monthlyNotes.Where(n => n.Status == NoteStatus.Pending).Sum(n => n.Units);
        public decimal? LoggedUnits => _monthlyNotes.Where(n => n.Status == NoteStatus.Logged).Sum(n => n.Units);
        public decimal? AbandonedUnits => _monthlyNotes.Where(n => n.Status == NoteStatus.Abandoned).Sum(n => n.Units);

        public decimal EstimatedIncentive => _incentive?.Calculate(LoggedUnits ?? 0) ?? 0;
        public int RemainingEligibleDays => _remainingEligibleDays;

        public decimal? UnitsPerRemainingDay
        {
            get
            {
                if (_remainingEligibleDays <= 0) return null;
                var unitsNeeded = Threshold - ((LoggedUnits ?? 0) + (PendingUnits ?? 0));
                if (unitsNeeded <= 0) return 0m;
                return Math.Round(unitsNeeded / _remainingEligibleDays, 1);
            }
        }

        // -------------------------------------------------------------------------
        // Compliance flags
        // -------------------------------------------------------------------------

        public bool Q1RCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Q1R)?.IsCompliant ?? false;
        public bool Q2RCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Q2R)?.IsCompliant ?? false;
        public bool Q3RCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Q3R)?.IsCompliant ?? false;
        public bool Q4RCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Q4R)?.IsCompliant ?? false;
        public bool PcpCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.PCP)?.IsCompliant ?? false;
        public bool CompAssessmentCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.ComprehensiveAssessment)?.IsCompliant ?? false;
        public bool ReclassificationCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Reclassification)?.IsCompliant ?? false;
        public bool SafetyPlanCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.SafetyPlan)?.IsCompliant ?? false;
        public bool PrivacyPracticesCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.PrivacyPractices)?.IsCompliant ?? false;
        public bool ReleaseAgencyCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Release_Agency)?.IsCompliant ?? false;
        public bool ReleaseDhhsCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Release_DHHS)?.IsCompliant ?? false;
        public bool ReleaseMedicalCompliant => SelectedPerson?.GetCurrentCycleForm(FormType.Release_Medical)?.IsCompliant ?? false;

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------
        [RelayCommand] private void SelectUpcomingTab() => ShowOverdue = false;
        [RelayCommand] private void SelectOverdueTab() => ShowOverdue = true;
        [RelayCommand] private void NavigateToHelp() => NavigateToGuidance();
        [RelayCommand] private void NavigateToGuidance() => CurrentSubViewModel = Guidance;
        [RelayCommand] private void NavigateToReference() => CurrentSubViewModel = Reference;
        [RelayCommand] private Task NavigateToDocuments() => NavigateToATRequestsCommand.ExecuteAsync(null);
        [RelayCommand] private void NavigateToOverview() => CurrentSubViewModel = null; 
        [RelayCommand] private void NavigateToClients() => CurrentSubViewModel = Clients;
        [RelayCommand] private void NavigateToNotesLog() => CurrentSubViewModel = NotesLog;
        [RelayCommand]
        private void NavigateToMatrix() 
        {
            Matrix?.Rebuild(People, DateTime.Today);
            CurrentSubViewModel = Matrix;
        }
        [RelayCommand] private void NavigateToCalendar() => CurrentSubViewModel = Calendar;
        [RelayCommand]
        private async Task NavigateToStatistics()
        {
            CurrentSubViewModel = Statistics;
            await Statistics.LoadAsync();
        }

        // Loads on every visit rather than once. Review items depend on the
        // client's waiver-service flags, which can change in the Clients tab
        // between visits, and on the cycle anchor, which rolls over on the
        // anniversary. Reloading each time means those changes always show up
        // without an event subscription per source of change; generation is
        // idempotent, so the cost of a no-change visit is two queries.
        [RelayCommand]
        private async Task NavigateToReviews()
        {
            await Reviews.LoadAsync();
            CurrentSubViewModel = Reviews;
        }

        // Loads on every visit, mirroring Reviews — the provider directory is shared
        // reference data another CM could edit between visits (multi-user future).
        // Cheap: one query.
        [RelayCommand]
        private async Task NavigateToProviders()
        {
            await Providers.LoadAsync();
            CurrentSubViewModel = Providers;
        }

        [RelayCommand]
        private async Task NavigateToATRequests()
        {
            CurrentSubViewModel = ATRequests;
            await ATRequests.InitializeAsync();
        }

        [RelayCommand]
        private void NavigateToAuthorizedRepresentative()
        {
            AuthorizedRepresentative.Prepare();
            CurrentSubViewModel = AuthorizedRepresentative;
        }

        [RelayCommand]
        private void NavigateToReleases()
        {
            Releases.Prepare();
            CurrentSubViewModel = Releases;
        }

        [RelayCommand]
        private async Task DeleteNote()
        {
            if (SelectedNote is null)
                return;

            try
            {
                await _noteService.DeleteNoteAsync(SelectedNote);
            }
            catch (NoteConcurrencyException)
            {
                SelectedNote = null;
                await LoadMonthlyNotesAsync();
                await LoadUpcomingEventsAsync();
                await Calendar.InitializeAsync();
                await NotesLog.ReloadAsync();
                await Clients.ReloadAsync();
                MessageBox.Show(
                    "This note changed after it was selected, so it was not deleted. The note lists have been refreshed; review the latest copy before trying again.",
                    "Note Updated",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            Notes.Remove(SelectedNote);
            SelectedPerson?.Notes.Remove(SelectedNote);
            await LoadExemptDatesAsync();
            await LoadMonthlyNotesAsync();
            await LoadUpcomingEventsAsync();
            await Calendar.InitializeAsync();
            await NotesLog.ReloadAsync();
            await Clients.ReloadAsync();
            SelectedNote = null;
        }

        [RelayCommand]
        private async Task ToggleForm(FormType type)
        {
            if (SelectedPerson is null)
                return;

            var form = SelectedPerson.GetCurrentCycleForm(type);
            if (form is null)
                return;

            BeginAttestation(form, SelectedPerson);
            await Task.CompletedTask;
        }

        [RelayCommand]
        private void SelectTab(BoardTab tab) => SelectedTab = tab;

        [RelayCommand]
        private void CycleDateFilter()
        {
            var values = Enum.GetValues<BoardDateFilter>();
            var next = (Array.IndexOf(values, DateFilter) + 1) % values.Length;
            DateFilter = values[next];
        }

        [RelayCommand]
        private async Task MarkFormOpened(FormTaskRow? row)
        {
            if (row is null)
                return;

            if (row.Form.IsCompliant)
            {
                BeginAttestation(row.Form, PersonFor(row.Form));
                return;
            }

            await _formService.OpenFormAsync(row.Form);
            await AfterRowStatusChangeAsync(row);
        }

        [RelayCommand]
        private async Task MarkFormCompleted(FormTaskRow? row)
        {
            if (row is null)
                return;

            BeginAttestation(row.Form, PersonFor(row.Form));
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task MarkFormNotStarted(FormTaskRow? row)
        {
            if (row is null)
                return;

            if (row.Form.IsCompliant)
            {
                BeginAttestation(row.Form, PersonFor(row.Form));
                return;
            }

            row.Form.OpenedDate = null;
            await _formService.UpdateFormAsync(row.Form);
            await AfterRowStatusChangeAsync(row);
        }

        // Updates the touched row in place rather than rebuilding the list, so the row
        // recolors where you're looking and a just-completed form doesn't vanish before
        // you see green. Compliance flags and the matrix do refresh, keeping the
        // checkbox grid and caseload matrix in step with the board.
        private async Task AfterRowStatusChangeAsync(FormTaskRow row)
        {
            row.Refresh();
            await AfterFormComplianceChangedAsync();
        }

        private async Task AfterFormComplianceChangedAsync()
        {
            RefreshComplianceFlags();
            RefreshPendingAttestations();
            Matrix?.Rebuild(People, DateTime.Today);
            await LoadUpcomingEventsAsync();
        }

        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            LoggedInUser = _sessionService.CurrentUser;
            await NoteEntry.InitializeAsync();
            await LoadAsync();
        }

        // -------------------------------------------------------------------------
        // Private methods
        // -------------------------------------------------------------------------

        private async Task LoadAsync()
        {
            try
            {
                if (LoggedInUser is null)
                    return;
                _settings = await _settingsService.LoadAsync();
                await LoadPeopleAsync();
                Matrix = new CaseloadMatrixViewModel();
                Matrix.Rebuild(People, DateTime.Today);
                OnPropertyChanged(nameof(Matrix));
                OnPropertyChanged(nameof(EffectiveDateGroups));
                await _noteService.UpdateAbandonedNotesAsync(_settings.AbandonedAfterDays);
                await LoadMonthlyNotesAsync();
                await LoadUpcomingEventsAsync();
                await Calendar.InitializeAsync();

                var (_, wasCreated) = await _incentiveService.GetOrCreateAsync(
    LoggedInUser!.Id, DateTime.Now.Month, DateTime.Now.Year);
                await RefreshIncentiveAsync();
                StartAbandonmentTimer();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadAsync failed: {ex.Message}");
                MessageBox.Show(
                    "Sati encountered an error loading your data. Please restart the application.",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        // The reload cascade that used to tail SubmitNewNoteAsync/SubmitEditedNoteAsync.
        // Fires after the module has persisted the note.
        // LoadPeopleAsync → SetPeople re-selects by Id inside the module; the mirror
        // then updates SelectedPerson here, which re-runs LoadNotesForPersonAsync and
        // RefreshComplianceFlags — grid and checkboxes track without explicit calls.
        private async Task OnNoteSavedAsync()
        {
            await LoadPeopleAsync();
            await LoadMonthlyNotesAsync();
            await LoadUpcomingEventsAsync();
            await NotesLog.ReloadAsync();
            await Clients.ReloadAsync();
            await Calendar.RefreshCommand.ExecuteAsync(null);
            if (Scratchpad is not null)
                await Scratchpad.RefreshScheduledWorkAsync();
        }

        private async Task LoadNotesForPersonAsync(Person? person)
        {
            var request = _notesLoadRequests.Begin();
            if (person is null)
            {
                if (_notesLoadRequests.IsCurrent(request))
                {
                    Notes.Clear();
                    SetNotesState("Select a client to view notes.");
                }
                return;
            }

            SetNotesState("Loading notes...");
            try
            {
                var notes = await _noteService.GetAllByPersonAsync(person.Id);
                if (!_notesLoadRequests.IsCurrent(request) || SelectedPerson?.Id != person.Id)
                    return;

                Notes.Clear();
                foreach (var note in notes)
                    Notes.Add(note);
                RefreshNotesState();
                RefreshPendingAttestations();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load notes for person {person.Id}: {ex.Message}");
                if (_notesLoadRequests.IsCurrent(request) && SelectedPerson?.Id == person.Id)
                    SetNotesState("Notes could not be loaded. Change clients or reopen Overview to try again.");
            }
        }

        private void RefreshNotesState()
        {
            if (SelectedPerson is null)
            {
                SetNotesState("Select a client to view notes.");
                return;
            }

            SetNotesState(NotesView.IsEmpty ? "No notes match these filters." : null);
        }

        private void SetNotesState(string? message)
        {
            NotesStateMessage = message ?? string.Empty;
            IsNotesStateVisible = message is not null;
        }

        public bool FilterNotes(object obj)
        {
            if (obj is not Note note)
                return false;

            var matchesText = string.IsNullOrWhiteSpace(SearchText) ||
                              note.Narrative.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
            var matchesStatus = FilterStatus is null || note.Status == FilterStatus;

            return matchesText && matchesStatus;
        }

        public async Task LoadPeopleAsync()
        {
            try
            {
                var request = _peopleLoadRequests.Begin();
                var people = await _personService.GetAllPeopleAsync(LoggedInUser!.Id);
                if (!_peopleLoadRequests.IsCurrent(request))
                    return;

                var sortsByLastName = await SortsPickersByLastNameAsync();
                people = ApplyConsumerPickerSort(people, sortsByLastName);

                People.Clear();
                foreach (var person in people)
                    People.Add(person);

                // Hand the module the same instances. It re-selects by Id, and its
                // same-Id guard preserves any in-progress draft.
                NoteEntry.SetPeople(People);
                NoteEntry.SetSortsPickersByLastName(sortsByLastName);

                OnPropertyChanged(nameof(BoardItems));
                OnPropertyChanged(nameof(BoardGroups));
                OnPropertyChanged(nameof(TabHasOverdue));
                OnPropertyChanged(nameof(EffectiveDateGroups));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load people: {ex.Message}");
            }
        }

        // Pure and independently testable: the order a client picker shows, given the raw service
        // result and the current preference.
        //
        // Both branches actively sort, on purpose. The caseload query already orders by LastName
        // at the database level, so an earlier version of this method left the list untouched when
        // the preference was off — meaning turning the preference on or off rarely looked any
        // different, since "off" was already last-name-grouped in practice. Sorting explicitly by
        // FirstName when off makes the two states visibly distinct, which is what a "sort by X"
        // toggle needs to actually do something a user can see.
        internal static List<Person> ApplyConsumerPickerSort(List<Person> people, bool sortByLastName) =>
            sortByLastName
                ? people
                    .OrderBy(p => p.LastName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.FirstName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : people
                    .OrderBy(p => p.FirstName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.LastName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

        // Independently loads the preference rather than trusting Settings to have loaded it
        // first — the client picker order should reflect it starting with the first caseload
        // load of the session, not only after the Settings window has been opened once.
        private int? _sortPickersByLastNameForUserId;
        private async Task<bool> SortsPickersByLastNameAsync()
        {
            if (_consumerPickerSortPreferences is null || LoggedInUser is null)
                return false;
            if (_sortPickersByLastNameForUserId == LoggedInUser.Id)
                return _consumerPickerSortPreferences.SortByLastName;

            var sortByLastName = await _consumerPickerSortPreferences.LoadForUserAsync(LoggedInUser.Id);
            _sortPickersByLastNameForUserId = LoggedInUser.Id;
            return sortByLastName;
        }

        private async Task ReloadAfterExternalFormComplianceChangedAsync()
        {
            var selectedPersonId = SelectedPerson?.Id;
            await LoadPeopleAsync();
            if (selectedPersonId is int personId)
                SelectedPerson = People.FirstOrDefault(person => person.Id == personId);
            await NotesLog.ReloadAsync();
            await AfterFormComplianceChangedAsync();
        }

        private async Task LoadMonthlyNotesAsync()
        {
            _monthlyNotes = await _noteService.GetMonthlyNotesAsync(LoggedInUser!.Id);

            var workedDays = _monthlyNotes
                            .Where(n => n.Status is NoteStatus.Pending or NoteStatus.Logged && n.EventDate.HasValue)
                            .Select(n => n.EventDate!.Value.Date)
                            .ToHashSet();
            var exemptDays = _exemptDatesForMonth
                .Select(e => e.Date.Date)
                .ToHashSet();
            _remainingEligibleDays = await _incentiveService.GetRemainingEligibleDaysAsync(
                DateTime.Now.Month, DateTime.Now.Year, workedDays, exemptDays);

            OnPropertyChanged(nameof(PendingUnits));
            OnPropertyChanged(nameof(LoggedUnits));
            OnPropertyChanged(nameof(AbandonedUnits));
            OnPropertyChanged(nameof(EstimatedIncentive));
            OnPropertyChanged(nameof(Threshold));
            OnPropertyChanged(nameof(SafeThreshold));
            OnPropertyChanged(nameof(DailyAverageUnits));
            OnPropertyChanged(nameof(RemainingEligibleDays));
            OnPropertyChanged(nameof(UnitsPerRemainingDay));
        }

        private async Task LoadUpcomingEventsAsync()
        {
            if (LoggedInUser is null)
                return;

            var request = _upcomingEventLoadRequests.Begin();
            _hasLoadedDeadlineData = false;
            _deadlineLoadFailure = null;
            RefreshBoardState();
            try
            {
                var settings = await _settingsService.LoadAsync();
                var events = _upcomingEventService.GenerateEvents(People, settings);
                if (!_upcomingEventLoadRequests.IsCurrent(request))
                    return;

                UpcomingEvents.Clear();
                foreach (var e in events)
                    UpcomingEvents.Add(e);

                _hasLoadedDeadlineData = true;
                OnPropertyChanged(nameof(AllEvents));
                OnPropertyChanged(nameof(OverdueCount));
                OnPropertyChanged(nameof(HasOverdueEvents));
                OnPropertyChanged(nameof(BoardItems));
                OnPropertyChanged(nameof(BoardGroups));
                OnPropertyChanged(nameof(TabHasOverdue));
                RefreshBoardState();
            }
            catch
            {
                if (_upcomingEventLoadRequests.IsCurrent(request))
                {
                    _deadlineLoadFailure = "Deadlines could not be loaded. Reopen Overview to try again.";
                    RefreshBoardState();
                }
                throw;
            }
        }

        private void RefreshBoardState()
        {
            var message = _deadlineLoadFailure;
            if (message is null && !_hasLoadedDeadlineData)
                message = "Loading deadlines...";
            if (message is null)
            {
                var hasItems = SelectedTab == BoardTab.EffectiveDates
                    ? EffectiveDateGroups.Any(group => group.ClientNames.Count > 0)
                    : BoardItems.Any();
                if (!hasItems)
                    message = SelectedTab == BoardTab.EffectiveDates
                        ? "No effective dates in this range."
                        : "No deadlines in this date range.";
            }

            BoardStateMessage = message ?? string.Empty;
            IsBoardStateVisible = message is not null;
        }

        public async Task OpenFormAsync(FormType formType)
        {
            if (SelectedPerson is null)
                return;

            var form = SelectedPerson.GetCurrentCycleForm(formType);
            if (form is null)
                return;

            await _formService.OpenFormAsync(form);
            Matrix?.Rebuild(People, DateTime.Today);
        }

        [RelayCommand]
        private void SelectPendingAttestation(PendingAttestation? pending)
        {
            if (pending is null || SelectedPerson is null)
                return;
            var form = SelectedPerson.Forms.SingleOrDefault(candidate => candidate.Id == pending.FormId);
            if (form is not null)
                BeginAttestation(form, SelectedPerson, pending.EvidenceNoteId);
        }

        private void BeginAttestation(Form form, Person? person, int? evidenceNoteId = null)
        {
            if (person?.EffectiveDate is not DateTime effectiveDate)
                return;
            Attestation.Begin(
                form,
                effectiveDate,
                $"{Person.FormDisplayName(form.Type)} — {person.FullName}",
                evidenceNoteId);
        }

        private Person? PersonFor(Form form) =>
            People.FirstOrDefault(person => person.Id == form.PersonId) ?? form.Person;

        private void RefreshPendingAttestations()
        {
            _ = RefreshAnnualReminderAsync();
            PendingAttestations.Clear();
            if (SelectedPerson is not { EffectiveDate: DateTime effectiveDate } person)
            {
                OnPropertyChanged(nameof(HasPendingAttestations));
                return;
            }

            var noteFacts = Notes
                .Where(note => note.FormType.HasValue && note.EventDate.HasValue && note.Status.HasValue)
                .Select(note => new NoteFact(
                    note.Id,
                    note.PersonId,
                    note.FormType!.Value.ToString(),
                    note.EventDate!.Value,
                    note.Status!.Value.ToString()))
                .ToList();
            var formFacts = person.Forms.Select(form => new FormFact(
                form.Id,
                person.Id,
                form.Type.ToString(),
                form.DueDate,
                form.CompletedDate)).ToList();
            foreach (var pending in FormAttestationRules.PendingAttestations(
                         noteFacts, formFacts, effectiveDate, DateTime.Today))
            {
                PendingAttestations.Add(pending);
            }
            OnPropertyChanged(nameof(HasPendingAttestations));
        }

        private async Task RefreshAnnualReminderAsync()
        {
            var ticket = _annualReminderRequests.Begin();
            if (_annualDocuments is null || SelectedPerson?.EffectiveDate is not DateTime effective) return;
            var id = SelectedPerson.Id;
            try
            {
                var settings = _settings ?? await _settingsService.LoadAsync();
                var cycle = AnnualPacketWindow.SuggestedCycle(effective, DateTime.Today, settings.AnnualPacketOpenDaysBefore);
                var status = await _annualDocuments.GetStatusAsync(id, cycle);
                if (_annualReminderRequests.IsCurrent(ticket)) AnnualDocumentReminderText = status.Reminder;
            }
            catch (Exception)
            {
                if (_annualReminderRequests.IsCurrent(ticket)) AnnualDocumentReminderText = "Annual document status could not be checked. Open Annual Documents to reload it.";
            }
        }

        private void RefreshComplianceFlags()
        {
            OnPropertyChanged(nameof(Q1RCompliant));
            OnPropertyChanged(nameof(Q2RCompliant));
            OnPropertyChanged(nameof(Q3RCompliant));
            OnPropertyChanged(nameof(Q4RCompliant));
            OnPropertyChanged(nameof(PcpCompliant));
            OnPropertyChanged(nameof(CompAssessmentCompliant));
            OnPropertyChanged(nameof(ReclassificationCompliant));
            OnPropertyChanged(nameof(SafetyPlanCompliant));
            OnPropertyChanged(nameof(PrivacyPracticesCompliant));
            OnPropertyChanged(nameof(ReleaseAgencyCompliant));
            OnPropertyChanged(nameof(ReleaseDhhsCompliant));
            OnPropertyChanged(nameof(ReleaseMedicalCompliant));
        }


        // Double-click handoff: the grid's selection pushes into the module, which
        // owns edit state now — including whether the draft already in the panel
        // may be replaced. This host used to skip that guard while the notes log
        // applied it; routing both through OpenForEdit is what stops them drifting
        // apart again. Unlike the notes log, single selection here does NOT load a
        // note, so this is the only path that can overwrite the panel.
        public void EnterEditMode() => NoteEntry.OpenForEdit(SelectedNote);

        private async Task LoadExemptDatesAsync()
        {
            if (LoggedInUser is null) return;
            var allExempt = await _exemptDateService.GetByYearAsync(
                LoggedInUser.Id, DateTime.Now.Year);
            _exemptDatesForMonth = allExempt
                .Where(e => e.Date.Month == DateTime.Now.Month)
                .ToList();
        }

        public async Task RefreshIncentiveAsync()
        {
            var (incentive, _) = await _incentiveService.GetOrCreateAsync(
                LoggedInUser!.Id, DateTime.Now.Month, DateTime.Now.Year);
            _incentive = incentive;
            await LoadExemptDatesAsync();

            // Recompute remaining days now that exempt dates are fresh.
            // Reuses _monthlyNotes already in memory — no extra DB query.
            var workedDays = _monthlyNotes
                .Where(n => n.Status is NoteStatus.Pending or NoteStatus.Logged
                         && n.EventDate.HasValue)
                .Select(n => n.EventDate!.Value.Date)
                .ToHashSet();
            var exemptDays = _exemptDatesForMonth
                .Select(e => e.Date.Date)
                .ToHashSet();
            _remainingEligibleDays = await _incentiveService.GetRemainingEligibleDaysAsync(
                DateTime.Now.Month, DateTime.Now.Year, workedDays, exemptDays);

            OnPropertyChanged(nameof(Threshold));
            OnPropertyChanged(nameof(SafeThreshold));
            OnPropertyChanged(nameof(EstimatedIncentive));
            OnPropertyChanged(nameof(DailyAverageUnits));
            OnPropertyChanged(nameof(RemainingEligibleDays));
            OnPropertyChanged(nameof(UnitsPerRemainingDay));
        }

        public void Reset()
        {
            LoggedInUser = null;
            People.Clear();
            Notes.Clear();
            UpcomingEvents.Clear();
            _monthlyNotes = [];
            _incentive = null;
            _settings = null;
            _exemptDatesForMonth = [];
            _hasLoadedDeadlineData = false;
            _deadlineLoadFailure = null;
            SetNotesState("Select a client to view notes.");
            RefreshBoardState();
            SelectedPerson = null;
            SelectedNote = null;
            NoteEntry.Reset();
        }

        public IEnumerable<EffectiveDateGroup> EffectiveDateGroups => BuildEffectiveDateGroups();

        private IEnumerable<EffectiveDateGroup> BuildEffectiveDateGroups()
        {
            var today = DateTime.Today;
            var currentMonth = new DateTime(today.Year, today.Month, 1);

            return Enumerable.Range(0, 7)
                .Select(i => currentMonth.AddMonths(i))
                .Select(month => new EffectiveDateGroup(
                    Label: month.ToString("MMMM yyyy"),
                    IsCurrent: month.Year == today.Year && month.Month == today.Month,
                    ClientNames: People
                        .Where(p => p.EffectiveDate.HasValue &&
                                    p.EffectiveDate.Value.Month == month.Month)
                        .OrderBy(p => p.EffectiveDate!.Value.Day)
                        .Select(p => $"{p.FullName} ({p.EffectiveDate!.Value:MMM d})")
                        .ToList()))
                .Where(g => g.ClientNames.Count > 0)
                .ToList();
        }

        private void StartAbandonmentTimer()
        {
            _abandonmentTimer?.Stop();
            _abandonmentTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _abandonmentTimer.Tick += async (s, e) =>
            {
                if ((DateTime.Now - _lastAbandonmentCheck).TotalHours >= 24)
                {
                    _abandonmentTimer.Stop();
                    try
                    {
                        await _noteService.UpdateAbandonedNotesAsync(_settings?.AbandonedAfterDays ?? 8);
                        _lastAbandonmentCheck = DateTime.Now;
                    }
                    catch (Exception ex)
                    {
                        // DispatcherTimer callbacks are async-void WPF events. An
                        // exception escaping here would be promoted to an application
                        // error even though this is only background housekeeping.
                        Debug.WriteLine($"Abandoned-note maintenance failed: {ex.Message}");
                    }
                    finally
                    {
                        _abandonmentTimer.Start();
                    }
                }
            };
            _abandonmentTimer.Start();
        }
    }
}
