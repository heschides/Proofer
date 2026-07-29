using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sati.ViewModels.Children
{
    // Sub-tab HOST for the Helpers section. Mirrors ShellViewModel's navigation
    // idiom one level down: each sub-destination is a field, CurrentSubViewModel
    // points at the active one, IsXActive flags drive the sub-nav highlight.
    public partial class HelpersViewModel : ObservableObject
    {
        private readonly HelperReferenceViewModel _reference;
        private readonly ATRequestViewModel _atRequests;

        public HelpersViewModel(HelperReferenceViewModel reference, ATRequestViewModel atRequests)
        {
            _reference = reference;
            _atRequests = atRequests;
            CurrentSubViewModel = _reference;   // Reference is the default sub-tab
        }

        [ObservableProperty] private object? currentSubViewModel;

        public bool IsReferenceActive => CurrentSubViewModel is HelperReferenceViewModel;
        public bool IsATRequestsActive => CurrentSubViewModel is ATRequestViewModel;

        partial void OnCurrentSubViewModelChanged(object? value)
        {
            OnPropertyChanged(nameof(IsReferenceActive));
            OnPropertyChanged(nameof(IsATRequestsActive));
        }

        [RelayCommand] private void NavigateToReference() => CurrentSubViewModel = _reference;

        // Async: activating the AT tab loads its queue + client list. Reloads on
        // every visit so the queue reflects the latest saves. RelayCommand emits
        // this as an AsyncRelayCommand; the XAML binding is unchanged.
        [RelayCommand]
        private async Task NavigateToATRequests()
        {
            CurrentSubViewModel = _atRequests;
            await _atRequests.InitializeAsync();
        }
    }
}