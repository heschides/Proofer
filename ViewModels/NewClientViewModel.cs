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
        private string? phoneNumber;

        [ObservableProperty]
        private string? address;

        [ObservableProperty]
        private string? primaryCareProvider;

        [ObservableProperty]
        private string? healthcareSystemName;

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnSelectedPersonChanged(Person? value)
        {
            OnPropertyChanged(nameof(HasSelectedPerson));
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
            IsEntryPanelOpen = false;
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
        public bool AllowComplianceOverride => _sessionService.AllowComplianceOverride;
        public bool HasWaiver => Waiver != WaiverType.None;
        public string SubmitButtonLabel => IsEditMode ? "Save Changes" : "Add Client";
        public Array Waivers => Enum.GetValues(typeof(WaiverType));

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
                   INoteService noteService, IFormService formService, ISettingsService settingsService)
        {
            _personService = personService;
            _sessionService = session;
            _noteService = noteService;
            _formService = formService;
            _settingsService = settingsService;
            _ = LoadPeopleAsync();
            _ = LoadHealthcareOptionsAsync();
        }

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private void OpenEntryPanel()
        {
            ClearFields();
            IsEditMode = false;
            OnPropertyChanged(nameof(SubmitButtonLabel));
            IsEntryPanelOpen = true;
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
                existing.EffectiveDate = effectiveDate;
                existing.Waiver = Waiver;
                existing.Bio = Bio!;
                existing.OpenWithVR = OpenWithVR;
                existing.HasGuardian = HasGuardian;
                existing.GuardianName = GuardianName;
                existing.PhoneNumber = PhoneNumber;
                existing.Address = Address;
                existing.PrimaryCareProvider = PrimaryCareProvider;
                existing.HealthcareSystemName = HealthcareSystemName;

                if (wasNoWaiver && isAddingWaiver && effectiveDate is not null)
                {
                    await _formService.DeleteFormsAsync(existing.Forms);
                    var forms = Person.GenerateFormList(effectiveDate.Value);
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

                IsEditMode = false;
                SelectedPerson = null;
                ClearFields();
                OnPropertyChanged(nameof(SubmitButtonLabel));
                IsEntryPanelOpen = false;
            }
            else
            {
                var person = Person.CreatePerson(_sessionService.CurrentUser!.Id,
                                    FirstName!, LastName!, Bio!, BirthDate!.Value, effectiveDate, Waiver, settings);
                person.OpenWithVR = OpenWithVR;
                person.HasGuardian = HasGuardian;
                person.GuardianName = GuardianName;
                person.PhoneNumber = PhoneNumber;
                person.Address = Address;
                person.PrimaryCareProvider = PrimaryCareProvider;
                person.HealthcareSystemName = HealthcareSystemName;
                var confirmed = ComplianceReviewRequested?.Invoke(person.Forms) ?? true;
                if (!confirmed)
                    return;
                await _personService.AddPersonAsync(person);
                People.Add(person);
                IsEntryPanelOpen = false;
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
            IsEditMode = true;
            IsEntryPanelOpen = true;
            OnPropertyChanged(nameof(SubmitButtonLabel));
            FirstName = person.FirstName;
            LastName = person.LastName;
            Bio = person.Bio;
            BirthDate = person.BirthDate;
            EffectiveDateText = person.EffectiveDate?.ToString("MM/dd") ?? string.Empty;
            Waiver = person.Waiver;
            OpenWithVR = person.OpenWithVR;
            HasGuardian = person.HasGuardian;
            GuardianName = person.GuardianName;
            PhoneNumber = person.PhoneNumber;
            Address = person.Address;
            PrimaryCareProvider = person.PrimaryCareProvider;
            HealthcareSystemName = person.HealthcareSystemName;
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
            PhoneNumber = string.Empty;
            Address = string.Empty;
            PrimaryCareProvider = string.Empty;
            HealthcareSystemName = null;
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