using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Helpers;
using Sati.Models;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Children
{
    // Observable editing wrapper around one in-memory ATRequest — the "detail"
    // half of the AT master-detail. Keeps INotifyPropertyChanged out of the domain
    // entity; write-through properties mean the entity is always current.
    //
    // BUILD STATE: slice 1c — item grid + live totals. Item rows are wrapped in
    // ATRequestItemEditorViewModel; cost/qty edits route through RaiseTotalsChanged
    // so passthrough/total recompute live. Persistence (Save) is still 1d: nothing
    // here reaches the DB, so closing the editor discards an unsaved request.
    public partial class ATRequestEditorViewModel : ObservableObject
    {
        private readonly ATRequest _request;

        // Passthrough rate from Settings, injected because the entity can't see
        // Settings and its TotalCost/PassthroughFee methods take the rate as an arg.
        private readonly decimal _rate;

        // Sales tax rate from Settings, injected for the same reason as the
        // passthrough rate: the entity cannot see Settings.
        private readonly decimal _salesTaxRate;

        public ATRequestEditorViewModel(
            ATRequest request,
            decimal passthroughRate,
            decimal salesTaxRate,
            IEnumerable<Provider> providers,
            Provider? defaultProvider)
        {
            _request = request;
            _rate = passthroughRate;
            _salesTaxRate = salesTaxRate;

            foreach (var provider in providers)
                AvailableProviders.Add(provider);

            // Wrap existing rows (present when 1d opens a saved request; empty for a
            // new one). Each row gets RaiseTotalsChanged as its change callback.
            foreach (var item in _request.Items)
                Items.Add(new ATRequestItemEditorViewModel(item, RaiseTotalsChanged));

            SelectedProvider = defaultProvider;

            // A brand-new request starts with no items and no tax; seeding it here
            // means the calculated figure is correct from the first item onward
            // without the user touching the field. A saved request keeps whatever
            // was filed — recalculating on open would silently rewrite a document
            // of record if the agency's rate had changed since.
            if (_request.Id == 0 && !_request.SalesTaxOverridden)
                RecalculateSalesTax();
        }

        public ATRequest Request => _request;

        // ---- Publication state ----
        // These drive enablement and the attestation banner. They are NOT the lock:
        // the lock is ATRequestService.UpdateAsync refusing a save against a stored
        // published row. A disabled text box is a courtesy to the user, not a
        // control — see the engineering rule about UI visibility and security.
        public bool IsPublished => _request.IsPublished;
        public bool CanEdit => !IsPublished;

        public string? SignedByName => _request.SignedByName;
        public string? SignedByRole => _request.SignedByRole;
        public DateTime? SignedAtUtc => _request.SignedAtUtc;

        public string PublishedSummary => IsPublished
            ? $"Published by {_request.SignedByName} ({_request.SignedByRole}) on " +
              $"{_request.SignedAtUtc:yyyy-MM-dd HH:mm} UTC. Reopen for correction to make changes."
            : string.Empty;

        // Why the Publish button is unavailable, in the user's words. Recomputed on
        // demand rather than cached: the answer changes with every item edit, and
        // the check is a handful of string tests over an in-memory list.
        public IReadOnlyList<string> PublishBlockers => AtRequestPublication.FindBlockers(
            _request.VendorName,
            _request.VendorBillingLocation,
            _request.Items.Select(item => new AtRequestLine(item.Name, item.ItemCost, item.Quantity)),
            _request.IsPublished);

        public bool CanPublish => PublishBlockers.Count == 0;

        // ---- Pasted evidence clips ----
        // The count is surfaced so the case manager can confirm at a glance that
        // every clip they pasted actually landed, rather than discovering a
        // missing one in the generated PDF. Repeated in the publish confirmation
        // for the same reason.
        public int ScreenshotCount => _request.Items.Count(item => item.HasScreenshot);

        public string ScreenshotSummary
        {
            get
            {
                var total = _request.Items.Count;
                if (total == 0)
                    return "No items yet.";

                var pasted = ScreenshotCount;
                if (pasted == 0)
                    return "No screenshots pasted. Items without one are listed on the request but have no evidence page.";

                var clip = pasted == 1 ? "screenshot" : "screenshots";
                return pasted == total
                    ? $"{pasted} {clip} pasted — one for every item."
                    : $"{pasted} of {total} items have a {clip.TrimEnd('s')}.";
            }
        }

        // Empty once published as well as once publishable: a published request's
        // only "blocker" is that it is already published, which the attestation
        // banner says far better than a list of prerequisites would.
        public bool HasPublishBlockers => !CanPublish && !IsPublished;

        public string PublishBlockerSummary => HasPublishBlockers
            ? "Before publishing: " + string.Join(" ", PublishBlockers)
            : string.Empty;

        // Raised after Publish so the whole dependent surface refreshes at once.
        // Called by the owning page VM, which is what actually performs the save.
        public void RaisePublicationChanged()
        {
            OnPropertyChanged(nameof(IsPublished));
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(SignedByName));
            OnPropertyChanged(nameof(SignedByRole));
            OnPropertyChanged(nameof(SignedAtUtc));
            OnPropertyChanged(nameof(PublishedSummary));
            RaisePublishabilityChanged();
        }

        private void RaisePublishabilityChanged()
        {
            OnPropertyChanged(nameof(PublishBlockers));
            OnPropertyChanged(nameof(CanPublish));
            OnPropertyChanged(nameof(HasPublishBlockers));
            OnPropertyChanged(nameof(PublishBlockerSummary));
            OnPropertyChanged(nameof(ScreenshotCount));
            OnPropertyChanged(nameof(ScreenshotSummary));
        }

        // Provider is live directory data; Vendor* fields below are the immutable
        // request snapshot once saved. Selecting a provider copies its current AT
        // billing contacts into that snapshot, while leaving the copied fields
        // editable for one-off request corrections.
        public ObservableCollection<Provider> AvailableProviders { get; } = [];

        [ObservableProperty]
        private Provider? selectedProvider;

        partial void OnSelectedProviderChanged(Provider? value)
        {
            if (value is null)
                return;

            VendorName = value.Name;
            VendorBillingLocation = value.BillingLocationEis;
            VendorProgramContact = value.ProgramContact;
            VendorBillingContact = value.BillingContact;
        }

        // ---- Read-only snapshot fields (for the preview pane) ----
        public string? ClientName => _request.ClientName;
        public string? ClientEvergreenId => _request.ClientEvergreenId;
        public string? CaseManagerName => _request.CaseManagerName;
        public string? CaseManagerEmail => _request.CaseManagerEmail;
        public string? CaseManagerPhone => _request.CaseManagerPhone;
        public string? CaseManagerAgency => _request.CaseManagerAgency;

        // ---- Editable: vendor block ----
        // Each setter drops the write once the request is published. That is a
        // guard against a binding that outlived its IsEnabled, not the lock — the
        // lock is server-side. Silently ignoring beats throwing here because a
        // throw from a binding setter is swallowed into a trace nobody reads.
        public string? VendorName
        {
            get => _request.VendorName;
            set
            {
                if (IsPublished || _request.VendorName == value) return;
                _request.VendorName = value;
                OnPropertyChanged();
                RaisePublishabilityChanged();
            }
        }

        public string? VendorBillingLocation
        {
            get => _request.VendorBillingLocation;
            set
            {
                if (IsPublished || _request.VendorBillingLocation == value) return;
                _request.VendorBillingLocation = value;
                OnPropertyChanged();
                RaisePublishabilityChanged();
            }
        }

        public string? VendorProgramContact
        {
            get => _request.VendorProgramContact;
            set
            {
                if (IsPublished || _request.VendorProgramContact == value) return;
                _request.VendorProgramContact = value;
                OnPropertyChanged();
            }
        }

        public string? VendorBillingContact
        {
            get => _request.VendorBillingContact;
            set
            {
                if (IsPublished || _request.VendorBillingContact == value) return;
                _request.VendorBillingContact = value;
                OnPropertyChanged();
            }
        }

        // ---- Sales tax: calculated by default, overridable per request ----

        // Editable only while SalesTaxOverridden is true; the XAML binds the field's
        // enabled state to that. The setter still works either way so the
        // recalculation below can write through it and raise the same notifications.
        public decimal SalesTax
        {
            get => _request.SalesTax;
            set
            {
                if (IsPublished) return;
                if (_request.SalesTax != value)
                {
                    _request.SalesTax = value;
                    OnPropertyChanged();
                    RaiseTotalsChanged();
                }
            }
        }

        public bool SalesTaxOverridden
        {
            get => _request.SalesTaxOverridden;
            set
            {
                if (IsPublished || _request.SalesTaxOverridden == value)
                    return;

                _request.SalesTaxOverridden = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SalesTaxRateLabel));

                // Turning the override OFF restores the calculated figure. Turning it
                // ON deliberately leaves the current amount in place, so the user
                // edits from the calculated value rather than from an empty box.
                if (!value)
                    RecalculateSalesTax();
            }
        }

        // Shown next to the field so the applied rate is visible rather than folded
        // invisibly into an amount.
        public string SalesTaxRateLabel => SalesTaxOverridden
            ? "Entered manually — the configured rate is not applied."
            : $"Calculated at {_salesTaxRate:P2} of the items subtotal, from agency settings.";

        private void RecalculateSalesTax()
        {
            var calculated = ATRequestCalculator.SalesTax(_request.ItemsTotal, _salesTaxRate);
            if (_request.SalesTax == calculated)
                return;

            _request.SalesTax = calculated;
            OnPropertyChanged(nameof(SalesTax));
        }

        // ---- Item grid ----
        public ObservableCollection<ATRequestItemEditorViewModel> Items { get; } = [];

        // Append a blank item to both the entity and the observable grid, then
        // refresh totals. The new row carries RaiseTotalsChanged so its later
        // cost/qty edits propagate.
        [RelayCommand]
        private void AddItem()
        {
            if (IsPublished) return;
            var item = new ATRequestItem();
            _request.Items.Add(item);
            Items.Add(new ATRequestItemEditorViewModel(item, RaiseTotalsChanged));
            RaiseTotalsChanged();
        }

        // Remove a row from both the entity and the grid, then refresh totals.
        [RelayCommand]
        private void RemoveItem(ATRequestItemEditorViewModel row)
        {
            if (IsPublished) return;
            _request.Items.Remove(row.Item);
            Items.Remove(row);
            RaiseTotalsChanged();
        }

        // ---- Live totals ----
        // Read-only lenses over the entity's calculator-backed math. Passthrough is
        // applied post-tax (see ATRequestCalculator); both pass the injected rate.
        public decimal ItemsTotal => _request.ItemsTotal;
        public decimal PassthroughFee => _request.PassthroughFee(_rate);
        public decimal TotalCost => _request.TotalCost(_rate);

        private void RaiseTotalsChanged()
        {
            // Sales tax first: it is an input to the passthrough fee and the total,
            // so recalculating after raising them would publish stale figures. This
            // writes the entity directly rather than through the SalesTax setter,
            // which would re-enter here.
            if (!_request.SalesTaxOverridden)
                RecalculateSalesTax();

            OnPropertyChanged(nameof(ItemsTotal));
            OnPropertyChanged(nameof(PassthroughFee));
            OnPropertyChanged(nameof(TotalCost));

            // Item names, costs, and quantities are all publication blockers, and
            // every one of them routes through here.
            RaisePublishabilityChanged();
        }
    }
}
