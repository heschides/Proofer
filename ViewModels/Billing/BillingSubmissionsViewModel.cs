using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Edi;
using Sati.Models.Billing;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Sati.ViewModels.Billing
{
    public partial class BillingSubmissionsViewModel : ObservableObject
    {
        private readonly IBillingService _billingService;
        private readonly IEdiService _ediService;
        private readonly ISessionService _sessionService;
        private string? _pendingEdiKey;
        private int? _pendingPeriodId;
        private bool? _pendingIsTest;

        public BillingSubmissionsViewModel(
            IBillingService billingService,
            IEdiService ediService,
            ISessionService sessionService)
        {
            _billingService = billingService;
            _ediService = ediService;
            _sessionService = sessionService;
        }

        public ObservableCollection<BillingPeriod> BillingPeriods { get; } = [];

        [ObservableProperty] private BillingPeriod? selectedPeriod;
        [ObservableProperty] private string? lastGeneratedPath;
        [ObservableProperty] private string? statusMessage;
        [ObservableProperty] private bool isGenerating;
        [ObservableProperty] private bool isTestMode = true;
        public bool HasLoaded { get; private set; }

        public bool HasSelectedPeriod => SelectedPeriod is not null;
        public bool HasGeneratedFile => !string.IsNullOrWhiteSpace(LastGeneratedPath);

        partial void OnSelectedPeriodChanged(BillingPeriod? value)
            => OnPropertyChanged(nameof(HasSelectedPeriod));

        partial void OnLastGeneratedPathChanged(string? value)
            => OnPropertyChanged(nameof(HasGeneratedFile));

        public async Task LoadAsync()
        {
            try
            {
                BillingPeriods.Clear();
                var user = _sessionService.CurrentUser!;
                var periods = user.Role is UserRole.Admin or UserRole.Supervisor
                    ? await _billingService.GetAllBillingPeriodsAsync()
                    : await _billingService.GetBillingPeriodsAsync(user.Id);

                foreach (var period in periods)
                    BillingPeriods.Add(period);

                HasLoaded = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BillingSubmissionsViewModel.LoadAsync failed: {ex.Message}");
                StatusMessage = "Failed to load billing periods.";
            }
        }

        [RelayCommand]
        private async Task GenerateEdi()
        {
            if (SelectedPeriod is null)
                return;

            if (_pendingEdiKey is null || _pendingPeriodId != SelectedPeriod.Id || _pendingIsTest != IsTestMode)
            {
                _pendingEdiKey = Guid.NewGuid().ToString("N");
                _pendingPeriodId = SelectedPeriod.Id;
                _pendingIsTest = IsTestMode;
            }

            try
            {
                IsGenerating = true;
                StatusMessage = "Generating 837P file...";

                var path = await _ediService.GenerateAndSaveAsync(
                    SelectedPeriod.Id,
                    IsTestMode,
                    _pendingEdiKey);

                LastGeneratedPath = path;
                StatusMessage = $"File saved: {path}";
                _pendingEdiKey = null;
                _pendingPeriodId = null;
                _pendingIsTest = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GenerateEdi failed: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsGenerating = false;
            }
        }

        [RelayCommand]
        private void OpenOutputFolder()
        {
            try
            {
                var folder = System.IO.Path.GetDirectoryName(LastGeneratedPath);
                if (folder is not null && System.IO.Directory.Exists(folder))
                    Process.Start("explorer.exe", folder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OpenOutputFolder failed: {ex.Message}");
            }
        }
    }
}