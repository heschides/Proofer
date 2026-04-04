using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Sati.Data;
using Sati.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Windows.Devices.I2c.Provider;


namespace Sati
{
    public partial class MainWindowViewModel : ObservableObject
    {

        //SERVICES
        private readonly IPersonService _personService;
        private readonly INoteService _noteService;
        private readonly ISettingsService _settingsService;
        private Settings? _settings;
        private readonly IScratchpadService _scratchpadService;
        private Scratchpad? _scratchpad;
        private readonly IIncentiveService _incentiveService;
        private Incentive? _incentive;
        private readonly ISessionService _sessionService;
        private readonly IUpcomingEventService _upcomingEventService;
        private readonly IFormService _formService;


        //compliance flags
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


        //EVENTS
        public event EventHandler<bool>? OpenClientsWindowRequested;
        public event EventHandler<bool>? OpenSettingsWindowRequested;
        public event EventHandler<bool>? PromptSchedulerRequested;
        public event EventHandler<FormType>? MarkFormCompleteRequested;
        public event EventHandler? OpenScratchpadHistoryRequested;
        public event EventHandler<bool>? OpenNotesWindowRequested;

        //PROPERTIES

        [ObservableProperty] private Person? selectedPerson;
        partial void OnSelectedPersonChanged(Person? value)
        {
            LoadNotesForPersonAsync(value);
            RefreshComplianceFlags();
            IsEditing = false;
            Status = null;
            Narrative = string.Empty;
            EventDate = null;
            Units = null;
            SelectedNoteType = null;
            SelectedFormType = null;
        }

        [ObservableProperty] private int? units;
        [ObservableProperty] private DateTime? eventDate;
        [ObservableProperty] private int? duration;
        [ObservableProperty] private string? narrative;
        [ObservableProperty] private NoteStatus? status;
        [ObservableProperty] private bool isEditing = false;
        [ObservableProperty] private Note? selectedNote = null;
        [ObservableProperty] private string? searchText;
        [ObservableProperty] private NoteStatus? filterStatus;
        [ObservableProperty] private User? loggedInUser;
        [ObservableProperty] private string scratchpadContent = string.Empty;
        [ObservableProperty] private NoteType? selectedNoteType;
        [ObservableProperty] private int daysScheduled;
        [ObservableProperty] private bool isSchedulerOpen = false;
        [ObservableProperty] private bool sortByDate = true;
        [ObservableProperty] private FormType? selectedFormType;
        [ObservableProperty] private double narrativeFontSize = 14;
        [ObservableProperty] private double scratchpadFontSize = 14;
        partial void OnSelectedFormTypeChanged(FormType? value)
        {
            if (value is null) return;
            if (SelectedPerson is null) return;
            if (!string.IsNullOrWhiteSpace(Narrative)) return;

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
        partial void OnSortByDateChanged(bool value)
        {
            OnPropertyChanged(nameof(FormEvents));
            OnPropertyChanged(nameof(VisitEvents));
            OnPropertyChanged(nameof(ContactEvents));
        }

        public ICollectionView NotesView { get; }
        public IEnumerable<UpcomingEvent> FormEvents => SortByDate
     ? UpcomingEvents.Where(e => e.Kind == UpcomingEventKind.OpenReview || e.Kind == UpcomingEventKind.LateReview).OrderBy(e => e.Date)
     : UpcomingEvents.Where(e => e.Kind == UpcomingEventKind.OpenReview || e.Kind == UpcomingEventKind.LateReview).OrderBy(e => e.Kind);

        public IEnumerable<UpcomingEvent> VisitEvents => SortByDate
            ? UpcomingEvents.Where(e => e.Kind == UpcomingEventKind.ScheduledVisit).OrderBy(e => e.Date)
            : UpcomingEvents.Where(e => e.Kind == UpcomingEventKind.ScheduledVisit).OrderBy(e => e.Kind);

        public IEnumerable<UpcomingEvent> ContactEvents => SortByDate
            ? UpcomingEvents.Where(e => e.Kind == UpcomingEventKind.ScheduledContact).OrderBy(e => e.Date)
            : UpcomingEvents.Where(e => e.Kind == UpcomingEventKind.ScheduledContact).OrderBy(e => e.Kind);
        partial void OnFilterStatusChanged(NoteStatus? value) => NotesView.Refresh();
        public static Array NoteStatusOptions => Enum.GetValues(typeof(NoteStatus));
        public ObservableCollection<Note> Notes { get; } = [];
        public ObservableCollection<Person> People { get; set; } = [];
        public ObservableCollection<UpcomingEvent> UpcomingEvents { get; set; } = [];
        public Array FormTypes => Enum.GetValues(typeof(FormType));

        public int SafeThreshold => Threshold > 0 ? Threshold : 1;
        public bool IsFormNote => SelectedNoteType == NoteType.Form;

        //Constructor
        public MainWindowViewModel(IPersonService personService, INoteService noteService, ISettingsService settingsService, IScratchpadService scratchpadService, IIncentiveService incentiveService, ISessionService sessionService, IUpcomingEventService upcomingEventService, IFormService formService)
        {
            _personService = personService;
            _noteService = noteService;
            _settingsService = settingsService;
            _scratchpadService = scratchpadService;
            _incentiveService = incentiveService;
            NotesView = CollectionViewSource.GetDefaultView(Notes);
            NotesView.Filter = FilterNotes;
            _sessionService = sessionService;
            _upcomingEventService = upcomingEventService;
            _formService = formService;
        }

        //Commands
        [RelayCommand]
        public void OpenClientList()
        {
            OpenClientsWindowRequested?.Invoke(this, true);
        }
        [RelayCommand]
        public void OpenSettingsWindow()
        {
            OpenSettingsWindowRequested?.Invoke(this, true);
        }
        [RelayCommand]
        public async Task SubmitNote()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Narrative) || SelectedPerson == null)
                    return;


                if (!IsEditing)
                {
                    var note = Note.Create(Narrative, EventDate, Status, Units, SelectedPerson.Id, SelectedFormType, SelectedNoteType);
                    var savedNote = await _noteService.AddNoteAsync(note);

                    Notes.Insert(0, savedNote);
                    SelectedPerson?.Notes.Add(savedNote);
                    await LoadMonthlyNotesAsync();
                    if (EventDate.HasValue &&
        (EventDate.Value.Month != DateTime.Now.Month ||
         EventDate.Value.Year != DateTime.Now.Year))
                    {
                        MessageBox.Show(
                            $"This note's date ({EventDate.Value:MMM d, yyyy}) is outside the current month and will not appear in this month's productivity totals.",
                            "Note Outside Current Month",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    NotesView.Refresh();
                    var formType = SelectedFormType;
                    if (formType.HasValue && (Status == NoteStatus.Pending || Status == NoteStatus.Logged))
                        MarkFormCompleteRequested?.Invoke(this, formType.Value);
                    //SelectedPerson = null;
                    Status = null;
                    Narrative = string.Empty;
                    EventDate = null;
                    Units = null;
                    Duration = null;
                    SelectedFormType = null;
                    SelectedNoteType = null;
                    await LoadUpcomingEventsAsync();

                }
                else
                {
                    if (SelectedNote == null)
                        return;

                    var note = SelectedNote!;
                    note.Narrative = Narrative;
                    note.EventDate = EventDate;
                    note.Units = Units ?? 0;
                    note.Status = Status;
                    await _noteService.UpdateNoteAsync(note);
                    await LoadMonthlyNotesAsync();
                    NotesView.Refresh();

                    var formType = SelectedNote.FormType;
                    if (formType.HasValue && (Status == NoteStatus.Pending || Status == NoteStatus.Logged))
                        MarkFormCompleteRequested?.Invoke(this, formType.Value);
                    IsEditing = false;
                    Status = null;
                    Narrative = string.Empty;
                    EventDate = null;
                    Units = null;
                    Duration = null;
                    SelectedFormType = null;
                    SelectedNoteType = null;
                    await LoadUpcomingEventsAsync();
                }
            }


            catch (Exception ex)
            {
                Debug.WriteLine($"SubmitNote failed: {ex.Message}");
                MessageBox.Show(
                    "Sati encountered an error saving your note. Please try again.  Tell Josh.",
                    "Save Error",
                     MessageBoxButton.OK,
            MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void OpenNotesWindow()
        {
            OpenNotesWindowRequested?.Invoke(this, true);
        }

        [RelayCommand]
        private async Task DeleteNote()
        {
            if (SelectedNote != null)
            {
                await _noteService.DeleteNoteAsync(SelectedNote);
                Notes.Remove(SelectedNote);
                SelectedPerson?.Notes.Remove(SelectedNote);
                await LoadMonthlyNotesAsync();
                await LoadUpcomingEventsAsync();
                SelectedNote = null;
            }
        }
        [RelayCommand]
        private async Task ToggleForm(FormType type)
        {
            if (SelectedPerson is null)
                return;

            var form = SelectedPerson.GetCurrentCycleForm(type);
            if (form is null)
                return;

            form.IsCompliant = !form.IsCompliant;
            await _formService.UpdateFormAsync(form);

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

        [RelayCommand]
        private void OpenScratchpadHistory() =>
    OpenScratchpadHistoryRequested?.Invoke(this, EventArgs.Empty);

        [RelayCommand]
        private void OpenScheduler()
        {
            IsSchedulerOpen = !IsSchedulerOpen;
        }

        [RelayCommand]
        private void IncreaseNarrativeFont() =>
    NarrativeFontSize = Math.Min(NarrativeFontSize + 2, 28);

        [RelayCommand]
        private void DecreaseNarrativeFont() =>
            NarrativeFontSize = Math.Max(NarrativeFontSize - 2, 10);

        [RelayCommand]
        private void IncreaseScratchpadFont() =>
            ScratchpadFontSize = Math.Min(ScratchpadFontSize + 2, 28);

        [RelayCommand]
        private void DecreaseScratchpadFont() =>
            ScratchpadFontSize = Math.Max(ScratchpadFontSize - 2, 10);

        //Methods

        public void Initialize()
        {
            LoggedInUser = _sessionService.CurrentUser;
            _ = LoadAsync();
        }
        private async Task LoadAsync()
        {
            try
            {
                if (LoggedInUser is null)
                    return;
                _settings = await _settingsService.LoadAsync();
                await LoadPeopleAsync();
                await _noteService.UpdateAbandonedNotesAsync(_settings.AbandonedAfterDays);
                await LoadMonthlyNotesAsync();
                await LoadUpcomingEventsAsync();

                var (incentive, wasCreated) = await _incentiveService.GetOrCreateAsync(
                    LoggedInUser!.Id, DateTime.Now.Month, DateTime.Now.Year);
                _incentive = incentive;

                if (wasCreated)
                    PromptSchedulerRequested?.Invoke(this, true);

                _scratchpad = await _scratchpadService.LoadTodayAsync(LoggedInUser!.Id);
                ScratchpadContent = _scratchpad!.Content;

                StartAbandonmentTimer();
                // StartScratchpadTimer();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadAsync failed: {ex.Message}");
                MessageBox.Show("Sati encountered an error loading your data. Please restart the application.  Tell Josh.",
                                "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        partial void OnSearchTextChanged(string? value)
        {
            NotesView.Refresh();
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

            var matchesStatus = FilterStatus == null || note.Status == FilterStatus;

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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load people: {ex.Message}");
            }
        }

        private void StartScratchpadTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            timer.Tick += async (s, e) =>
            {
                if (_scratchpad is null) return;
                _scratchpad.Content = ScratchpadContent;
                await _scratchpadService.SaveAsync(_scratchpad);
            };
            timer.Start();
        }

        public async Task SaveScratchpadAsync(string content)
        {
            try
            {
                if (_scratchpad is null) return;
                _scratchpad.Content = content;
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SAVING SCRATCHPAD: '{content}'");
                await _scratchpadService.SaveAsync(_scratchpad);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveScratchpadAsync failed: {ex.Message}");
                MessageBox.Show(
                    "Sati encountered an error saving your scratchpad. Your work may not have been saved.  Tell Josh.",
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public void EnterEditMode()
        {
            if (SelectedNote is null)
                return;

            IsEditing = true;

            Narrative = SelectedNote.Narrative;
            EventDate = SelectedNote.EventDate;
            Units = SelectedNote.Units;
            Status = SelectedNote.Status;
            SelectedPerson = People.First(p => p.Id == SelectedNote.PersonId);
        }

        private DateTime _lastAbandonmentCheck = DateTime.Now;
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

        partial void OnSelectedNoteTypeChanged(NoteType? value)
        {
            OnPropertyChanged(nameof(IsFormNote));
            if (value != NoteType.Form)
                SelectedFormType = null;

            if (value is null) return;
            if (!string.IsNullOrWhiteSpace(Narrative)) return;

            Narrative = value.Value switch
            {
                NoteType.Visit => _settings?.VisitTemplate ?? string.Empty,
                NoteType.Contact => _settings?.ContactTemplate ?? string.Empty,
                _ => string.Empty
            };
        }
        private async Task LoadMonthlyNotesAsync()
        {
            _monthlyNotes = await _noteService.GetMonthlyNotesAsync();
            OnPropertyChanged(nameof(PendingUnits));
            OnPropertyChanged(nameof(LoggedUnits));
            OnPropertyChanged(nameof(AbandonedUnits));
            OnPropertyChanged(nameof(EstimatedIncentive));
            OnPropertyChanged(nameof(Threshold));
            OnPropertyChanged(nameof(SafeThreshold));
        }

        private async Task LoadUpcomingEventsAsync()
        {
            if (LoggedInUser is null) return;
            var settings = await _settingsService.LoadAsync();
            var events = _upcomingEventService.GenerateEvents(People, settings);
            UpcomingEvents.Clear();
            foreach (var e in events)
                UpcomingEvents.Add(e);
            OnPropertyChanged(nameof(FormEvents));
            OnPropertyChanged(nameof(VisitEvents));
            OnPropertyChanged(nameof(ContactEvents));
        }

        public async Task MarkFormCompleteAsync(FormType formType)
        {
            if (SelectedPerson is null) return;

            var form = SelectedPerson.GetCurrentCycleForm(formType);
            if (form is null) return;

            form.IsCompliant = true;
            await _formService.UpdateFormAsync(form);
            RefreshComplianceFlags();
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

        //COMPUTED PROPERTIES AND METHOD FOR UNIT LOGIC
        private List<Note> _monthlyNotes = [];
        public int? PendingUnits => _monthlyNotes
      .Where(n => n.Status == NoteStatus.Pending)
      .Sum(n => n.Units);

        public int? LoggedUnits => _monthlyNotes
            .Where(n => n.Status == NoteStatus.Logged)
            .Sum(n => n.Units);

        public int? AbandonedUnits => _monthlyNotes
            .Where(n => n.Status == NoteStatus.Abandoned)
            .Sum(n => n.Units);

        public decimal EstimatedIncentive => _incentive?.Calculate(LoggedUnits ?? 0) ?? 0;
        public int Threshold => _incentive?.Threshold ?? 0;

        public async Task RefreshIncentiveAsync()
        {
            var (incentive, _) = await _incentiveService.GetOrCreateAsync(
                LoggedInUser!.Id, DateTime.Now.Month, DateTime.Now.Year);
            _incentive = incentive;
            OnPropertyChanged(nameof(Threshold));
            OnPropertyChanged(nameof(SafeThreshold));
            OnPropertyChanged(nameof(EstimatedIncentive));
        }


    }
}
