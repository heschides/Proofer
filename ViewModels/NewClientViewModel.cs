
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using static Proofer.Enums;
using System.ComponentModel.DataAnnotations;
using Proofer.Data;


namespace Proofer
{

    public partial class NewClientViewModel : ObservableValidator
    {

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

        public ObservableCollection<Person> People { get; } = [];

        public Array Waivers => Enum.GetValues(typeof(WaiverType));

        private readonly IPersonService _personService;
        

        //constructor
        public NewClientViewModel(IPersonService personService)
        {
            _personService = personService;
            _ = LoadPeopleAsync();
        }
        
        
        [RelayCommand]
        private async Task Submit()
        {
            ValidateAllProperties();
            if (HasErrors)
                return;
            //Validation above guarantees these are non-null at this point.  ! operator used to suppress IDE warning.
            var person = Person.CreatePerson(FirstName!, LastName!, Bio!, BirthDate, EffectiveDate, Waiver);
          
            var savedPerson = await _personService.AddPersonAsync(person);

            People.Add(savedPerson);
                
            FirstName = string.Empty;
            LastName = string.Empty;
            Bio = string.Empty;
            BirthDate = DateTime.MinValue; 
            EffectiveDate = DateTime.MinValue;
            Waiver = WaiverType.None;
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
            var people = await _personService.GetAllPeopleAsync();
            foreach (var person in people)
                People.Add(person);
        }
    }
}
