using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Helpers;
using Sati.Models;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Windows.Threading;

namespace Sati.ViewModels
{
    public partial class NewClientViewModel : ObservableValidator
    {
        // -------------------------------------------------------------------------
        // Services
        // -------------------------------------------------------------------------

        private readonly ISessionService _sessionService;
        private readonly IPersonService _personService;
        private readonly INoteService _noteService;
        private readonly IFormService _formService;
        private readonly ISettingsService _settingsService;
        private readonly IReviewItemService _reviewItemService;

        // Per-consumer journal state. The timer debounces saves to 2s after the last
        // keystroke — Stop()+Start() on every edit means it fires once typing pauses,
        // not once per character. _journalPersonId is the id the CURRENT Journal text
        // belongs to, captured so a debounced save writes to the right record even if
        // selection has since moved. _suppressJournalSave guards the load: assigning
        // Journal from the DB raises OnJournalChanged, which would otherwise start the
        // timer and save the just-loaded text straight back.
        private DispatcherTimer? _journalSaveTimer;
        private int? _journalPersonId;
        private bool _suppressJournalSave;

        [ObservableProperty]
        private string? journal;
        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        public event Func<List<Form>, bool>? ComplianceReviewRequested;
        public event EventHandler? FormComplianceChanged;

        // Raised before a client is deleted. The view answers true to proceed, false
        // to abort — same View-less pattern as ComplianceReviewRequested. Null-coalesced
        // to false at the call site, so if no handler is wired the delete does NOT
        // happen — fail safe, not fail open: a missing handler can't cause silent loss.
        public event Func<bool>? DeleteConfirmationRequested;

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "First name is required.")]
        private string? firstName;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "Last name is required.")]
        private string? lastName;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "Birthdate is required.")]
        private DateTime? birthDate;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "A short biographical description is required.")]
        private string? bio;

        [ObservableProperty]
        private WaiverType waiver;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [CustomValidation(typeof(NewClientViewModel), nameof(ValidateEffectiveDate))]
        private string effectiveDateText = string.Empty;

        [ObservableProperty]
        private Person? selectedPerson;

        [ObservableProperty]
        private bool isEntryPanelOpen = false;

        [ObservableProperty]
        private bool isEditMode;
        [ObservableProperty]
        private bool openWithVR;

        [ObservableProperty]
        private bool hasGuardian;
        [ObservableProperty]
        private string? guardianName;

        [ObservableProperty]
        private string? evergreenId;

        [ObservableProperty]
        private string? phoneNumber;
        [ObservableProperty]
        private string? address;

        [ObservableProperty]
        private string? primaryCareProvider;

        [ObservableProperty]
        private string? healthcareSystemName;

        // Waiver services & employment
        [ObservableProperty]
        private bool isEmployed;

        [ObservableProperty]
        private bool hasHomeSupport;

        [ObservableProperty]
        private bool hasSelfDirectedHomeSupport;

        [ObservableProperty]
        private bool hasSharedLiving;

        [ObservableProperty]
        private bool hasCommunitySupport1To1;

        [ObservableProperty]
        private bool hasCommunitySupportSelfDirected;

        [ObservableProperty]
        private bool hasCommunitySupportDayProgram;

        [ObservableProperty]
        private int dayProgramCount = 1;

        [ObservableProperty]
        private bool hasEmploymentSpecialist;

        [ObservableProperty]
        private bool hasWorkSupports;

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        // Two-parameter overload runs alongside the single-param one below. Its job is
        // the journal handoff: flush the OUTGOING person's journal (using oldValue,
        // before the id is lost), then load the incoming person's journal under the
        // suppress guard so the load doesn't retrigger a save. Ordering matters —
        // flush old, then load new.
        partial void OnSelectedPersonChanged(Person? oldValue, Person? newValue)
        {
            // Flush any pending edit for the person we're leaving. Fire-and-forget is
            // acceptable: the write is a single-column UPDATE and the timer is stopped
            // so it can't also fire.
            _journalSaveTimer?.Stop();
            if (_journalPersonId is int leavingId && !_suppressJournalSave)
                _ = _personService.SaveJournalAsync(leavingId, Journal);

            _ = LoadJournalAsync(newValue);
        }

        partial void OnSelectedPersonChanged(Person? value)
        {
            OnPropertyChanged(nameof(HasSelectedPerson));
            OnPropertyChanged(nameof(SelectedPersonServices));
            OnPropertyChanged(nameof(HasSelectedPersonServices));
            OnPropertyChanged(nameof(ShowsEmploymentTracking));
            OnPropertyChanged(nameof(Q1RDueDate));
            OnPropertyChanged(nameof(Q2RDueDate));
            OnPropertyChanged(nameof(Q3RDueDate));
            OnPropertyChanged(nameof(Q4RDueDate));
            OnPropertyChanged(nameof(PcpDueDate));
            OnPropertyChanged(nameof(CompAssessmentDueDate));
            OnPropertyChanged(nameof(ReclassificationDueDate));
            OnPropertyChanged(nameof(SafetyPlanDueDate));
            OnPropertyChanged(nameof(ReleaseAgencyDueDate));
            OnPropertyChanged(nameof(ReleaseMedicalDueDate));
            OnPropertyChanged(nameof(ReleaseDhhsDueDate));
            RefreshComplianceFlags();
            _ = LoadSelectedPersonNotesAsync(value);
            _ = LoadAppointmentsAsync(value);

            // The panel is persistent now, so it can't be left showing one client's
            // data while another is selected — Submit's edit branch writes to
            // SelectedPerson, and a stale form would overwrite the wrong record.
            // Selection is therefore the only thing that fills this form.
            if (value is null)
            {
                ClearFields();
                IsEditMode = false;
            }
            else
            {
                PopulateFrom(value);
            }
        }

        // Header and button caption both key off edit mode, so one callback covers
        // every path that flips it.
        partial void OnIsEditModeChanged(bool value)
        {
            OnPropertyChanged(nameof(SubmitButtonLabel));
            OnPropertyChanged(nameof(EntryPanelHeader));
        }

        // A day-program client always has at least one program. Clamping here rather
        // than trusting the UI means a hand-typed 0 can't silently zero out that
        // client's community note-review slots. The recursive set terminates: the
        // second pass sees a value already >= 1 and doesn't set again.
        // Employment supports are meaningless without employment. Clearing rather
        // than merely disabling prevents storing a contradiction — unlike the
        // HasGuardian/GuardianName pattern, where the preserved value is real
        // typed data worth protecting.
        partial void OnIsEmployedChanged(bool value)
        {
            if (value)
                return;

            HasEmploymentSpecialist = false;
            HasWorkSupports = false;
        }

        partial void OnDayProgramCountChanged(int value)
        {
            if (value < 1)
                DayProgramCount = 1;
        }

        partial void OnWaiverChanged(WaiverType value)
        {
            OnPropertyChanged(nameof(HasWaiver));
            if (value == WaiverType.None)
                EffectiveDateText = string.Empty;
        }

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------

        public bool HasSelectedPerson => SelectedPerson is not null;

        // Comma-joined list of the selected client's active waiver services, for
        // display only. Empty string when none are set, which the detail panel
        // renders as a hidden section rather than an empty label.
        public string SelectedPersonServices
        {
            get
            {
                if (SelectedPerson is not Person p)
                    return string.Empty;

                var services = new List<string>();

                if (p.HasHomeSupport) services.Add("Home support");
                if (p.HasSelfDirectedHomeSupport) services.Add("Self-directed home support");
                if (p.HasSharedLiving) services.Add("Shared living");
                if (p.HasCommunitySupport1To1) services.Add("Community support 1:1");
                if (p.HasCommunitySupportSelfDirected) services.Add("Community support self-directed");
                if (p.HasCommunitySupportDayProgram)
                    services.Add(p.DayProgramCount > 1
                        ? $"Day program ×{p.DayProgramCount}"
                        : "Day program");
                if (p.HasEmploymentSpecialist) services.Add("Employment specialist");
                if (p.HasWorkSupports) services.Add("Work supports");

                return string.Join(", ", services);
            }
        }

        public bool HasSelectedPersonServices => SelectedPersonServices.Length > 0;

        // Employed and receiving no employment supports from any funding stream —
        // the population whose employment parameters must be tracked quarterly.
        public bool ShowsEmploymentTracking => SelectedPerson?.RequiresEmploymentTracking ?? false;
        public bool AllowComplianceOverride => _sessionService.AllowComplianceOverride;
        public bool HasWaiver => Waiver != WaiverType.None;
        public string SubmitButtonLabel => IsEditMode ? "Save Changes" : "Add Client";
        public string EntryPanelHeader => IsEditMode ? "EDIT CLIENT" : "ADD CLIENT"; public Array Waivers => Enum.GetValues(typeof(WaiverType));

        public ObservableCollection<Note> SelectedPersonNotes { get; } = [];
        public ObservableCollection<Person> People { get; } = [];
        public ObservableCollection<HealthcareSystemOption> HealthcareSystems { get; } = [];

        // Derived, read-only: the most recent Contact-type note's date for the selected
        // client. A window over the already-loaded notes, not a stored field. Selecting
        // into DateTime? before Max means an empty sequence yields null rather than
        // throwing, and the detail panel renders null as a dash.
        public DateTime? LastContact =>
                    SelectedPersonNotes
                        .Where(n => n.NoteType == NoteType.Contact)
                        .Select(n => (DateTime?)n.EventDate)
                        .Max();

        // Latest doctor/dentist appointments for the selected client, loaded on
        // selection (LoadAppointmentsAsync) rather than stored on Person — so the
        // caseload-load path and the Person entity stay untouched. Overdue = more
        // than 365 days since the most recent of that kind; null = none on record,
        // which the Clients column renders as a dash.
        private Appointment? _latestDoctor;
        private Appointment? _latestDentist;

        public DateTime? LastDoctorDate => _latestDoctor?.Date;
        public string? DoctorName => _latestDoctor?.ProviderName;
        public bool IsDoctorOverdue =>
            _latestDoctor is not null && (DateTime.Today - _latestDoctor.Date).TotalDays > 365;

        public DateTime? LastDentistDate => _latestDentist?.Date;
        public string? DentistName => _latestDentist?.ProviderName;
        public bool IsDentistOverdue =>
            _latestDentist is not null && (DateTime.Today - _latestDentist.Date).TotalDays > 365;

        // Due dates
        public DateTime? Q1RDueDate => SelectedPerson?.GetCurrentCycleForm(FormType.Q1R)?.DueDate;
        public DateTime? Q2RDueDate => SelectedPerson?.GetCurrentCycleForm(FormType.Q2R)?.DueDate;
        public DateTime? Q3RDueDate => SelectedPerson?.GetCurrentCycleForm(FormType.Q3R)?.DueDate;
        public DateTime? Q4RDueDate => SelectedPerson?.GetCurrentCycleForm(FormType.Q4R)?.DueDate;
        public DateTime? PcpDueDate => SelectedPerson?.GetCurrentCycleForm(FormType.PCP)?.DueDate;
        public DateTime? CompAssessmentDueDate => SelectedPerson?.GetCurrentCycleForm(FormType.ComprehensiveAssessment)?.DueDate;
        public DateTime? ReclassificationDueDate => SelectedPerson?.GetCurrentCycleForm(FormType.Reclassification)?.DueDate;
        public DateTime? SafetyPlanDueDate => SelectedPerson?.GetCurrentCycleForm(FormType.SafetyPlan)?.DueDate;
        public DateTime? PrivacyPracticesDueDate => SelectedPerson?.GetCurrentCycleForm(FormType.PrivacyPractices)?.DueDate;
        public DateTime? ReleaseAgencyDueDate => SelectedPerson?.GetCurrentCycleForm(FormType.Release_Agency)?.DueDate;
        public DateTime? ReleaseDhhsDueDate => SelectedPerson?.GetCurrentCycleForm(FormType.Release_DHHS)?.DueDate;
        public DateTime? ReleaseMedicalDueDate => SelectedPerson?.GetCurrentCycleForm(FormType.Release_Medical)?.DueDate;

        // Compliance flags
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
        // Constructor
        // -------------------------------------------------------------------------

        public NewClientViewModel(IPersonService personService, ISessionService session,
                           INoteService noteService, IFormService formService, ISettingsService settingsService,
                           IReviewItemService reviewItemService)
        {
            _personService = personService;
            _sessionService = session;
            _noteService = noteService;
            _formService = formService;
            _settingsService = settingsService;
            _reviewItemService = reviewItemService;
            _ = LoadHealthcareOptionsAsync();
        }

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        // The plus no longer means "open the panel" — the panel has its own toggle.
        // It means "switch to add mode": drop the selection (which clears the form
        // through OnSelectedPersonChanged) and make sure the panel is showing, so
        // the click always produces a visible blank form.
        [RelayCommand]
        private void OpenEntryPanel()
        {
            SelectedPerson = null;
            IsEntryPanelOpen = true;
        }

        [RelayCommand]
        private void ToggleEntryPanel() => IsEntryPanelOpen = !IsEntryPanelOpen;

        [RelayCommand]
        private async Task Submit()
        {
            ValidateAllProperties();
            if (HasErrors)
                return;

            var effectiveDate = TryGetEffectiveDate(EffectiveDateText);
            var settings = await _settingsService.LoadAsync();

            if (IsEditMode && SelectedPerson is Person existing)
            {
                var wasNoWaiver = existing.Waiver == WaiverType.None;
                var isAddingWaiver = Waiver != WaiverType.None;

                existing.FirstName = FirstName!;
                existing.LastName = LastName!;
                existing.BirthDate = BirthDate!.Value;
                existing.EffectiveDate = effectiveDate;
                existing.Waiver = Waiver;
                existing.Bio = Bio!;
                existing.OpenWithVR = OpenWithVR;
                existing.HasGuardian = HasGuardian;
                existing.GuardianName = GuardianName;
                existing.EvergreenId = EvergreenId;
                existing.PhoneNumber = PhoneNumber; existing.Address = Address;
                existing.PrimaryCareProvider = PrimaryCareProvider;
                existing.HealthcareSystemName = HealthcareSystemName;
                existing.IsEmployed = IsEmployed;
                existing.HasHomeSupport = HasHomeSupport;
                existing.HasSelfDirectedHomeSupport = HasSelfDirectedHomeSupport;
                existing.HasSharedLiving = HasSharedLiving;
                existing.HasCommunitySupport1To1 = HasCommunitySupport1To1;
                existing.HasCommunitySupportSelfDirected = HasCommunitySupportSelfDirected;
                existing.HasCommunitySupportDayProgram = HasCommunitySupportDayProgram;
                existing.DayProgramCount = HasCommunitySupportDayProgram ? DayProgramCount : 1;
                existing.HasEmploymentSpecialist = HasEmploymentSpecialist;
                existing.HasWorkSupports = HasWorkSupports;

                if (wasNoWaiver && isAddingWaiver && effectiveDate is not null)
                {
                    await _formService.DeleteFormsAsync(existing.Forms);
                    var forms = Person.GenerateFormList(effectiveDate.Value, settings);
                    existing.Forms = forms;
                    var confirmed = ComplianceReviewRequested?.Invoke(existing.Forms) ?? true;
                    if (!confirmed)
                        return;
                }

                await _personService.EditPersonAsync(existing);

                var index = People.IndexOf(existing);
                if (index >= 0)
                {
                    People.RemoveAt(index);
                    People.Insert(index, existing);
                }

                SelectedPerson = null;
            }
            else
            {
                var person = Person.CreatePerson(_sessionService.CurrentUser!.Id,
                                    FirstName!, LastName!, Bio!, BirthDate!.Value, effectiveDate, Waiver, settings);
                person.OpenWithVR = OpenWithVR;
                person.HasGuardian = HasGuardian;
                person.GuardianName = GuardianName;
                person.EvergreenId = EvergreenId;
                person.PhoneNumber = PhoneNumber;
                person.Address = Address;
                person.PrimaryCareProvider = PrimaryCareProvider;
                person.HealthcareSystemName = HealthcareSystemName;
                person.IsEmployed = IsEmployed;
                person.HasHomeSupport = HasHomeSupport;
                person.HasSelfDirectedHomeSupport = HasSelfDirectedHomeSupport;
                person.HasSharedLiving = HasSharedLiving;
                person.HasCommunitySupport1To1 = HasCommunitySupport1To1;
                person.HasCommunitySupportSelfDirected = HasCommunitySupportSelfDirected;
                person.HasCommunitySupportDayProgram = HasCommunitySupportDayProgram;
                person.DayProgramCount = HasCommunitySupportDayProgram ? DayProgramCount : 1;
                person.HasEmploymentSpecialist = HasEmploymentSpecialist;
                person.HasWorkSupports = HasWorkSupports;
                var confirmed = ComplianceReviewRequested?.Invoke(person.Forms) ?? true;
                if (!confirmed)
                    return;
                await _personService.AddPersonAsync(person);
                People.Add(person);
                ClearFields();
            }
        }

        [RelayCommand]
        private async Task RemoveSelectedPerson()
        {
            if (SelectedPerson is null)
                return;

            // No handler wired => false => no delete. The guard fails safe: the only
            // way past this line is an explicit true from the confirmation dialog.
            var confirmed = DeleteConfirmationRequested?.Invoke() ?? false;
            if (!confirmed)
                return;

            // Drop journal tracking before the delete so the selection-change flush
            // (triggered by SelectedPerson = null below) can't write a journal back to
            // the row we're removing.
            _journalSaveTimer?.Stop();
            _journalPersonId = null;

            await _personService.DeletePersonAsync(SelectedPerson);
            People.Remove(SelectedPerson);
            SelectedPerson = null;
        }

        [RelayCommand]
        private void ToggleComplianceOverride()
        {
            _sessionService.AllowComplianceOverride = !_sessionService.AllowComplianceOverride;
            OnPropertyChanged(nameof(AllowComplianceOverride));
        }

        [RelayCommand]
        private async Task ToggleForm(FormType type)
        {
            if (SelectedPerson is null) return;
            var form = SelectedPerson.GetCurrentCycleForm(type);
            if (form is null) return;

            // Toggle through the sanctioned door. Marking compliant uses the form's
            // own DueDate as the completion date — the on-time assumption, matching the
            // creation dialog's default. A detail-panel toggle is almost always "this
            // was done on schedule"; the precise-late-date case lives in the dialog.
            if (form.IsCompliant)
                form.Reset();
            else
                form.MarkComplete(form.DueDate);

            await _formService.UpdateFormAsync(form);
            RefreshComplianceFlags();
            FormComplianceChanged?.Invoke(this, EventArgs.Empty);
        }

        // -------------------------------------------------------------------------
        // Public methods
        // -------------------------------------------------------------------------

        public void LoadPersonForEdit(Person person)
        {
            PopulateFrom(person);
            IsEntryPanelOpen = true;
        }

        // Fills the form without touching panel visibility. Split from
        // LoadPersonForEdit so a selection change can repopulate a panel the user
        // already has open, while double-click additionally forces it open.
        private void PopulateFrom(Person person)
        {
            IsEditMode = true;
            FirstName = person.FirstName; LastName = person.LastName;
            Bio = person.Bio;
            BirthDate = person.BirthDate;
            EffectiveDateText = person.EffectiveDate?.ToString("MM/dd") ?? string.Empty;
            Waiver = person.Waiver;
            OpenWithVR = person.OpenWithVR;
            HasGuardian = person.HasGuardian;
            GuardianName = person.GuardianName;
            EvergreenId = person.EvergreenId;
            PhoneNumber = person.PhoneNumber;
            Address = person.Address; PrimaryCareProvider = person.PrimaryCareProvider;
            HealthcareSystemName = person.HealthcareSystemName;
            IsEmployed = person.IsEmployed;
            HasHomeSupport = person.HasHomeSupport;
            HasSelfDirectedHomeSupport = person.HasSelfDirectedHomeSupport;
            HasSharedLiving = person.HasSharedLiving;
            HasCommunitySupport1To1 = person.HasCommunitySupport1To1;
            HasCommunitySupportSelfDirected = person.HasCommunitySupportSelfDirected;
            HasCommunitySupportDayProgram = person.HasCommunitySupportDayProgram;
            DayProgramCount = person.DayProgramCount;
            HasEmploymentSpecialist = person.HasEmploymentSpecialist;
            HasWorkSupports = person.HasWorkSupports;
        }

        public async Task ReloadAsync()
        {
            People.Clear();
            var people = await _personService.GetAllPeopleAsync(_sessionService.CurrentUser!.Id);
            foreach (var person in people)
                People.Add(person);
        }

        // -------------------------------------------------------------------------
        // Private methods
        // -------------------------------------------------------------------------

        private async Task LoadPeopleAsync()
        {
            if (_sessionService.CurrentUser is null)
                return;

            var people = await _personService.GetAllPeopleAsync(_sessionService.CurrentUser.Id);

            // Clear immediately before repopulating, not at the top: an awaited
            // query that fails or runs long would otherwise leave the grid empty
            // in the meantime. Clearing here also makes this method safe to call
            // twice, which is what the constructor/ReloadAsync race exploited.
            People.Clear();
            foreach (var person in people)
                People.Add(person);
        }

        private async Task LoadSelectedPersonNotesAsync(Person? person)
        {
            SelectedPersonNotes.Clear();
            if (person is null)
            {
                OnPropertyChanged(nameof(LastContact));
                return;
            }
            var notes = await _noteService.GetAllByPersonAsync(person.Id);
            foreach (var note in notes)
                SelectedPersonNotes.Add(note);

            // LastContact is computed from the notes just loaded, so it can't refresh
            // until they're here. (The notes arrive async after the selection changes,
            // which is why this can't live in OnSelectedPersonChanged.)
            OnPropertyChanged(nameof(LastContact));
        }

        // Loads the selected client's most-recent doctor and dentist appointments
        // for the Clients-tab column. A per-selection read like the notes load above
        // — deliberately NOT part of GetAllPeopleAsync, so the caseload load stays
        // blob-free and Person gains no appointment columns. Null person clears both.
        private async Task LoadAppointmentsAsync(Person? person)
        {
            if (person is null)
            {
                _latestDoctor = null;
                _latestDentist = null;
            }
            else
            {
                var (medical, dental) = await _reviewItemService.GetLatestAppointmentsAsync(person.Id);
                _latestDoctor = medical;
                _latestDentist = dental;
            }

            OnPropertyChanged(nameof(LastDoctorDate));
            OnPropertyChanged(nameof(DoctorName));
            OnPropertyChanged(nameof(IsDoctorOverdue));
            OnPropertyChanged(nameof(LastDentistDate));
            OnPropertyChanged(nameof(DentistName));
            OnPropertyChanged(nameof(IsDentistOverdue));
        }
        

        // Every keystroke restarts the 2s countdown; it elapses only after typing
        // pauses. The suppress guard skips the load-time assignment. Timer is created
        // lazily on first real edit so a session that never touches a journal never
        // spins one up.
        partial void OnJournalChanged(string? value)
        {
            if (_suppressJournalSave)
                return;

            _journalSaveTimer ??= CreateJournalTimer();
            _journalSaveTimer.Stop();
            _journalSaveTimer.Start();
        }

        private DispatcherTimer CreateJournalTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += async (s, e) =>
            {
                timer.Stop();
                if (_journalPersonId is int id)
                    await _personService.SaveJournalAsync(id, Journal);
            };
            return timer;
        }

        // Loads the incoming person's journal under the suppress guard so the
        // assignment doesn't trip OnJournalChanged and save it straight back.
        // _journalPersonId is set to the loaded person so any subsequent edit saves
        // against the right record.
        private async Task LoadJournalAsync(Person? person)
        {
            if (person is null)
            {
                _suppressJournalSave = true;
                Journal = string.Empty;
                _journalPersonId = null;
                _suppressJournalSave = false;
                return;
            }

            var text = await _personService.GetJournalAsync(person.Id);

            _suppressJournalSave = true;
            Journal = text ?? string.Empty;
            _journalPersonId = person.Id;
            _suppressJournalSave = false;
        }

        // Immediate flush for shutdown/user-switch. Public so ShellViewModel can call
        // it in the same teardown path that saves the Scratchpad. Stops the timer
        // first so a pending tick can't double-write.
        public async Task FlushJournalAsync()
        {
            _journalSaveTimer?.Stop();
            if (_journalPersonId is int id)
                await _personService.SaveJournalAsync(id, Journal);
        }

        // Loads the configurable system names from Settings and projects each into a
        // HealthcareSystemOption for the combobox. Normalize re-applies the "Other"
        // floor and ordering in case the stored list was hand-edited.
        private async Task LoadHealthcareOptionsAsync()
        {
            var settings = await _settingsService.LoadAsync();
            HealthcareSystems.Clear();
            foreach (var name in HealthcareSystemOptions.Normalize(settings.HealthcareSystems))
                HealthcareSystems.Add(new HealthcareSystemOption(name));
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

        private void ClearFields()
        {
            FirstName = string.Empty;
            LastName = string.Empty;
            BirthDate = default;
            EffectiveDateText = string.Empty;
            Waiver = default;
            Bio = string.Empty;
            OpenWithVR = false;
            HasGuardian = false;
            GuardianName = string.Empty;
            EvergreenId = string.Empty;
            PhoneNumber = string.Empty;
            Address = string.Empty;
            PrimaryCareProvider = string.Empty;
            HealthcareSystemName = null;
            IsEmployed = false;
            HasHomeSupport = false;
            HasSelfDirectedHomeSupport = false;
            HasSharedLiving = false;
            HasCommunitySupport1To1 = false;
            HasCommunitySupportSelfDirected = false;
            HasCommunitySupportDayProgram = false;
            DayProgramCount = 1;
            HasEmploymentSpecialist = false;
            HasWorkSupports = false;
            ClearErrors();
        }

        // -------------------------------------------------------------------------
        // Validation
        // -------------------------------------------------------------------------

        // Effective date is optional — empty string is valid.
        // If provided, must be in MM/DD format.
        public static ValidationResult? ValidateEffectiveDate(string value, ValidationContext context)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ValidationResult.Success;

            if (!DateTime.TryParseExact(value.Trim(), "MM/dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return new ValidationResult("Date must be in MM/DD format.");

            return ValidationResult.Success;
        }

        // Returns null if empty or unparseable — caller decides what to do with a
        // missing effective date rather than receiving a sentinel like DateTime.MinValue.
        private static DateTime? TryGetEffectiveDate(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            if (!DateTime.TryParseExact(input.Trim(), "MM/dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return null;

            var candidate = new DateTime(DateTime.Today.Year, parsed.Month, parsed.Day);
            return candidate > DateTime.Today ? candidate.AddYears(-1) : candidate;
        }
    }
}