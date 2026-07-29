using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Children
{
    // Observable editing wrapper around one in-memory ATRequest — the "detail"
    // half of the AT master-detail. Exists because ATRequest is a pure persistence
    // entity with no change notification; the live document preview needs the
    // right pane to react as the left pane is typed into, which requires an
    // observable surface. Keeps INotifyPropertyChanged OUT of the domain entity.
    //
    // Write-through: each bindable property reads and writes the underlying entity
    // directly, so the entity is always current and Save just persists it — no
    // copy-back step. The entity is the single store; this VM is the notifying lens.
    //
    // BUILD STATE: slice 1b — vendor block + sales tax. The item grid
    // (ObservableCollection<ATRequestItemEditorViewModel>) and live totals land in
    // 1c; the collection and total properties are seamed in here but not yet
    // populated/computed.
    public partial class ATRequestEditorViewModel : ObservableObject
    {
        private readonly ATRequest _request;

        public ATRequestEditorViewModel(ATRequest request)
        {
            _request = request;
        }

        // The wrapped entity, exposed for Save and for the preview's read-only
        // snapshot fields (client/CM info, frozen at CreateForClient).
        public ATRequest Request => _request;

        // ---- Read-only snapshot fields (for the preview pane) ----
        // Frozen at creation; displayed on the form, never edited here.
        public string? ClientName => _request.ClientName;
        public string? ClientEvergreenId => _request.ClientEvergreenId;
        public string? CaseManagerName => _request.CaseManagerName;
        public string? CaseManagerEmail => _request.CaseManagerEmail;
        public string? CaseManagerPhone => _request.CaseManagerPhone;
        public string? CaseManagerAgency => _request.CaseManagerAgency;

        // ---- Editable: vendor block ----
        // Write-through properties. Each setter writes the entity and raises
        // PropertyChanged so the preview pane updates live. Not [ObservableProperty]
        // because these back onto the entity's fields, not VM-local backing fields.

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
        // Changing tax must also refresh the totals (passthrough is computed on
        // the tax-inclusive subtotal). Totals are stubbed in 1b, computed in 1c;
        // the OnPropertyChanged for them is already fired here so the wiring is
        // complete when 1c fills the getters in.
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

        // ---- Item grid (populated in 1c) ----
        public ObservableCollection<ATRequestItemEditorViewModel> Items { get; } = [];

        // ---- Live totals (computed in 1c) ----
        // Stubbed to zero for 1b so the file builds and the preview can bind now;
        // 1c replaces the bodies with real calculator-backed math and wires
        // per-item change notifications to RaiseTotalsChanged.
        public decimal ItemsTotal => 0m;
        public decimal PassthroughFee => 0m;
        public decimal TotalCost => 0m;

        // Re-raises the three computed totals. Called by SalesTax and (in 1c) by
        // item add/remove/edit. Central so every total-affecting change routes
        // through one notifier.
        private void RaiseTotalsChanged()
        {
            OnPropertyChanged(nameof(ItemsTotal));
            OnPropertyChanged(nameof(PassthroughFee));
            OnPropertyChanged(nameof(TotalCost));
        }
    }
}