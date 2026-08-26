using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Helpers;
using Sati.Models;
using Sati.Reporting;
using Sati.Services;
using Sati.ViewModels.Children;
using Sati.ViewModels.ClientDocuments;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
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
        private readonly IPersonContactService _personContactService;
        private readonly IATRequestService _atRequestService;
        private readonly ATRequestPdfExporter _atRequestPdfExporter;

        public DhhsFormsViewModel DhhsForms { get; }

        /// <summary>
        /// Social Security number, shared with the DHHS forms workspace so both screens
        /// display, store, and reveal it the same way. Its own audited save; never part
        /// of the demographic save, which must not carry the number.
        /// </summary>
        public SsnPanelViewModel SsnPanel { get; }
        public AgencyReleaseViewModel AgencyRelease { get; }

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
        private int _journalLoadVersion;
        private readonly JournalSaveCoordinator _journalSaveCoordinator = new();
        private readonly JournalDraftTracker _journalDraftTracker = new();

        [ObservableProperty]
        private string? journal;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasJournalSaveWarning))]
        private string? journalSaveWarning;
        public bool HasJournalSaveWarning => !string.IsNullOrWhiteSpace(JournalSaveWarning);
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEditJournal))]
        private bool isJournalLoading;
        public bool CanEditJournal => !IsJournalLoading && _journalPersonId is not null;
        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        public event Func<List<Form>, bool>? ComplianceReviewRequested;
        public event EventHandler? FormComplianceChanged;

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
        private Gender gender = Gender.Unknown;

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
        private bool isClientEditorOpen;
        [ObservableProperty]
        private bool isClientListCompact;
        [ObservableProperty]
        private int clientWorkspaceTabIndex;
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
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        private string? email;
        [ObservableProperty]
        private string? address;

        [ObservableProperty]
        private string? maineCareId;
        [ObservableProperty]
        private string? diagnosisCode;
        [ObservableProperty]
        private int? placeOfService;
        [ObservableProperty]
        private string? billingStreet;
        [ObservableProperty]
        private string? billingCity;
        [ObservableProperty]
        private string? billingState;
        [ObservableProperty]
        private string? billingZip;

        [ObservableProperty]
        private string? primaryCareProvider;

        [ObservableProperty]
        private string? healthcareSystemName;

        [ObservableProperty]
        private bool caseManagerIsRepPayee;

        [ObservableProperty]
        private bool caseManagerIsDhhsRepresentative;

        [ObservableProperty]
        private bool usesModivcare;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [CustomValidation(typeof(NewClientViewModel), nameof(ValidateRepPayeeMonthlyIncome))]
        private string repPayeeMonthlyIncomeText = string.Empty;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [CustomValidation(typeof(NewClientViewModel), nameof(ValidateRepPayeeRegularCheckRequestNeeds))]
        private string? repPayeeRegularCheckRequestNeeds;

        // Support-network editor. The list is persisted separately from Person so
        // client edits cannot accidentally replace or delete the contact graph.
        [ObservableProperty] private PersonContact? selectedContact;
        [ObservableProperty] private bool isContactEditorOpen;
        [ObservableProperty] private bool isEditingContact;
        [ObservableProperty] private string contactFirstName = string.Empty;
        [ObservableProperty] private string contactLastName = string.Empty;
        [ObservableProperty] private PersonContactKind contactKind;
        [ObservableProperty] private string? contactRelationship;
        [ObservableProperty] private string? contactOrganization;
        [ObservableProperty] private string? contactPhone;
        [ObservableProperty] private string? contactEmail;
        [ObservableProperty] private bool contactIsEmergencyContact;
        [ObservableProperty] private bool contactHasActiveRelease;
        [ObservableProperty] private string contactStatusMessage = string.Empty;

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
            if (_journalPersonId is int leavingId &&
                !_suppressJournalSave &&
                _journalDraftTracker.IsDirty(leavingId, Journal))
                _ = TrySaveJournalAsync(leavingId, Journal);

            _ = LoadJournalAsync(newValue);
        }

        partial void OnSelectedPersonChanged(Person? value)
        {
            OnPropertyChanged(nameof(HasSelectedPerson));
            OnPropertyChanged(nameof(ShowClientWorkspace));
            OnPropertyChanged(nameof(SelectedPersonServices));
            OnPropertyChanged(nameof(HasSelectedPersonServices));
            OnPropertyChanged(nameof(SelectedPersonComplianceReasons));
            OnPropertyChanged(nameof(HasSelectedPersonComplianceIssues));
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
            _ = LoadContactsAsync(value);
            _ = LoadAtRequestsAsync(value);
            SsnPanel.SetPerson(value?.Id);
            DhhsForms.SetPerson(value);
            AgencyRelease.SetPerson(value);
            RefreshUpcomingItems(value);

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
                if (!IsClientEditorOpen)
                    IsEditMode = false;
            }
        }

        // Header and button caption both key off edit mode, so one callback covers
        // every path that flips it.
        partial void OnIsEditModeChanged(bool value)
        {
            OnPropertyChanged(nameof(SubmitButtonLabel));
            OnPropertyChanged(nameof(EntryPanelHeader));
            OnPropertyChanged(nameof(IsAddingClient));
        }

        partial void OnIsClientEditorOpenChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowClientWorkspace));
            OnPropertyChanged(nameof(IsAddingClient));
        }

        partial void OnIsContactEditorOpenChanged(bool value)
        {
            OnPropertyChanged(nameof(ContactEditorHeader));
            OnPropertyChanged(nameof(ContactSaveButtonLabel));
        }

        partial void OnIsEditingContactChanged(bool value)
        {
            OnPropertyChanged(nameof(ContactEditorHeader));
            OnPropertyChanged(nameof(ContactSaveButtonLabel));
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

        partial void OnCaseManagerIsRepPayeeChanged(bool value)
        {
            if (!value)
            {
                RepPayeeMonthlyIncomeText = string.Empty;
                RepPayeeRegularCheckRequestNeeds = string.Empty;
            }

            ValidateProperty(RepPayeeMonthlyIncomeText, nameof(RepPayeeMonthlyIncomeText));
            ValidateProperty(
                RepPayeeRegularCheckRequestNeeds,
                nameof(RepPayeeRegularCheckRequestNeeds));
        }

        partial void OnSelectedConsumerFilterChanged(string value)
        {
            PeopleView.Refresh();
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
        public bool ShowClientWorkspace => HasSelectedPerson || IsClientEditorOpen;
        public bool IsAddingClient => IsClientEditorOpen && !IsEditMode;

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
        public IReadOnlyList<string> SelectedPersonComplianceReasons =>
            GetComplianceReasons(SelectedPerson, DateTime.Today);
        public bool HasSelectedPersonComplianceIssues => SelectedPersonComplianceReasons.Count > 0;

        internal static IReadOnlyList<string> GetComplianceReasons(Person? person, DateTime today) =>
            person?.EvaluateComplianceGate(today).Reasons ?? [];

        // Employed and receiving no employment supports from any funding stream —
        // the population whose employment parameters must be tracked quarterly.
        public bool ShowsEmploymentTracking => SelectedPerson?.RequiresEmploymentTracking ?? false;
        public bool AllowComplianceOverride => _sessionService.AllowComplianceOverride;
        public bool HasWaiver => Waiver != WaiverType.None;
        public string SubmitButtonLabel => IsEditMode ? "Save Changes" : "Add Client";
        public string EntryPanelHeader => IsEditMode ? "EDIT CLIENT" : "ADD CLIENT"; public Array Waivers => Enum.GetValues(typeof(WaiverType));
        public Array Genders => Enum.GetValues(typeof(Gender));

        public ObservableCollection<Note> SelectedPersonNotes { get; } = [];
        public ObservableCollection<Person> People { get; } = [];
        public ICollectionView PeopleView { get; }
        public IReadOnlyList<string> ConsumerFilters { get; } =
        [
            "All consumers",
            "Case manager is representative payee",
            "Case manager is DHHS representative",
            "Uses Modivcare",
            "Open with VR",
            "Home support",
            "Self-directed home support",
            "Community support 1:1",
            "Community support self-directed",
            "Day program",
            "Shared living",
            "Employment specialist",
            "Work supports"
        ];

        [ObservableProperty]
        private string selectedConsumerFilter = "All consumers";
        public ObservableCollection<HealthcareSystemOption> HealthcareSystems { get; } = [];
        public ObservableCollection<PersonContact> Contacts { get; } = [];
        public ObservableCollection<UpcomingEvent> SelectedPersonUpcomingItems { get; } = [];
        public bool HasSelectedPersonUpcomingItems => SelectedPersonUpcomingItems.Count > 0;
        public Array ContactKinds => Enum.GetValues(typeof(PersonContactKind));
        public string ContactEditorHeader => IsEditingContact ? "EDIT CONTACT" : "ADD CONTACT";
        public string ContactSaveButtonLabel => IsEditingContact ? "Save Contact" : "Add Contact";

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
                           IReviewItemService reviewItemService,
                           IPersonContactService personContactService,
                           IATRequestService atRequestService,
                           ATRequestPdfExporter atRequestPdfExporter,
                           DhhsFormsViewModel dhhsForms,
                           AgencyReleaseViewModel agencyRelease,
                           SsnPanelViewModel ssnPanel)
        {
            _personService = personService;
            _sessionService = session;
            _noteService = noteService;
            _formService = formService;
            _settingsService = settingsService;
            _reviewItemService = reviewItemService;
            _personContactService = personContactService;
            _atRequestService = atRequestService;
            _atRequestPdfExporter = atRequestPdfExporter;
            DhhsForms = dhhsForms;
            SsnPanel = ssnPanel;
            AgencyRelease = agencyRelease;
            PeopleView = CollectionViewSource.GetDefaultView(People);
            PeopleView.Filter = MatchesConsumerFilter;
            _ = LoadHealthcareOptionsAsync();
        }

        // ---------------------------------------------------------------------
        // AT requests filed for this client
        // ---------------------------------------------------------------------

        public ObservableCollection<ATRequestListItem> SelectedPersonAtRequests { get; } = [];

        public bool HasSelectedPersonAtRequests => SelectedPersonAtRequests.Count > 0;

        /// <summary>
        /// Raised when a regenerated AT request PDF is ready to be written to
        /// disk. The view owns the file dialog; this view model owns the bytes.
        /// </summary>
        public event EventHandler<ATRequestPdfReadyEventArgs>? AtRequestPdfReady;

        public event EventHandler<ATRequestProblemEventArgs>? AtRequestProblem;

        private async Task LoadAtRequestsAsync(Person? person)
        {
            SelectedPersonAtRequests.Clear();
            OnPropertyChanged(nameof(HasSelectedPersonAtRequests));

            if (person is null)
                return;

            var personId = person.Id;
            var requests = await _atRequestService.GetAllForPersonAsync(personId);

            // Selection can move while the query is in flight. Never show one
            // client's payment requests under another's name.
            if (SelectedPerson?.Id != personId)
                return;

            foreach (var row in requests)
                SelectedPersonAtRequests.Add(row);

            OnPropertyChanged(nameof(HasSelectedPersonAtRequests));
        }

        /// <summary>
        /// Rebuilds the PDF for a filed request from the stored record.
        ///
        /// FAITHFUL, NOT REFRESHED. Every figure comes from the request itself —
        /// the frozen client and case-manager details, the stored tax amount, the
        /// passthrough rate the request was published under, and the recorded
        /// attestation. The agency's current rate is passed only as the fallback
        /// a draft needs; a published request ignores it.
        /// </summary>
        [RelayCommand]
        private async Task RegenerateAtRequestPdfAsync(ATRequestListItem? row)
        {
            if (row is null)
                return;

            var request = await _atRequestService.GetByIdAsync(row.Id);
            if (request is null)
            {
                AtRequestProblem?.Invoke(this, new ATRequestProblemEventArgs(
                    "Request Not Found",
                    "This AT request is no longer available. The list has been refreshed."));
                await LoadAtRequestsAsync(SelectedPerson);
                return;
            }

            var settings = await _settingsService.LoadAsync();
            var content = _atRequestPdfExporter.Generate(request, settings.PassthroughRate, DateTime.UtcNow);

            AtRequestPdfReady?.Invoke(this, new ATRequestPdfReadyEventArgs(
                content, ATRequestPdfExporter.SuggestedFileName(request)));
        }

        private void RefreshUpcomingItems(Person? person)
        {
            SelectedPersonUpcomingItems.Clear();
            OnPropertyChanged(nameof(HasSelectedPersonUpcomingItems));
            if (person is null) return;

            // The dashboard intentionally shows only items inside its configured
            // action window. This compact preview serves a different purpose: show
            // the selected person's next work even when it has not opened yet.
            var formItems = person.Forms
                .Where(form => !form.IsCompliant)
                .Select(form => new UpcomingEvent
                {
                    ClientName = person.FullName,
                    Title = Person.FormDisplayName(form.Type),
                    Date = form.DueDate,
                    Kind = form.DueDate.Date < DateTime.Today
                        ? UpcomingEventKind.LateReview
                        : UpcomingEventKind.OpenReview
                });

            var scheduledItems = SelectedPersonNotes
                .Where(note => note.Status == NoteStatus.Scheduled &&
                               note.EventDate.HasValue &&
                               note.EventDate.Value.Date >= DateTime.Today)
                .Select(note => new UpcomingEvent
                {
                    ClientName = person.FullName,
                    Title = note.NoteType switch
                    {
                        NoteType.Contact => "Scheduled Contact",
                        NoteType.Form => "Scheduled Form",
                        NoteType.Reminder => "Reminder",
                        _ => "Scheduled Visit"
                    },
                    Date = note.EventDate!.Value,
                    Kind = note.NoteType switch
                    {
                        NoteType.Contact => UpcomingEventKind.ScheduledContact,
                        NoteType.Form => UpcomingEventKind.ScheduledForm,
                        NoteType.Reminder => UpcomingEventKind.ScheduledReminder,
                        _ => UpcomingEventKind.ScheduledVisit
                    }
                });

            var items = formItems.Concat(scheduledItems)
                .OrderBy(item => item.Date)
                .Take(4)
                .ToList();

            foreach (var item in items)
                SelectedPersonUpcomingItems.Add(item);
            OnPropertyChanged(nameof(HasSelectedPersonUpcomingItems));
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
            IsEditMode = false;
            IsClientEditorOpen = true;
            ClientWorkspaceTabIndex = 0;
        }

        [RelayCommand]
        private void BeginClientEdit()
        {
            if (SelectedPerson is not Person person) return;
            PopulateFrom(person);
            IsClientEditorOpen = true;
            ClientWorkspaceTabIndex = 0;
        }

        [RelayCommand]
        private void CancelClientEdit()
        {
            if (SelectedPerson is Person person)
                PopulateFrom(person);
            else
                ClearFields();
            IsClientEditorOpen = false;
            IsEditMode = false;
        }

        [RelayCommand]
        private void ToggleEntryPanel() => IsEntryPanelOpen = !IsEntryPanelOpen;

        [RelayCommand]
        private void ToggleClientList() => IsClientListCompact = !IsClientListCompact;

        [RelayCommand]
        private void BeginAddContact()
        {
            if (SelectedPerson is null)
                return;

            SelectedContact = null;
            IsEditingContact = false;
            ClearContactEditor();
            IsContactEditorOpen = true;
        }

        [RelayCommand]
        private void BeginEditContact(PersonContact? contact)
        {
            if (contact is null)
                return;

            SelectedContact = contact;
            IsEditingContact = true;
            ContactFirstName = contact.FirstName;
            ContactLastName = contact.LastName;
            ContactKind = contact.Kind;
            ContactRelationship = contact.Relationship;
            ContactOrganization = contact.Organization;
            ContactPhone = contact.Phone;
            ContactEmail = contact.Email;
            ContactIsEmergencyContact = contact.IsEmergencyContact;
            ContactHasActiveRelease = contact.HasActiveRelease;
            ContactStatusMessage = string.Empty;
            IsContactEditorOpen = true;
        }

        [RelayCommand]
        private void CancelContactEdit()
        {
            IsContactEditorOpen = false;
            IsEditingContact = false;
            SelectedContact = null;
            ClearContactEditor();
        }

        [RelayCommand]
        private async Task SaveContact()
        {
            if (SelectedPerson is null)
                return;

            if (string.IsNullOrWhiteSpace(ContactFirstName) ||
                string.IsNullOrWhiteSpace(ContactLastName))
            {
                ContactStatusMessage = "First and last name are required.";
                return;
            }

            var contact = IsEditingContact && SelectedContact is not null
                ? SelectedContact
                : new PersonContact { PersonId = SelectedPerson.Id };

            contact.FirstName = ContactFirstName;
            contact.LastName = ContactLastName;
            contact.Kind = ContactKind;
            contact.Relationship = ContactRelationship;
            contact.Organization = ContactOrganization;
            contact.Phone = ContactPhone;
            contact.Email = ContactEmail;
            contact.IsEmergencyContact = ContactIsEmergencyContact;
            contact.HasActiveRelease = ContactHasActiveRelease;

            await _personContactService.SaveAsync(contact);
            await LoadContactsAsync(SelectedPerson);

            IsContactEditorOpen = false;
            IsEditingContact = false;
            SelectedContact = null;
            ClearContactEditor();
        }

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
                existing.Gender = Gender;
                existing.EffectiveDate = effectiveDate;
                existing.Waiver = Waiver;
                existing.Bio = Bio!;
                existing.OpenWithVR = OpenWithVR;
                existing.HasGuardian = HasGuardian;
                existing.GuardianName = GuardianName;
                existing.EvergreenId = EvergreenId;
                existing.PhoneNumber = PhoneNumber; existing.Address = Address;
                existing.Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim();
                existing.MaineCareId = MaineCareId;
                existing.DiagnosisCode = DiagnosisCode?.Trim().ToUpperInvariant();
                existing.PlaceOfService = PlaceOfService;
                existing.BillingStreet = BillingStreet;
                existing.BillingCity = BillingCity;
                existing.BillingState = BillingState?.Trim().ToUpperInvariant();
                existing.BillingZip = BillingZip;
                existing.PrimaryCareProvider = PrimaryCareProvider;
                existing.HealthcareSystemName = HealthcareSystemName;
                existing.CaseManagerIsRepPayee = CaseManagerIsRepPayee;
                existing.CaseManagerIsDhhsRepresentative = CaseManagerIsDhhsRepresentative;
                existing.UsesModivcare = UsesModivcare;
                existing.RepPayeeMonthlyIncome = ParseRepPayeeMonthlyIncome();
                existing.RepPayeeRegularCheckRequestNeeds = CaseManagerIsRepPayee
                    ? RepPayeeRegularCheckRequestNeeds?.Trim()
                    : null;
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
                SelectedPerson = existing;
            }
            else
            {
                var person = Person.CreatePerson(_sessionService.CurrentUser!.Id,
                                    FirstName!, LastName!, Bio!, BirthDate!.Value, effectiveDate, Waiver, settings);
                person.Gender = Gender;
                person.OpenWithVR = OpenWithVR;
                person.HasGuardian = HasGuardian;
                person.GuardianName = GuardianName;
                person.EvergreenId = EvergreenId;
                person.PhoneNumber = PhoneNumber;
                person.Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim();
                person.Address = Address;
                person.MaineCareId = MaineCareId;
                person.DiagnosisCode = DiagnosisCode?.Trim().ToUpperInvariant();
                person.PlaceOfService = PlaceOfService;
                person.BillingStreet = BillingStreet;
                person.BillingCity = BillingCity;
                person.BillingState = BillingState?.Trim().ToUpperInvariant();
                person.BillingZip = BillingZip;
                person.PrimaryCareProvider = PrimaryCareProvider;
                person.HealthcareSystemName = HealthcareSystemName;
                person.CaseManagerIsRepPayee = CaseManagerIsRepPayee;
                person.CaseManagerIsDhhsRepresentative = CaseManagerIsDhhsRepresentative;
                person.UsesModivcare = UsesModivcare;
                person.RepPayeeMonthlyIncome = ParseRepPayeeMonthlyIncome();
                person.RepPayeeRegularCheckRequestNeeds = CaseManagerIsRepPayee
                    ? RepPayeeRegularCheckRequestNeeds?.Trim()
                    : null;
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
                SelectedPerson = person;
            }

            PeopleView.Refresh();
            IsClientEditorOpen = false;
            IsEditMode = false;
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
            RefreshUpcomingItems(SelectedPerson);
            FormComplianceChanged?.Invoke(this, EventArgs.Empty);
        }

        // -------------------------------------------------------------------------
        // Public methods
        // -------------------------------------------------------------------------

        public void LoadPersonForEdit(Person person)
        {
            PopulateFrom(person);
            IsClientEditorOpen = true;
            ClientWorkspaceTabIndex = 0;
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
            Gender = person.Gender;
            EffectiveDateText = person.EffectiveDate?.ToString("MM/dd") ?? string.Empty;
            Waiver = person.Waiver;
            OpenWithVR = person.OpenWithVR;
            HasGuardian = person.HasGuardian;
            GuardianName = person.GuardianName;
            EvergreenId = person.EvergreenId;
            PhoneNumber = person.PhoneNumber;
            Email = person.Email;
            Address = person.Address; PrimaryCareProvider = person.PrimaryCareProvider;
            MaineCareId = person.MaineCareId;
            DiagnosisCode = person.DiagnosisCode;
            PlaceOfService = person.PlaceOfService;
            BillingStreet = person.BillingStreet;
            BillingCity = person.BillingCity;
            BillingState = person.BillingState;
            BillingZip = person.BillingZip;
            HealthcareSystemName = person.HealthcareSystemName;
            CaseManagerIsRepPayee = person.CaseManagerIsRepPayee;
            CaseManagerIsDhhsRepresentative = person.CaseManagerIsDhhsRepresentative;
            UsesModivcare = person.UsesModivcare;
            RepPayeeMonthlyIncomeText = person.RepPayeeMonthlyIncome?.ToString(
                "0.00",
                CultureInfo.CurrentCulture) ?? string.Empty;
            RepPayeeRegularCheckRequestNeeds = person.RepPayeeRegularCheckRequestNeeds;
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
            if (SelectedPerson?.Id != person.Id)
                return;
            foreach (var note in notes)
                SelectedPersonNotes.Add(note);
            RefreshUpcomingItems(person);

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

        private async Task LoadContactsAsync(Person? person)
        {
            Contacts.Clear();
            IsContactEditorOpen = false;
            IsEditingContact = false;
            SelectedContact = null;

            if (person is null)
                return;

            var personId = person.Id;
            var contacts = await _personContactService.GetActiveByPersonAsync(personId);

            // The selection can change while the query is in flight. Never show the
            // outgoing consumer's support network under the incoming consumer.
            if (SelectedPerson?.Id != personId)
                return;

            foreach (var contact in contacts)
                Contacts.Add(contact);
        }

        private void ClearContactEditor()
        {
            ContactFirstName = string.Empty;
            ContactLastName = string.Empty;
            ContactKind = PersonContactKind.Personal;
            ContactRelationship = string.Empty;
            ContactOrganization = string.Empty;
            ContactPhone = string.Empty;
            ContactEmail = string.Empty;
            ContactIsEmergencyContact = false;
            ContactHasActiveRelease = false;
            ContactStatusMessage = string.Empty;
        }
        

        // Every keystroke restarts the 2s countdown; it elapses only after typing
        // pauses. The suppress guard skips the load-time assignment. Timer is created
        // lazily on first real edit so a session that never touches a journal never
        // spins one up.
        partial void OnJournalChanged(string? value)
        {
            if (_suppressJournalSave)
                return;
            if (_journalPersonId is not int personId)
                return;
            if (!_journalDraftTracker.IsDirty(personId, value))
            {
                _journalSaveTimer?.Stop();
                return;
            }

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
                    await TrySaveJournalAsync(id, Journal);
            };
            return timer;
        }

        // Loads the incoming person's journal under the suppress guard so the
        // assignment doesn't trip OnJournalChanged and save it straight back.
        // _journalPersonId is set to the loaded person so any subsequent edit saves
        // against the right record.
        private async Task LoadJournalAsync(Person? person)
        {
            var loadVersion = Interlocked.Increment(ref _journalLoadVersion);
            _suppressJournalSave = true;
            Journal = string.Empty;
            _journalPersonId = null;
            _journalDraftTracker.Clear();
            _suppressJournalSave = false;
            OnPropertyChanged(nameof(CanEditJournal));

            if (person is null)
            {
                IsJournalLoading = false;
                return;
            }

            IsJournalLoading = true;
            try
            {
                var text = await _personService.GetJournalAsync(person.Id);
                if (loadVersion != _journalLoadVersion || SelectedPerson?.Id != person.Id)
                    return;

                _suppressJournalSave = true;
                Journal = text ?? string.Empty;
                _journalPersonId = person.Id;
                _journalDraftTracker.Load(person.Id, Journal);
                _suppressJournalSave = false;
                JournalSaveWarning = null;
                OnPropertyChanged(nameof(CanEditJournal));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Journal load failed for person {person.Id}: {ex.Message}");
                if (loadVersion == _journalLoadVersion && SelectedPerson?.Id == person.Id)
                    JournalSaveWarning = "The journal could not be loaded from the cloud. Other client details remain available; retry by selecting this client again when connected.";
            }
            finally
            {
                if (loadVersion == _journalLoadVersion)
                    IsJournalLoading = false;
            }
        }

        // Immediate flush for shutdown/user-switch. Public so ShellViewModel can call
        // it in the same teardown path that saves the Scratchpad. Stops the timer
        // first so a pending tick can't double-write.
        public async Task<bool> FlushJournalAsync()
        {
            _journalSaveTimer?.Stop();
            if (_journalPersonId is int id && _journalDraftTracker.IsDirty(id, Journal))
                return await TrySaveJournalAsync(id, Journal);
            return true;
        }

        // Called by the host BEFORE a reminder is written to this client's journal.
        // The writer prepends to the journal AS STORED, so an edit still sitting in
        // the debounce window has to be committed first: otherwise the entry lands
        // on top of text the database has not seen, and this page's next save
        // replaces the entry with its own pre-reminder copy of the journal.
        // Scoped to the person actually shown — another client's pending edit is
        // not this reminder's business.
        public async Task FlushJournalIfCurrentAsync(int personId)
        {
            if (_journalPersonId == personId)
                await FlushJournalAsync();
        }

        // Applies a journal that was written elsewhere — a reminder added from the
        // note screen. Assigned under the suppress guard with the timer stopped, so
        // the incoming text is not saved straight back as though it were typed
        // here. Silently ignored when a different client is on screen: the entry is
        // already stored and appears the next time that client is selected.
        public void ApplyExternalJournal(int personId, string? journal)
        {
            if (_journalPersonId != personId)
                return;

            _journalSaveTimer?.Stop();
            _suppressJournalSave = true;
            Journal = journal ?? string.Empty;
            _journalDraftTracker.Load(personId, Journal);
            _suppressJournalSave = false;

            // The write succeeded, so any earlier warning on this band is stale.
            JournalSaveWarning = null;
        }

        private async Task<bool> TrySaveJournalAsync(int personId, string? content)
        {
            var result = await _journalSaveCoordinator.TrySaveAsync(
                () => _personService.SaveJournalAsync(personId, content));
            if (result.Succeeded)
            {
                _journalDraftTracker.MarkSaved(personId, content);
                JournalSaveWarning = null;
                return true;
            }

            Debug.WriteLine($"Journal save failed for person {personId}: {result.Error?.Message}");
            JournalSaveWarning = "The journal has not reached the cloud yet. Your text remains on screen; Sati will try again when the next save is requested.";
            return false;
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
            OnPropertyChanged(nameof(SelectedPersonComplianceReasons));
            OnPropertyChanged(nameof(HasSelectedPersonComplianceIssues));
            // Person is a domain object rather than an observable row VM. Raising
            // People refreshes the roster bindings after a form toggle so its
            // compliance tint changes immediately.
            OnPropertyChanged(nameof(People));
        }

        private bool MatchesConsumerFilter(object item) => item is Person person && SelectedConsumerFilter switch
        {
            "All consumers" => true,
            "Case manager is representative payee" => person.CaseManagerIsRepPayee,
            "Case manager is DHHS representative" => person.CaseManagerIsDhhsRepresentative,
            "Uses Modivcare" => person.UsesModivcare,
            "Open with VR" => person.OpenWithVR,
            "Home support" => person.HasHomeSupport,
            "Self-directed home support" => person.HasSelfDirectedHomeSupport,
            "Community support 1:1" => person.HasCommunitySupport1To1,
            "Community support self-directed" => person.HasCommunitySupportSelfDirected,
            "Day program" => person.HasCommunitySupportDayProgram,
            "Shared living" => person.HasSharedLiving,
            "Employment specialist" => person.HasEmploymentSpecialist,
            "Work supports" => person.HasWorkSupports,
            _ => true
        };

        private void ClearFields()
        {
            FirstName = string.Empty;
            LastName = string.Empty;
            BirthDate = null;
            Gender = Gender.Unknown;
            EffectiveDateText = string.Empty;
            Waiver = default;
            Bio = string.Empty;
            OpenWithVR = false;
            HasGuardian = false;
            GuardianName = string.Empty;
            EvergreenId = string.Empty;
            PhoneNumber = string.Empty;
            Email = string.Empty;
            Address = string.Empty;
            MaineCareId = string.Empty;
            DiagnosisCode = string.Empty;
            PlaceOfService = null;
            BillingStreet = string.Empty;
            BillingCity = string.Empty;
            BillingState = string.Empty;
            BillingZip = string.Empty;
            PrimaryCareProvider = string.Empty;
            HealthcareSystemName = null;
            CaseManagerIsRepPayee = false;
            CaseManagerIsDhhsRepresentative = false;
            UsesModivcare = false;
            RepPayeeMonthlyIncomeText = string.Empty;
            RepPayeeRegularCheckRequestNeeds = string.Empty;
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

        public static ValidationResult? ValidateRepPayeeMonthlyIncome(
            string value,
            ValidationContext context)
        {
            var viewModel = (NewClientViewModel)context.ObjectInstance;
            if (!viewModel.CaseManagerIsRepPayee)
                return ValidationResult.Success;

            if (!TryParseRepPayeeMonthlyIncome(value, out var amount))
                return new ValidationResult("Enter a valid monthly dollar amount.");

            var errors = RepresentativePayeeRules.Validate(
                true,
                amount,
                viewModel.RepPayeeRegularCheckRequestNeeds);
            return errors.TryGetValue("repPayeeMonthlyIncome", out var messages)
                ? new ValidationResult(messages[0])
                : ValidationResult.Success;
        }

        public static ValidationResult? ValidateRepPayeeRegularCheckRequestNeeds(
            string? value,
            ValidationContext context)
        {
            var viewModel = (NewClientViewModel)context.ObjectInstance;
            if (!viewModel.CaseManagerIsRepPayee)
                return ValidationResult.Success;

            _ = TryParseRepPayeeMonthlyIncome(
                viewModel.RepPayeeMonthlyIncomeText,
                out var amount);
            var errors = RepresentativePayeeRules.Validate(true, amount, value);
            return errors.TryGetValue("repPayeeRegularCheckRequestNeeds", out var messages)
                ? new ValidationResult(messages[0])
                : ValidationResult.Success;
        }

        private decimal? ParseRepPayeeMonthlyIncome() =>
            CaseManagerIsRepPayee && TryParseRepPayeeMonthlyIncome(
                RepPayeeMonthlyIncomeText,
                out var amount)
                ? amount
                : null;

        private static bool TryParseRepPayeeMonthlyIncome(string? value, out decimal amount) =>
            decimal.TryParse(
                value,
                NumberStyles.Currency,
                CultureInfo.CurrentCulture,
                out amount);

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
