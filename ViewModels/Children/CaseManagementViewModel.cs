using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sati.ViewModels.Children
{
    // Sub-tab HOST for the Case Management section. Mirrors ShellViewModel's
    // navigation idiom one level down, the same way HelpersViewModel did: each
    // sub-destination is a field, CurrentSubViewModel points at the active one, and
    // the IsXActive flags drive the sub-nav highlight.
    //
    // Guidance and Reference are section-level tools. Operational work such as AT
    // requests lives one level deeper on the dashboard beside Clients and Notes.
    public partial class CaseManagementViewModel : ObservableObject
    {
        private readonly CaseManagerDashboardViewModel _dashboard;
        private readonly GuidanceViewModel _guidance;
        private readonly HelperReferenceViewModel _reference;

        public CaseManagementViewModel(
            CaseManagerDashboardViewModel dashboard,
            GuidanceViewModel guidance,
            HelperReferenceViewModel reference)
        {
            _dashboard = dashboard;
            _guidance = guidance;
            _reference = reference;
            CurrentSubViewModel = _dashboard;   // The dashboard is the default sub-tab
        }

        // The shell still needs the dashboard itself to flush an in-progress journal
        // edit while the window is closing.
        public CaseManagerDashboardViewModel Dashboard => _dashboard;

        [ObservableProperty] private object? currentSubViewModel;

        public bool IsDashboardActive => CurrentSubViewModel is CaseManagerDashboardViewModel;
        public bool IsGuidanceActive => CurrentSubViewModel is GuidanceViewModel;
        public bool IsReferenceActive => CurrentSubViewModel is HelperReferenceViewModel;

        partial void OnCurrentSubViewModelChanged(object? value)
        {
            OnPropertyChanged(nameof(IsDashboardActive));
            OnPropertyChanged(nameof(IsGuidanceActive));
            OnPropertyChanged(nameof(IsReferenceActive));
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

    }
}
