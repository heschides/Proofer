
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;



namespace Sati
{

    public partial class NewClientViewModel : ObservableValidator
    {

        //FIELDS
        private readonly ISessionService _sessionService;
        private readonly IPersonService _personService;
        private readonly INoteService _noteService;
        private readonly IFormService _formService;


        //EVENTS
        public event Func<List<Form>, bool>? ComplianceReviewRequested;

        //PROPERTIES
        [ObservableProperty][NotifyDataErrorInfo][Required(ErrorMessage = "First name is required.")] private string? firstName;
        [ObservableProperty][NotifyDataErrorInfo][Required(ErrorMessage = "Last name is required.")] private string? lastName;
        [ObservableProperty][NotifyDataErrorInfo][Required(ErrorMessage = "Birthdate is required.")] private DateTime? birthDate;
        [ObservableProperty][NotifyDataErrorInfo][Required(ErrorMessage = "A short biographical description is required.")] private string? bio;
        [ObservableProperty] private WaiverType waiver;
        [ObservableProperty][NotifyDataErrorInfo][CustomValidation(typeof(NewClientViewModel), nameof(ValidateEffectiveDate))] private string effectiveDateText = string.Empty;
        [ObservableProperty] private Person? selectedPerson;
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
        [ObservableProperty] private bool isEntryPanelOpen = false;

       

        [ObservableProperty] private bool isEditMode;
        public bool HasSelectedPerson => SelectedPerson is not null;
        public bool AllowComplianceOverride => _sessionService.AllowComplianceOverride;

        public ObservableCollection<Note> SelectedPersonNotes { get; } = [];

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

        private async Task LoadSelectedPersonNotesAsync(Person? person)
        {
            SelectedPersonNotes.Clear();
            if (person is null) return;
            var notes = await _noteService.GetAllByPersonAsync(person.Id);
            foreach (var note in notes)
                SelectedPersonNotes.Add(note);
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

        //COMPUTED PROPERTIES
        public bool HasWaiver => Waiver != WaiverType.None;
        public string SubmitButtonLabel => IsEditMode ? "Save Changes" : "Add Client";
        public ObservableCollection<Person> People { get; } = [];
        public Array Waivers => Enum.GetValues(typeof(WaiverType));

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

        //PROPERTY CALLBACKS
        partial void OnWaiverChanged(WaiverType value)
        {
            OnPropertyChanged(nameof(HasWaiver));
            if (value == WaiverType.None)
                EffectiveDateText = string.Empty;
        }
        //constructor
        public NewClientViewModel(IPersonService personService, ISessionService session, INoteService noteService, IFormService formService)
        {
            _personService = personService;
            _sessionService = session;
            _ = LoadPeopleAsync();
            _noteService = noteService;
            _formService = formService;
        }

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

            var effectiveDate = TryGetEffectiveDate(EffectiveDateText)!;
            if (IsEditMode && SelectedPerson is Person existing)
            {
                var wasNoWaiver = existing.Waiver == WaiverType.None;
                var isAddingWaiver = Waiver != WaiverType.None;
                existing.FirstName = FirstName!;
                existing.LastName = LastName!;
                existing.BirthDate = BirthDate!.Value;
                existing.EffectiveDate = TryGetEffectiveDate(EffectiveDateText);
                existing.Waiver = Waiver;
                existing.Bio = Bio!;
                if (wasNoWaiver && isAddingWaiver)
                {
                    var forms = Person.GenerateFormList(existing.EffectiveDate);
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
                var person = Person.CreatePerson(_sessionService.CurrentUser!.Id, FirstName!, LastName!, Bio!, BirthDate!.Value, effectiveDate, Waiver);
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

        private async Task LoadPeopleAsync()
        {
            if (_sessionService.CurrentUser is null)
                return;
            var people = await _personService.GetAllPeopleAsync(_sessionService.CurrentUser.Id);
            foreach (var person in people)
                People.Add(person);
        }

        public void LoadPersonForEdit(Person person)
        {
            IsEditMode = true;
            IsEntryPanelOpen = true;
            OnPropertyChanged(nameof(SubmitButtonLabel));
            FirstName = person.FirstName;
            LastName = person.LastName;
            Bio = person.Bio;
            BirthDate = person.BirthDate;
            EffectiveDateText = person.EffectiveDate.ToString("MM/dd");
            Waiver = person.Waiver;
        }

        [RelayCommand]
        private async Task ToggleForm(FormType type)
        {
            if (SelectedPerson is null) return;
            var form = SelectedPerson.GetCurrentCycleForm(type);
            if (form is null) return;
            form.IsCompliant = !form.IsCompliant;
            await _formService.UpdateFormAsync(form);
            RefreshComplianceFlags();
        }

        private void ClearFields()
        {
            FirstName = string.Empty;
            LastName = string.Empty;
            BirthDate = default;
            EffectiveDateText = string.Empty;
            Waiver = default;
            Bio = string.Empty;
            ClearErrors();
        }

        //METHODS

        //validation
        public static ValidationResult? ValidateEffectiveDate(string value, ValidationContext context)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new ValidationResult("Effective date is required.");

            if (!DateTime.TryParseExact(value.Trim(), "MM/dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return new ValidationResult("Date must be in MM/DD format.");

            return ValidationResult.Success;
        }

        private static DateTime TryGetEffectiveDate(string input)
        {
            if (!DateTime.TryParseExact(input.Trim(), "MM/dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return DateTime.MinValue;

            var candidate = new DateTime(DateTime.Today.Year, parsed.Month, parsed.Day);
            return candidate > DateTime.Today ? candidate.AddYears(-1) : candidate;
        }

    }
}
