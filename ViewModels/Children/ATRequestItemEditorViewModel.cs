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
    // whenever a field the REQUEST depends on changes — cost and quantity, which
    // feed the totals, and Name, which decides whether the request is complete
    // enough to publish (AtRequestPublication.FindBlockers). Url alone changes
    // nothing above this row, so it does not call back.
    public partial class ATRequestItemEditorViewModel : ObservableObject
    {
        private readonly ATRequestItem _item;
        private readonly Action? _onChanged;

        public ATRequestItemEditorViewModel(ATRequestItem item, Action? onChanged = null)
        {
            _item = item;
            _onChanged = onChanged;
        }

        // Exposed so the parent can add/remove it from ATRequest.Items in lockstep
        // with this VM.
        public ATRequestItem Item => _item;

        public string? Name
        {
            get => _item.Name;
            set { if (_item.Name != value) { _item.Name = value; OnPropertyChanged(); _onChanged?.Invoke(); } }
        }

        public string? Url
        {
            get => _item.Url;
            set { if (_item.Url != value) { _item.Url = value; OnPropertyChanged(); } }
        }

        // ---- Pasted evidence clip ----
        // Held as raw PNG bytes, never as a BitmapImage: this view model is
        // referenced by tests and by the API-backed path, and neither should have
        // to drag in WPF imaging. The view converts for display
        // (PngBytesToImageConverter).
        //
        // Validation happens at the paste boundary in the view, so by the time
        // bytes arrive here they have already been downscaled and checked against
        // AtRequestScreenshot. The setter still notifies the parent, because the
        // request-level clip count depends on it.
        public byte[]? ScreenshotPng
        {
            get => _item.ScreenshotPng;
            set
            {
                if (_item.ScreenshotPng == value)
                    return;

                _item.ScreenshotPng = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasScreenshot));
                _onChanged?.Invoke();
            }
        }

        public bool HasScreenshot => _item.HasScreenshot;

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
                    _onChanged?.Invoke();
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
                    _onChanged?.Invoke();
                }
            }
        }

        public decimal LineTotal => _item.LineTotal;
    }
}