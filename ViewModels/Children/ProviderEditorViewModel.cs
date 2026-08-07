using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;

namespace Sati.ViewModels.Children
{
    // Observable editing wrapper around one Provider — the same write-through lens
    // as ATRequestEditorViewModel. INPC stays out of the POCO; each property reads
    // and writes the entity, so Save just persists it.
    //
    // Two non-obvious bits:
    //  - The four waiver-service bools each map to ONE bit of the OfferedServices
    //    [Flags] enum: set with "| flag", clear with "& ~flag".
    //  - ProvidesPassthroughService drives the reveal of the three passthrough
    //    fields; the XAML binds their visibility to it, so it only needs to raise.
    public partial class ProviderEditorViewModel : ObservableObject
    {
        private readonly Provider _provider;

        public ProviderEditorViewModel(Provider provider)
        {
            _provider = provider;
        }

        public Provider Provider => _provider;

        // Enum.GetValues<T>() per your style guide; backs the Type combo.
        public static Array ProviderTypeOptions => Enum.GetValues<ProviderType>();

        public ProviderType Type
        {
            get => _provider.Type;
            set { if (_provider.Type != value) { _provider.Type = value; OnPropertyChanged(); } }
        }

        public string Name
        {
            get => _provider.Name;
            set { if (_provider.Name != value) { _provider.Name = value; OnPropertyChanged(); } }
        }

        public string? Street
        {
            get => _provider.Street;
            set { if (_provider.Street != value) { _provider.Street = value; OnPropertyChanged(); } }
        }

        public string? City
        {
            get => _provider.City;
            set { if (_provider.City != value) { _provider.City = value; OnPropertyChanged(); } }
        }

        public string? State
        {
            get => _provider.State;
            set { if (_provider.State != value) { _provider.State = value; OnPropertyChanged(); } }
        }

        public string? Zip
        {
            get => _provider.Zip;
            set { if (_provider.Zip != value) { _provider.Zip = value; OnPropertyChanged(); } }
        }

        public string? PrimaryContact
        {
            get => _provider.PrimaryContact;
            set { if (_provider.PrimaryContact != value) { _provider.PrimaryContact = value; OnPropertyChanged(); } }
        }

        public string? Phone
        {
            get => _provider.Phone;
            set { if (_provider.Phone != value) { _provider.Phone = value; OnPropertyChanged(); } }
        }

        // ---- Waiver services: one bool per [Flags] bit ----
        // Set a bit with OR (| flag); clear it with AND-NOT (& ~flag). The guard
        // suppresses a redundant notify when the bit is already in the target state.

        public bool OffersHomeSupport
        {
            get => _provider.OfferedServices.HasFlag(WaiverService.HomeSupport);
            set
            {
                var updated = value ? _provider.OfferedServices | WaiverService.HomeSupport
                                    : _provider.OfferedServices & ~WaiverService.HomeSupport;
                if (_provider.OfferedServices != updated)
                {
                    _provider.OfferedServices = updated;
                    OnPropertyChanged();
                }
            }
        }

        public bool OffersCommunitySupport
        {
            get => _provider.OfferedServices.HasFlag(WaiverService.CommunitySupport);
            set
            {
                var updated = value ? _provider.OfferedServices | WaiverService.CommunitySupport
                                    : _provider.OfferedServices & ~WaiverService.CommunitySupport;
                if (_provider.OfferedServices != updated)
                {
                    _provider.OfferedServices = updated;
                    OnPropertyChanged();
                }
            }
        }

        public bool OffersSelfDirection
        {
            get => _provider.OfferedServices.HasFlag(WaiverService.SelfDirection);
            set
            {
                var updated = value ? _provider.OfferedServices | WaiverService.SelfDirection
                                    : _provider.OfferedServices & ~WaiverService.SelfDirection;
                if (_provider.OfferedServices != updated)
                {
                    _provider.OfferedServices = updated;
                    OnPropertyChanged();
                }
            }
        }

        public bool OffersCommunityMembership
        {
            get => _provider.OfferedServices.HasFlag(WaiverService.CommunityMembership);
            set
            {
                var updated = value ? _provider.OfferedServices | WaiverService.CommunityMembership
                                    : _provider.OfferedServices & ~WaiverService.CommunityMembership;
                if (_provider.OfferedServices != updated)
                {
                    _provider.OfferedServices = updated;
                    OnPropertyChanged();
                }
            }
        }

        // ---- Passthrough ----
        // The bool the AT dropdown filters on and the reveal trigger for the three
        // fields below. XAML binds their visibility straight to this.
        public bool ProvidesPassthroughService
        {
            get => _provider.ProvidesPassthroughService;
            set { if (_provider.ProvidesPassthroughService != value) { _provider.ProvidesPassthroughService = value; OnPropertyChanged(); } }
        }

        public string? BillingLocationEis
        {
            get => _provider.BillingLocationEis;
            set { if (_provider.BillingLocationEis != value) { _provider.BillingLocationEis = value; OnPropertyChanged(); } }
        }

        public string? ProgramContact
        {
            get => _provider.ProgramContact;
            set { if (_provider.ProgramContact != value) { _provider.ProgramContact = value; OnPropertyChanged(); } }
        }

        public string? BillingContact
        {
            get => _provider.BillingContact;
            set { if (_provider.BillingContact != value) { _provider.BillingContact = value; OnPropertyChanged(); } }
        }
    }
}