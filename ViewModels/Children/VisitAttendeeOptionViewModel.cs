using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;

namespace Sati.ViewModels.Children
{
    public sealed partial class VisitAttendeeOptionViewModel : ObservableObject
    {
        public VisitAttendeeOptionViewModel(PersonContact contact)
        {
            Contact = contact;
        }

        public PersonContact Contact { get; }
        public int ContactId => Contact.Id;
        public string FullName => Contact.FullName;
        public string DisplayLabel => Contact.DisplayLabel;
        public string? Role => string.IsNullOrWhiteSpace(Contact.Relationship)
            ? Contact.Kind.ToString()
            : Contact.Relationship;
        public string? Organization => Contact.Organization;

        [ObservableProperty]
        private bool isSelected;
    }
}
