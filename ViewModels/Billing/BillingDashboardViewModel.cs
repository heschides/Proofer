using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sati.ViewModels.Billing
{
    public partial class BillingDashboardViewModel : ObservableObject
    {
        private readonly BillingOverviewViewModel _overviewViewModel;
        private readonly BillingQueueViewModel _queueViewModel;
        private readonly BillingSubmissionsViewModel _submissionsViewModel;
        private readonly BillingRemittancesViewModel _remittancesViewModel;
        private readonly BillingAlertsViewModel _alertsViewModel;

        public BillingDashboardViewModel(
            BillingOverviewViewModel overviewViewModel,
            BillingQueueViewModel queueViewModel,
            BillingSubmissionsViewModel submissionsViewModel,
            BillingRemittancesViewModel remittancesViewModel,
            BillingAlertsViewModel alertsViewModel)
        {
            _overviewViewModel = overviewViewModel;
            _queueViewModel = queueViewModel;
            _submissionsViewModel = submissionsViewModel;
            _remittancesViewModel = remittancesViewModel;
            _alertsViewModel = alertsViewModel;

            CurrentSubView = _overviewViewModel;
            _ = _overviewViewModel.LoadAsync();
        }

        [ObservableProperty] private object? currentSubView;

        public bool IsOverviewActive => CurrentSubView is BillingOverviewViewModel;
        public bool IsQueueActive => CurrentSubView is BillingQueueViewModel;
        public bool IsSubmissionsActive => CurrentSubView is BillingSubmissionsViewModel;
        public bool IsRemittancesActive => CurrentSubView is BillingRemittancesViewModel;
        public bool IsAlertsActive => CurrentSubView is BillingAlertsViewModel;

        partial void OnCurrentSubViewChanged(object? value)
        {
            OnPropertyChanged(nameof(IsOverviewActive));
            OnPropertyChanged(nameof(IsQueueActive));
            OnPropertyChanged(nameof(IsSubmissionsActive));
            OnPropertyChanged(nameof(IsRemittancesActive));
            OnPropertyChanged(nameof(IsAlertsActive));
        }

        [RelayCommand]
        private async Task NavigateToOverview()
        {
            CurrentSubView = _overviewViewModel;
            if (!_overviewViewModel.HasLoaded)
                await _overviewViewModel.LoadAsync();
        }

        [RelayCommand]
        private async Task NavigateToQueue()
        {
            CurrentSubView = _queueViewModel;
            if (!_queueViewModel.HasLoaded)
                await _queueViewModel.LoadAsync();
        }

        [RelayCommand]
        private async Task NavigateToSubmissions()
        {
            CurrentSubView = _submissionsViewModel;
            if (!_submissionsViewModel.HasLoaded)
                await _submissionsViewModel.LoadAsync();
        }

        [RelayCommand]
        private async Task NavigateToRemittances()
        {
            CurrentSubView = _remittancesViewModel;
            if (!_remittancesViewModel.HasLoaded)
                await _remittancesViewModel.LoadAsync();
        }

        [RelayCommand]
        private async Task NavigateToAlerts()
        {
            CurrentSubView = _alertsViewModel;
            if (!_alertsViewModel.HasLoaded)
                await _alertsViewModel.LoadAsync();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
