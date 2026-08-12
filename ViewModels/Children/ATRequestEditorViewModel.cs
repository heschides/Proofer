using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        public ATRequestEditorViewModel(
            ATRequest request,
            decimal passthroughRate,
            IEnumerable<Provider> providers,
            Provider? defaultProvider)
        {
            _request = request;
            _rate = passthroughRate;

            foreach (var provider in providers)
                AvailableProviders.Add(provider);

            // Wrap existing rows (present when 1d opens a saved request; empty for a
            // new one). Each row gets RaiseTotalsChanged as its change callback.
            foreach (var item in _request.Items)
                Items.Add(new ATRequestItemEditorViewModel(item, RaiseTotalsChanged));

            SelectedProvider = defaultProvider;
        }

        public ATRequest Request => _request;

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
        public string? VendorName
        {
            get => _request.VendorName;
            set { if (_request.VendorName != value) { _request.VendorName = value; OnPropertyChanged(); } }
        }

        public string? VendorBillingLocation
        {
            get => _request.VendorBillingLocation;
            set { if (_request.VendorBillingLocation != value) { _request.VendorBillingLocation = value; OnPropertyChanged(); } }
        }

        public string? VendorProgramContact
        {
            get => _request.VendorProgramContact;
            set { if (_request.VendorProgramContact != value) { _request.VendorProgramContact = value; OnPropertyChanged(); } }
        }

        public string? VendorBillingContact
        {
            get => _request.VendorBillingContact;
            set { if (_request.VendorBillingContact != value) { _request.VendorBillingContact = value; OnPropertyChanged(); } }
        }

        // ---- Editable: sales tax ----
        public decimal SalesTax
        {
            get => _request.SalesTax;
            set
            {
                if (_request.SalesTax != value)
                {
                    _request.SalesTax = value;
                    OnPropertyChanged();
                    RaiseTotalsChanged();
                }
            }
        }

        // ---- Item grid ----
        public ObservableCollection<ATRequestItemEditorViewModel> Items { get; } = [];

        // Append a blank item to both the entity and the observable grid, then
        // refresh totals. The new row carries RaiseTotalsChanged so its later
        // cost/qty edits propagate.
        [RelayCommand]
        private void AddItem()
        {
            var item = new ATRequestItem();
            _request.Items.Add(item);
            Items.Add(new ATRequestItemEditorViewModel(item, RaiseTotalsChanged));
            RaiseTotalsChanged();
        }

        // Remove a row from both the entity and the grid, then refresh totals.
        [RelayCommand]
        private void RemoveItem(ATRequestItemEditorViewModel row)
        {
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
            OnPropertyChanged(nameof(ItemsTotal));
            OnPropertyChanged(nameof(PassthroughFee));
            OnPropertyChanged(nameof(TotalCost));
        }
    }
}
