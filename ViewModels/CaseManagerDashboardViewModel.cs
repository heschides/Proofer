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
        private readonly Func<string, UserMessageDialog> _validationDialog;
        private readonly IExemptDateService _exemptDateService;
        private const int DefaultLookaheadDays = 90;
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
            Func<string, UserMessageDialog> validationDialog,
            NotesWindowViewModel notesWindowViewModel,
            NewClientViewModel newClientViewModel,
CalendarViewModel calendarViewModel,
            IExemptDateService exemptDateService,
            StatisticsViewModel statisticsViewModel
            )
        {
            _personService = personService;
            _noteService = noteService;
            _settingsService = settingsService;
            _incentiveService = incentiveService;
            _sessionService = sessionService;
            _upcomingEventService = upcomingEventService;
            _formService = formService;
            _validationDialog = validationDialog;
            _exemptDateService = exemptDateService;
            NotesView = CollectionViewSource.GetDefaultView(Notes);
            NotesView.Filter = FilterNotes;
            NotesLog = notesWindowViewModel;
            Calendar = calendarViewModel;
            Clients = newClientViewModel;
            Statistics = statisticsViewModel;


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
        public Func<FormType, Task>? FormStatusRequested { get; set; }
        public CalendarViewModel Calendar { get; }
        public StatisticsViewModel Statistics { get; }
        [ObservableProperty] private object? currentSubViewModel;
        [ObservableProperty] private User? loggedInUser;
        [ObservableProperty] private Person? selectedPerson;
        [ObservableProperty] private Note? selectedNote;
        [ObservableProperty] private string? searchText;
        [ObservableProperty] private NoteStatus? filterStatus;
        [ObservableProperty] private NoteStatus? status;
        [ObservableProperty] private NoteType? selectedNoteType;
        [ObservableProperty] private FormType? selectedFormType;
        [ObservableProperty] private string? narrative;
        [ObservableProperty] private int? minutes;
        [ObservableProperty] private DateTime? eventDate;
        [ObservableProperty] private bool isEditing;
        [ObservableProperty] private bool sortByDate = true;
        [ObservableProperty] private bool showOverdue;
        [ObservableProperty] private BoardTab selectedTab = BoardTab.All;
        [ObservableProperty] private double narrativeFontSize = 14;
        [ObservableProperty] private bool isComplianceDialogVisible;
        [ObservableProperty] private string pendingJustification = string.Empty;
        [ObservableProperty] private IReadOnlyList<string> complianceFailureReasons = [];

        // True when the current dialog was triggered by a billing-window block (note
        // date inside a missed-form window), as opposed to a current-cycle paperwork
        // gate failure. Decides the non-supervisor outcome: window block => the note
        // is saved ComplianceBlocked (not billable, audit-trail evidence); paperwork
        // gate => HeldForCompliance as before. Set when the dialog opens, read in Hold.
        private bool _dialogIsWindowBlock;

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
            OnPropertyChanged(nameof(IsSubViewActive));
        }



        partial void OnSelectedPersonChanged(Person? value)
        {
            LoadNotesForPersonAsync(value);
            RefreshComplianceFlags();
            IsEditing = false;
            Status = null;
            Narrative = string.Empty;
            EventDate = null;
            SelectedNoteType = null;
            SelectedFormType = null;
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
            OnPropertyChanged(nameof(IsTaskListTab));
            OnPropertyChanged(nameof(IsEffectiveDatesTab));
        }

        partial void OnSearchTextChanged(string? value) => NotesView.Refresh();
        partial void OnFilterStatusChanged(NoteStatus? value) => NotesView.Refresh();

        partial void OnSelectedNoteTypeChanged(NoteType? value)
        {
            OnPropertyChanged(nameof(IsFormNote));

            if (value != NoteType.Form)
                SelectedFormType = null;

            if (value is null || !string.IsNullOrWhiteSpace(Narrative))
                return;

            Narrative = value.Value switch
            {
                NoteType.Visit => _settings?.VisitTemplate ?? string.Empty,
                NoteType.Contact => _settings?.ContactTemplate ?? string.Empty,
                _ => string.Empty
            };
        }

        partial void OnSelectedFormTypeChanged(FormType? value)
        {
            if (value is null || SelectedPerson is null || !string.IsNullOrWhiteSpace(Narrative))
                return;

            var user = _sessionService.CurrentUser?.DisplayName ?? "Case Manager";
            var client = SelectedPerson.FullName;

            Narrative = value switch
            {
                FormType.Q1R => $"{user} completed the Q1 90-Day Review for {client}.",
                FormType.Q2R => $"{user} completed the Q2 90-Day Review for {client}.",
                FormType.Q3R => $"{user} completed the Q3 90-Day Review for {client}.",
                FormType.Q4R => $"{user} completed the Q4 90-Day Review for {client}.",
                FormType.PCP => $"{user} attached the signed PCP and set the plan's status to \"Complete.\"",
                FormType.ComprehensiveAssessment => $"{user} completed the Comprehensive Assessment for {client}.",
                FormType.Reclassification => $"{user} completed the Reclassification for {client}.",
                FormType.SafetyPlan => $"{user} received the signed Safety Plan from {client} and filed it with annual documentation.",
                FormType.PrivacyPractices => $"{user} received the signed Privacy Practices form from {client} and filed it with annual documentation.",
                FormType.Release_Agency => $"{user} received the signed Agency Release from {client} and filed it with annual documentation.",
                FormType.Release_DHHS => $"{user} received the signed DHHS Release from {client} and filed it with annual documentation.",
                FormType.Release_Medical => $"{user} received the signed Medical Release from {client} and filed it with annual documentation.",
                _ => string.Empty
            };
        }

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
        public Array FormTypes => Enum.GetValues(typeof(FormType));

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
        public IEnumerable<object> BoardItems => SelectedTab switch
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

        public bool IsEffectiveDatesTab => SelectedTab == BoardTab.EffectiveDates;
        public bool IsTaskListTab => SelectedTab != BoardTab.EffectiveDates;

        // Per client, per type: the soonest-due form not yet marked compliant, scoped
        // to the current/next cycle, shown once it's overdue or within
        // max(its open window, 90 days) of its due date. The compliant-default annual
        // docs drop out, so this lands on next-cycle renewals and outstanding reviews.
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
                        .Where(f => f.Type == type && !f.IsCompliant && f.DueDate >= cycleStart)
                        .OrderBy(f => f.DueDate)
                        .FirstOrDefault();
                    if (form is null)
                        continue;

                    var openDaysBefore = Person.GetOpenDaysBefore(type, _settings);
                    var windowDays = Math.Max(openDaysBefore, DefaultLookaheadDays);
                    var isOverdue = today.Date > form.DueDate.Date;
                    var inWindow = today.Date >= form.DueDate.Date.AddDays(-windowDays);
                    if (!isOverdue && !inWindow)
                        continue;

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
        public bool IsFormNote => SelectedNoteType == NoteType.Form;
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
        [RelayCommand] private void NavigateToOverview() => CurrentSubViewModel = null; [RelayCommand] private void NavigateToClients() => CurrentSubViewModel = Clients;
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
        [RelayCommand] private void IncreaseNarrativeFont() => NarrativeFontSize = Math.Min(NarrativeFontSize + 2, 28);
        [RelayCommand] private void DecreaseNarrativeFont() => NarrativeFontSize = Math.Max(NarrativeFontSize - 2, 10);
        [RelayCommand] private void ClearNote() => ResetForm();
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
            ClearNoteFields();
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

        [RelayCommand]
        public async Task SubmitNote()
        {
            var errors = new List<string>();

            if (SelectedPerson is null) errors.Add("• Please select a client.");
            if (Status is null) errors.Add("• Please select a status.");
            if (EventDate is null) errors.Add("• Please enter a date.");
            if (string.IsNullOrWhiteSpace(Narrative)) errors.Add("• Please enter a narrative.");
            if (SelectedNoteType is null) errors.Add("• Please select a note type.");

            if (errors.Count > 0)
            {
                var dialog = new UserMessageDialog(string.Join("\n", errors)) { Owner = Application.Current.MainWindow };
                dialog.ShowDialog();
                return;
            }

            try
            {
                if (Status == NoteStatus.Logged)
                {
                    var (passed, reasons) = SelectedPerson!.EvaluateComplianceGate(DateTime.Today,
                        SelectedNoteType == NoteType.Form ? SelectedFormType : null);

                    // Window check is keyed to the NOTE's date, not today. EventDate is
                    // non-null here — validated at the top of SubmitNote — so the
                    // .Value is safe.
                    var windowReasons = SelectedPerson!.EvaluateBillingWindow(EventDate!.Value);

                    if (!passed || windowReasons.Count > 0)
                    {
                        // Combine both sets of reasons for the one dialog. The window
                        // flag drives the Hold outcome: if any window block is present,
                        // a held note becomes ComplianceBlocked rather than HeldForCompliance.
                        _dialogIsWindowBlock = windowReasons.Count > 0;
                        ComplianceFailureReasons = reasons.Concat(windowReasons).ToList();
                        PendingJustification = string.Empty;
                        IsComplianceDialogVisible = true;
                        return;
                    }
                }

                if (!IsEditing)
                    await SubmitNewNoteAsync();
                else
                    await SubmitEditedNoteAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SubmitNote failed: {ex.Message}");
                MessageBox.Show(
                    "Sati encountered an error saving your note. Please try again.",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public void Initialize()
        {
            LoggedInUser = _sessionService.CurrentUser;
            _ = LoadAsync();
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

        [RelayCommand]
        private async Task HoldForCompliance()
        {
            // A window block can't be "held for paperwork" — the note's date sits in a
            // missed-form window and isn't billable for that date. It's saved
            // ComplianceBlocked: not Logged, distinct from Abandoned, an audit record of
            // a unit suppressed by a missed form. A pure paperwork-gate failure keeps the
            // existing HeldForCompliance behavior.
            Status = _dialogIsWindowBlock
                ? NoteStatus.ComplianceBlocked
                : NoteStatus.HeldForCompliance;
            _dialogIsWindowBlock = false;
            IsComplianceDialogVisible = false;
            PendingJustification = string.Empty;
            if (IsEditing)
                await SubmitEditedNoteAsync();
            else
                await SubmitNewNoteAsync();
        }

        [RelayCommand]
        private async Task SendToSupervisor()
        {
            if (string.IsNullOrWhiteSpace(PendingJustification)) return;
            var justification = PendingJustification;
            IsComplianceDialogVisible = false;
            PendingJustification = string.Empty;
            if (IsEditing)
                await SubmitEditedNoteAsync(justification);
            else
                await SubmitNewNoteAsync(justification);
        }

        [RelayCommand]
        private void CancelComplianceDialog()
        {
            IsComplianceDialogVisible = false;
            PendingJustification = string.Empty;
        }

        private async Task SubmitNewNoteAsync(string? caseManagerJustification = null)
        {
            // Capture the client before any reload — LoadPeopleAsync rebuilds the People
            // collection with fresh instances, which drops the current SelectedPerson.
            var keepPersonId = SelectedPerson!.Id;

            var note = Note.Create(Narrative!, EventDate, Status, Minutes, SelectedPerson!.Id, SelectedFormType, SelectedNoteType);
            if (caseManagerJustification is not null)
                note.CaseManagerJustification = caseManagerJustification;
            var savedNote = await _noteService.AddNoteAsync(note);

            if (note.NoteType == NoteType.Form && note.FormType.HasValue &&
                            Status is NoteStatus.Pending or NoteStatus.Logged &&
                            FormStatusRequested is not null)
                await FormStatusRequested(note.FormType.Value);

            await LoadPeopleAsync();
            await LoadMonthlyNotesAsync();
            await LoadUpcomingEventsAsync();
            await NotesLog.ReloadAsync();
            await Clients.ReloadAsync();

            // Re-select the same client against the rebuilt collection so the combobox
            // holds its selection, then clear only the note fields.
            SelectedPerson = People.FirstOrDefault(p => p.Id == keepPersonId);
            ClearNoteFields();
        }

        private async Task SubmitEditedNoteAsync(string? caseManagerJustification = null)
        {
            if (SelectedNote is null)
                return;

            var keepPersonId = SelectedPerson?.Id;

            SelectedNote.Narrative = Narrative!;
            SelectedNote.EventDate = EventDate;
            SelectedNote.Minutes = Minutes ?? 0;
            SelectedNote.Status = Status;
            SelectedNote.NoteType = SelectedNoteType;
            SelectedNote.FormType = SelectedFormType;

            await _noteService.UpdateNoteAsync(SelectedNote);
            NotesView.Refresh();

            var formType = SelectedNote.FormType;
            if (formType.HasValue && Status is NoteStatus.Pending or NoteStatus.Logged)
                MarkFormCompleteRequested?.Invoke(this, formType.Value);

            IsEditing = false;
            await LoadPeopleAsync();
            await LoadMonthlyNotesAsync();
            await LoadUpcomingEventsAsync();
            await NotesLog.ReloadAsync();
            await Clients.ReloadAsync();

            // Keep the same client selected after an edit, mirroring new-note submit.
            SelectedPerson = keepPersonId is null
                ? null
                : People.FirstOrDefault(p => p.Id == keepPersonId.Value);
            ClearNoteFields();
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

                OnPropertyChanged(nameof(BoardItems));
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

        // Full reset, including the selected client. Used only by the Clear button —
        // the one place "start over completely" is the intent.
        private void ResetForm()
        {
            SelectedPerson = null;
            ClearNoteFields();
        }

        // Clears the note-entry fields but leaves SelectedPerson in place, so the case
        // manager can log several notes for the same client in a row. Minutes is reset
        // here too: OnSelectedPersonChanged doesn't cover it, so without this line it
        // would leak from one submitted note into the next.
        private void ClearNoteFields()
        {
            Status = null;
            Narrative = string.Empty;
            EventDate = null;
            SelectedFormType = null;
            SelectedNoteType = null;
            Minutes = null;
        }

        public void EnterEditMode()
        {
            if (SelectedNote is null)
                return;

            IsEditing = true;
            Narrative = SelectedNote.Narrative;
            EventDate = SelectedNote.EventDate;
            Minutes = SelectedNote.Minutes;
            Status = SelectedNote.Status;
            SelectedNoteType = SelectedNote.NoteType;
            SelectedFormType = SelectedNote.FormType;
            SelectedPerson = People.First(p => p.Id == SelectedNote.PersonId);
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
            IsEditing = false;
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