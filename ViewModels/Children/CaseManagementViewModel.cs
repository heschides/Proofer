using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sati.ViewModels.Children
{
    // Sub-tab HOST for the Case Management section. Mirrors ShellViewModel's
    // navigation idiom one level down, the same way HelpersViewModel did: each
    // sub-destination is a field, CurrentSubViewModel points at the active one, and
    // the IsXActive flags drive the sub-nav highlight.
    //
    // Guidance, Reference, and AT Requests were previously top-level tabs beside
    // Case Management. They are case-manager reference tools rather than peers of
    // the whole section, so they now sit inside it. The former Helpers wrapper was
    // dissolved in the same change: nesting it here would have produced three
    // navigation levels and two stacked sub-nav bars, so its two children were
    // promoted to sit directly alongside Guidance.
    public partial class CaseManagementViewModel : ObservableObject
    {
        private readonly CaseManagerDashboardViewModel _dashboard;
        private readonly GuidanceViewModel _guidance;
        private readonly HelperReferenceViewModel _reference;
        private readonly ATRequestViewModel _atRequests;

        public CaseManagementViewModel(
            CaseManagerDashboardViewModel dashboard,
            GuidanceViewModel guidance,
            HelperReferenceViewModel reference,
            ATRequestViewModel atRequests)
        {
            _dashboard = dashboard;
            _guidance = guidance;
            _reference = reference;
            _atRequests = atRequests;
            CurrentSubViewModel = _dashboard;   // The dashboard is the default sub-tab
        }

        // The shell still needs the dashboard itself to flush an in-progress journal
        // edit while the window is closing.
        public CaseManagerDashboardViewModel Dashboard => _dashboard;

        [ObservableProperty] private object? currentSubViewModel;

        public bool IsDashboardActive => CurrentSubViewModel is CaseManagerDashboardViewModel;
        public bool IsGuidanceActive => CurrentSubViewModel is GuidanceViewModel;
        public bool IsReferenceActive => CurrentSubViewModel is HelperReferenceViewModel;
        public bool IsATRequestsActive => CurrentSubViewModel is ATRequestViewModel;

        partial void OnCurrentSubViewModelChanged(object? value)
        {
            OnPropertyChanged(nameof(IsDashboardActive));
            OnPropertyChanged(nameof(IsGuidanceActive));
            OnPropertyChanged(nameof(IsReferenceActive));
            OnPropertyChanged(nameof(IsATRequestsActive));
        }

        // Switching users must not leave the outgoing user's sub-tab — and the data
        // it already loaded — on screen. The shell calls this during reinitialise so
        // the incoming session starts on the dashboard, which reloads as part of that
        // same flow. Previously the shell navigated straight to the dashboard view
        // model, so this reset was implicit; hosting it made it explicit work.
        public void ResetToDashboard() => CurrentSubViewModel = _dashboard;

        [RelayCommand] private void NavigateToDashboard() => CurrentSubViewModel = _dashboard;
        [RelayCommand] private void NavigateToGuidance() => CurrentSubViewModel = _guidance;
        [RelayCommand] private void NavigateToReference() => CurrentSubViewModel = _reference;

        // Async: activating the AT tab loads its queue + client list. Reloads on
        // every visit so the queue reflects the latest saves. Carried over verbatim
        // from HelpersViewModel when that wrapper was dissolved.
        [RelayCommand]
        private async Task NavigateToATRequests()
        {
            CurrentSubViewModel = _atRequests;
            await _atRequests.InitializeAsync();
        }
    }
}
