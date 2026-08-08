using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Identity.Client;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
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
        private Settings? _settings;
        private Incentive? _incentive;
        private List<Note> _monthlyNotes = [];
        private DateTime _lastAbandonmentCheck = DateTime.Now;
        private int _remainingEligibleDays;
        private List<ExemptDate> _exemptDatesForMonth = [];
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
            ProvidersViewModel providersViewModel
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
            NoteEntry = noteEntryViewModel; NotesView = CollectionViewSource.GetDefaultView(Notes);
            NotesView.Filter = FilterNotes;
            NotesLog = notesWindowViewModel;
            Calendar = calendarViewModel;
            Clients = newClientViewModel;
            Statistics = statisticsViewModel;
            Reviews = reviewsViewModel;
            Providers = providersViewModel;

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

            // Form side effects, awaited by the module BEFORE NoteSaved fires.
            // Preserves the old split: new form notes → FormStatusRequested;
            // edited form notes → MarkFormCompleteRequested.
            noteEntryViewModel.FormNoteSavedAsync = async (formType, wasEdit) =>
            {
                if (wasEdit)
                    MarkFormCompleteRequested?.Invoke(this, formType);
                else if (FormStatusRequested is not null)
                    await FormStatusRequested(formType);
            };

            noteEntryViewModel.NoteSaved += async (s, e) => await OnNoteSavedAsync();

            newClientViewModel.FormComplianceChanged += async (s, e) =>
            {
                await LoadPeopleAsync();
                if (SelectedPerson is not null)
                    SelectedPerson = People.FirstOrDefault(p => p.Id == SelectedPerson.Id);
                await NotesLog.ReloadAsync();
            };

            notesWindowViewModel.NoteStatusChanged += async (s, e) =>
            {
                await LoadMonthlyNotesAsync();
            };

            calendarViewModel.ExemptDateChanged += async (s, e) =>
            {
                await LoadExemptDatesAsync();
                await LoadMonthlyNotesAsync();
            };
        }

        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        public event EventHandler<FormType>? MarkFormCompleteRequested;

        // -------------------------------------------------------------------------
        // Properties
        // -------------------------------------------------------------------------

        public NoteEntryViewModel NoteEntry { get; }
        public NotesWindowViewModel NotesLog { get; }
        public NewClientViewModel Clients { get; }
        public CaseloadMatrixViewModel? Matrix { get; private set; }

        public bool IsDashboardSubActive => CurrentSubViewModel is null;
        public bool IsClientsSubActive => CurrentSubViewModel is NewClientViewModel;
        public bool IsNotesLogSubActive => CurrentSubViewModel is NotesWindowViewModel;
        public bool IsMatrixSubActive => CurrentSubViewModel is CaseloadMatrixViewModel;
        public bool IsSubViewActive => CurrentSubViewModel is not null;
        public bool IsCalendarSubActive => CurrentSubViewModel is CalendarViewModel;
        public bool IsStatisticsSubActive => CurrentSubViewModel is StatisticsViewModel;
        public bool IsReviewsSubActive => CurrentSubViewModel is ReviewsViewModel;
        public bool IsProvidersSubActive => CurrentSubViewModel is ProvidersViewModel;
        public ReviewsViewModel Reviews { get; }
        public ProvidersViewModel Providers { get; }
        public Func<FormType, Task>? FormStatusRequested { get; set; }
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
            OnPropertyChanged(nameof(IsSubViewActive));
        }



        partial void OnSelectedPersonChanged(Person? value)
        {
            LoadNotesForPersonAsync(value);
            RefreshComplianceFlags();
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
        }

        // The dot is per-tab and independent of the window, so it does not change
        // here — only the visible set does.
        partial void OnDateFilterChanged(BoardDateFilter value)
        {
            OnPropertyChanged(nameof(BoardItems));
            OnPropertyChanged(nameof(BoardGroups));
            OnPropertyChanged(nameof(DateFilterLabel));
        }

        partial void OnSearchTextChanged(string? value) => NotesView.Refresh();
        partial void OnFilterStatusChanged(NoteStatus? value) => NotesView.Refresh();



        // -------------------------------------------------------------------------
        // Collections & computed properties
        // -------------------------------------------------------------------------

        public ObservableCollection<Note> Notes { get; } = [];
        public ObservableCollection<Person> People { get; } = [];
        public ObservableCollection<UpcomingEvent> UpcomingEvents { get; } = [];
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
                                  or UpcomingEventKind.ScheduledForm)
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
            await Statistics.LoadAsync();
            CurrentSubViewModel = Statistics;
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
        private async Task DeleteNote()
        {
            if (SelectedNote is null)
                return;

            await _noteService.DeleteNoteAsync(SelectedNote);
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

            if (form.IsCompliant)
                form.Reset();
            else
                form.MarkComplete(form.DueDate);
            await _formService.UpdateFormAsync(form);
            RefreshComplianceFlags();
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

            row.Form.Reset();
            row.Form.OpenedDate = DateTime.Today;
            await _formService.UpdateFormAsync(row.Form);
            AfterRowStatusChange(row);
        }

        [RelayCommand]
        private async Task MarkFormCompleted(FormTaskRow? row)
        {
            if (row is null)
                return;

            row.Form.MarkComplete(DateTime.Today);
            await _formService.UpdateFormAsync(row.Form);
            AfterRowStatusChange(row);
        }

        [RelayCommand]
        private async Task MarkFormNotStarted(FormTaskRow? row)
        {
            if (row is null)
                return;

            row.Form.Reset();
            row.Form.OpenedDate = null;
            await _formService.UpdateFormAsync(row.Form);
            AfterRowStatusChange(row);
        }

        // Updates the touched row in place rather than rebuilding the list, so the row
        // recolors where you're looking and a just-completed form doesn't vanish before
        // you see green. Compliance flags and the matrix do refresh, keeping the
        // checkbox grid and caseload matrix in step with the board.
        private void AfterRowStatusChange(FormTaskRow row)
        {
            row.Refresh();
            RefreshComplianceFlags();
            Matrix?.Rebuild(People, DateTime.Today);
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
        // Fires after the module has saved and awaited its form side effects.
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
        }

        private async void LoadNotesForPersonAsync(Person? person)
        {
            if (person is null)
            {
                Notes.Clear();
                return;
            }

            var notes = await _noteService.GetAllByPersonAsync(person.Id);
            Notes.Clear();
            foreach (var note in notes)
                Notes.Add(note);
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
                People.Clear();
                var people = await _personService.GetAllPeopleAsync(LoggedInUser!.Id);
                foreach (var person in people)
                    People.Add(person);

                // Hand the module the same instances. It re-selects by Id, and its
                // same-Id guard preserves any in-progress draft.
                NoteEntry.SetPeople(People);

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

            var settings = await _settingsService.LoadAsync();
            var events = _upcomingEventService.GenerateEvents(People, settings);
            UpcomingEvents.Clear();
            foreach (var e in events)
                UpcomingEvents.Add(e);

            OnPropertyChanged(nameof(AllEvents));
            OnPropertyChanged(nameof(OverdueCount));
            OnPropertyChanged(nameof(HasOverdueEvents));
            OnPropertyChanged(nameof(BoardItems));
            OnPropertyChanged(nameof(BoardGroups));
            OnPropertyChanged(nameof(TabHasOverdue));
        }

        public async Task MarkFormCompleteAsync(FormType formType)
        {
            if (SelectedPerson is null)
                return;

            var form = SelectedPerson.GetCurrentCycleForm(formType);
            if (form is null)
                return;

            form.MarkComplete(DateTime.Today);
            await _formService.UpdateFormAsync(form);
            RefreshComplianceFlags();
            Matrix?.Rebuild(People, DateTime.Today);
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
        // owns edit state now.
        public void EnterEditMode()
        {
            if (SelectedNote is null)
                return;

            NoteEntry.EnterEditMode(SelectedNote);
        }

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
            var timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            timer.Tick += async (s, e) =>
            {
                if ((DateTime.Now - _lastAbandonmentCheck).TotalHours >= 24)
                {
                    await _noteService.UpdateAbandonedNotesAsync(_settings?.AbandonedAfterDays ?? 8);
                    _lastAbandonmentCheck = DateTime.Now;
                }
            };
            timer.Start();
        }
    }
}
