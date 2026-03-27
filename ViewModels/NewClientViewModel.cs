
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using static Sati.Enums;
using System.ComponentModel.DataAnnotations;
using Sati.Data;


namespace Sati
{

    public partial class NewClientViewModel : ObservableValidator
    {

        //FIELDS
        private readonly ISessionService _sessionService;
        //EVENTS

        //PROPERTIES
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
        private DateTime birthDate;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "A short biographical description is required.")]
        private string? bio;

        [ObservableProperty]
        private DateTime effectiveDate;

        [ObservableProperty]
        private WaiverType waiver;

        [ObservableProperty]
        private Person? selectedPerson;

        [ObservableProperty]
        private bool isEditMode;
        public string SubmitButtonLabel => IsEditMode ? "Save Changes" : "Add Client";


        public ObservableCollection<Person> People { get; } = [];

        public Array Waivers => Enum.GetValues(typeof(WaiverType));

        private readonly IPersonService _personService;
        

        //constructor
        public NewClientViewModel(IPersonService personService, ISessionService session)
        {
            _personService = personService;
            _sessionService = session;
            _ = LoadPeopleAsync();
        }


        [RelayCommand]
        private async Task Submit()
        {
            ValidateAllProperties();
            if (HasErrors)
                return;

            if (IsEditMode && SelectedPerson is Person existing)
            {
                existing.FirstName = FirstName!;
                existing.LastName = LastName!;
                existing.BirthDate = BirthDate;
                existing.EffectiveDate = EffectiveDate;
                existing.Waiver = Waiver;
                existing.Bio = Bio!;
                await _personService.EditPersonAsync(existing);
                
                var index = People.IndexOf(existing);
                if (index >= 0)
                    People[index] = existing;  
                IsEditMode = false;
                SelectedPerson = null;
                ClearFields();
                OnPropertyChanged(nameof(SubmitButtonLabel));
            }
            else
            {
                var person = Person.CreatePerson(_sessionService.CurrentUser!.Id, FirstName!, LastName!, Bio!, BirthDate, EffectiveDate, Waiver);
                await _personService.AddPersonAsync(person);
                People.Add(person);
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
            OnPropertyChanged(nameof(SubmitButtonLabel));
            FirstName = person.FirstName; 
            LastName = person.LastName;
            Bio = person.Bio;
            BirthDate = person.BirthDate;
            EffectiveDate= person.EffectiveDate;
            Waiver = person.Waiver;
        }

        private void ClearFields()
        {
            FirstName = string.Empty;
            LastName = string.Empty;
            BirthDate = default;
            EffectiveDate = default;
            Waiver = default;
            Bio = string.Empty;
        }

    }
}
