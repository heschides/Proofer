using System;
using System.Collections.Generic;
using System.Text;
using Proofer.Models;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using static Proofer.Enums;
using System.Threading.Tasks;
using System.Drawing.Text;
using Proofer.Data;


namespace Proofer
{
    public partial class NewClientViewModel : ObservableObject
    {

        [ObservableProperty]
        private string? firstName;

        [ObservableProperty]
        private string? lastName;

        [ObservableProperty]
        private Person? selectedPerson;

        [ObservableProperty]
        private DateTime birthDate;

        [ObservableProperty]
        private DateTime effectiveDate;

        [ObservableProperty]
        private WaiverType waiver;

        [ObservableProperty]
        private string? bio;

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
            if (string.IsNullOrWhiteSpace(FirstName) ||
             string.IsNullOrWhiteSpace(LastName) ||
             string.IsNullOrWhiteSpace(Bio))

            {
                return;
            }

            var person = Person.CreatePerson(FirstName, LastName, Bio, BirthDate, EffectiveDate, Waiver);
          
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
