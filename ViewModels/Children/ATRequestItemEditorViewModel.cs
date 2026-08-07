using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;

namespace Sati.ViewModels.Children
{
    // Observable editing wrapper for one ATRequestItem grid row. Write-through:
    // each bindable property reads/writes the entity directly, so the entity is
    // always current and Save just persists it (same lens pattern as the parent
    // ATRequestEditorViewModel).
    //
    // PARENT NOTIFICATION: the parent injects a callback that this row invokes
    // whenever LineTotal changes, so request-level totals (passthrough/total)
    // recompute. Name/Url don't touch the money math, so they don't call it.
    public partial class ATRequestItemEditorViewModel : ObservableObject
    {
        private readonly ATRequestItem _item;
        private readonly Action? _onLineTotalChanged;

        public ATRequestItemEditorViewModel(ATRequestItem item, Action? onLineTotalChanged = null)
        {
            _item = item;
            _onLineTotalChanged = onLineTotalChanged;
        }

        // Exposed so the parent can add/remove it from ATRequest.Items in lockstep
        // with this VM.
        public ATRequestItem Item => _item;

        public string? Name
        {
            get => _item.Name;
            set { if (_item.Name != value) { _item.Name = value; OnPropertyChanged(); } }
        }

        public string? Url
        {
            get => _item.Url;
            set { if (_item.Url != value) { _item.Url = value; OnPropertyChanged(); } }
        }

        public decimal ItemCost
        {
            get => _item.ItemCost;
            set
            {
                if (_item.ItemCost != value)
                {
                    _item.ItemCost = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LineTotal));
                    _onLineTotalChanged?.Invoke();
                }
            }
        }

        public int Quantity
        {
            get => _item.Quantity;
            set
            {
                if (_item.Quantity != value)
                {
                    _item.Quantity = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LineTotal));
                    _onLineTotalChanged?.Invoke();
                }
            }
        }

        public decimal LineTotal => _item.LineTotal;
    }
}