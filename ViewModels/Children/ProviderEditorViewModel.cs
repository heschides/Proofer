using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.ViewModels.Children
{
    // Observable editing wrapper around one Provider — the same write-through lens
    // as ATRequestEditorViewModel. INPC stays out of the POCO; each property reads
    // and writes the entity, so Save just persists it.
    //
    // Three non-obvious bits:
    //  - The four waiver-service bools each map to ONE bit of the OfferedServices
    //    [Flags] enum: set with "| flag", clear with "& ~flag".
    //  - ProvidesPassthroughService drives the reveal of the three passthrough
    //    fields; the XAML binds their visibility to it, so it only needs to raise.
    //  - Affiliation is medical-only in the form, and the parent picker offers only
    //    what a save would accept. ProviderAffiliation decides that, not this class:
    //    the picker filter and the server's rejection have to be the same rule or the
    //    form silently offers choices the API refuses.
    public partial class ProviderEditorViewModel : ObservableObject
    {
        // Stands in for the entry being edited while its affiliation is resolved. A new
        // entry has no id yet, and an existing one may have a parent that differs from
        // the stored row, so neither can be walked from the directory as loaded.
        private const int EditedEntryId = -1;

        private readonly Provider _provider;
        private readonly List<Provider> _directory;
        private readonly List<ProviderAffiliationNode> _nodes;

        // The directory is required rather than optional: an editor built without it
        // would offer no parents and quietly accept an entry the API would refuse.
        public ProviderEditorViewModel(Provider provider, IEnumerable<Provider> agencyDirectory)
        {
            ArgumentNullException.ThrowIfNull(agencyDirectory);
            _provider = provider;
            _directory = agencyDirectory.Where(candidate => candidate.Id != 0).ToList();
            _nodes = _directory.ToAffiliationNodes();
            RefreshParentOptions();
        }

        public Provider Provider => _provider;

        // Enum.GetValues<T>() per your style guide; backs the Type combo.
        public static Array ProviderTypeOptions => Enum.GetValues<ProviderType>();

        public static Array MedicalKindOptions => Enum.GetValues<MedicalProviderKind>();

        public ProviderType Type
        {
            get => _provider.Type;
            set
            {
                if (_provider.Type == value)
                    return;

                _provider.Type = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMedical));

                // Leaving healthcare clears both the tier and the link. Neither means
                // anything outside the medical hierarchy, and a stale parent left behind
                // would fail validation on save with nothing on screen explaining why.
                if (value != ProviderType.Healthcare)
                {
                    MedicalKind = null;
                    ParentProviderId = null;
                }

                RefreshParentOptions();
            }
        }

        // The reveal trigger for the affiliation fields. Affiliation is gated to medical
        // in the form only — the column itself is general, so waiver entries can gain a
        // hierarchy later without a schema change.
        public bool IsMedical => _provider.Type == ProviderType.Healthcare;

        public MedicalProviderKind? MedicalKind
        {
            get => _provider.MedicalKind;
            set
            {
                if (_provider.MedicalKind == value)
                    return;

                _provider.MedicalKind = value;
                OnPropertyChanged();
                RefreshParentOptions();
            }
        }

        public int? ParentProviderId
        {
            get => _provider.ParentProviderId;
            set
            {
                if (_provider.ParentProviderId == value)
                    return;

                _provider.ParentProviderId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AffiliationSummary));
                OnPropertyChanged(nameof(HasAffiliation));
            }
        }

        // Only entries a save would accept: the tier rule, the self link, and anything
        // that would close a loop are all excluded here rather than reported afterwards.
        public ObservableCollection<Provider> ParentOptions { get; } = [];

        public bool HasParentOptions => ParentOptions.Count > 0;

        // Shown when a medical entry has no legal parent to offer — an empty picker with
        // no explanation reads as a broken control rather than an empty directory.
        public string ParentEmptyExplanation => MedicalKind switch
        {
            MedicalProviderKind.Individual =>
                "Add a practice or a network to the directory before affiliating an individual.",
            MedicalProviderKind.Practice =>
                "Add a network to the directory before affiliating a practice.",
            MedicalProviderKind.Network =>
                "A network stands alone unless a larger network is in the directory.",
            _ => "Choose a designation to see the organizations this entry can belong to."
        };

        public bool HasAffiliation => _provider.ParentProviderId is not null;

        // The resolved chain above this entry, e.g. "Coastal Women's Healthcare · MaineHealth".
        // Derived on every read rather than stored, which is what makes a directory
        // correction show up everywhere instead of leaving stale copies behind.
        public string AffiliationSummary
        {
            get
            {
                if (_provider.ParentProviderId is null)
                    return string.Empty;

                var nodes = new List<ProviderAffiliationNode>(_nodes)
                {
                    new(EditedEntryId, _provider.Name, _provider.ParentProviderId, _provider.MedicalKind)
                };
                return ProviderAffiliation.DescribeAffiliation(EditedEntryId, nodes);
            }
        }

        private void RefreshParentOptions()
        {
            ParentOptions.Clear();
            foreach (var candidate in _directory
                         .Where(candidate => ProviderAffiliation.IsSelectableParent(
                             _provider.Id, _provider.MedicalKind, candidate.Id, _nodes))
                         .OrderBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                ParentOptions.Add(candidate);
            }

            // A parent that was legal under the previous tier may not be legal now —
            // changing Practice to Network invalidates a practice parent. Clearing it
            // here keeps the bound selection and the entity from disagreeing.
            if (_provider.ParentProviderId is { } parentId &&
                ParentOptions.All(option => option.Id != parentId))
            {
                ParentProviderId = null;
            }

            OnPropertyChanged(nameof(HasParentOptions));
            OnPropertyChanged(nameof(ParentEmptyExplanation));
            OnPropertyChanged(nameof(AffiliationSummary));
            OnPropertyChanged(nameof(HasAffiliation));
        }

        public string Name
        {
            get => _provider.Name;
            set
            {
                if (_provider.Name == value)
                    return;
                _provider.Name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SameNameWarning));
                OnPropertyChanged(nameof(HasSameNameWarning));
            }
        }

        public string SameNameWarning => ProviderDirectoryRules.SameNameWarning(
            _provider.Name,
            _provider.Id,
            _nodes);

        public bool HasSameNameWarning => SameNameWarning.Length > 0;

        // Durable organization identifiers. Optional, because a directory entry is
        // often created from a phone call before any paperwork exists — but they are
        // the only thing that will let this entry be recognized as the same
        // organization if it later joins the platform in its own right, so the form
        // asks for them rather than leaving it to memory.
        public string? Npi
        {
            get => _provider.Npi;
            set { if (_provider.Npi != value) { _provider.Npi = value; OnPropertyChanged(); } }
        }

        public string? MaineCareProviderId
        {
            get => _provider.MaineCareProviderId;
            set { if (_provider.MaineCareProviderId != value) { _provider.MaineCareProviderId = value; OnPropertyChanged(); } }
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
