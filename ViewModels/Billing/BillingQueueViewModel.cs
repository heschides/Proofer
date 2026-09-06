using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Sati.ViewModels.Billing
{
    public partial class BillingQueueViewModel : ObservableObject
    {
        private readonly IBillingService _billingService;
        private readonly ISessionService _sessionService;
        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private readonly LatestRequestTracker _accountLoads = new();

        public ObservableCollection<BillingQueueItemViewModel> QueueItems { get; } = [];

        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private string statusMessage = string.Empty;
        public bool HasLoaded { get; private set; }

        public int ValidCount => QueueItems.Count(r => r.IsValid);
        public int InvalidCount => QueueItems.Count(r => !r.IsValid);
        public int SelectedValidCount => QueueItems.Count(r => r.IsSelected && r.IsValid);

        public BillingQueueViewModel(IBillingService billingService, ISessionService sessionService)
        {
            _billingService = billingService;
            _sessionService = sessionService;
        }

        public async Task LoadAsync(bool waitForExisting = false)
        {
            if (waitForExisting)
                await _loadGate.WaitAsync();
            else if (!await _loadGate.WaitAsync(0))
                return;

            IsBusy = true;
            StatusMessage = string.Empty;
            var account = _sessionService.CurrentUser;
            var request = _accountLoads.Begin();
            try
            {
                Debug.WriteLine($"[BillingQueue] LoadAsync started — {DateTime.Now:HH:mm:ss.fff}");
                var actor = account?.ToAgencyActor()
                    ?? throw new UnauthorizedAccessException("A signed-in user is required.");
                var configuration = await _billingService.GetBillingConfigurationAsync(actor);
                var notes = await _billingService.GetApprovedUnbilledNotesAsync(actor);
                if (!_accountLoads.IsCurrent(request) || !ReferenceEquals(_sessionService.CurrentUser, account))
                    return;
                Debug.WriteLine($"[BillingQueue] GetApprovedUnbilledNotesAsync returned {notes.Count()} notes — {DateTime.Now:HH:mm:ss.fff}");
                QueueItems.Clear();
                var items = notes.Select(note => new BillingQueueItemViewModel(
                    _billingService.ValidateNoteForBilling(note), configuration));
                foreach (var item in items
                    .OrderByDescending(candidate => candidate.IsValid)
                    .ThenBy(candidate => candidate.Result.Note.EventDate))
                    QueueItems.Add(item);
                RefreshCounts();
                HasLoaded = true;
                Debug.WriteLine($"[BillingQueue] LoadAsync complete — {DateTime.Now:HH:mm:ss.fff}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BillingQueueViewModel.LoadAsync failed: {ex.Message}");
                if (_accountLoads.IsCurrent(request) && ReferenceEquals(_sessionService.CurrentUser, account))
                    StatusMessage = "The billing queue could not be loaded. Please try Refresh.";
            }
            finally
            {
                if (_accountLoads.IsCurrent(request) && ReferenceEquals(_sessionService.CurrentUser, account))
                    IsBusy = false;
                _loadGate.Release();
            }

        }

        [RelayCommand]
        private async Task PromoteAsync(BillingQueueItemViewModel item)
        {
            if (!item.IsValid)
                return;

            try
            {
                await _billingService.CreateClaimLineAsync(
                    CurrentActor(),
                    item.Result.Note.Id,
                    item.Result.Note.ComplianceOverride,
                    item.Result.Note.OverrideReason);
                QueueItems.Remove(item);
                RefreshCounts();
                StatusMessage = "The selected service was added to its draft billing period.";
            }
            catch
            {
                StatusMessage = "The service changed or could not be promoted. Refresh the queue before retrying.";
                throw;
            }
        }

        [RelayCommand]
        private async Task PromoteSelectedAsync()
        {
            var toPromote = QueueItems
                .Where(r => r.IsSelected && r.IsValid)
                .ToList();

            var promoted = 0;
            foreach (var item in toPromote)
            {
                try
                {
                    await _billingService.CreateClaimLineAsync(
                        CurrentActor(),
                        item.Result.Note.Id,
                        item.Result.Note.ComplianceOverride,
                        item.Result.Note.OverrideReason);
                    QueueItems.Remove(item);
                    promoted++;
                }
                catch
                {
                    StatusMessage = $"Promoted {promoted} service(s). A later service changed or failed; refresh before retrying.";
                    RefreshCounts();
                    throw;
                }
            }

            RefreshCounts();
            StatusMessage = $"Promoted {promoted} service(s) into draft billing periods.";
        }

        [RelayCommand]
        private void SelectAllReady()
        {
            foreach (var item in QueueItems.Where(r => r.IsValid))
                item.IsSelected = true;
            RefreshCounts();
        }

        [RelayCommand]
        private async Task RefreshAsync() => await LoadAsync();

        private void RefreshCounts()
        {
            OnPropertyChanged(nameof(ValidCount));
            OnPropertyChanged(nameof(InvalidCount));
            OnPropertyChanged(nameof(SelectedValidCount));
        }

        public void ClearForAccountSwitch()
        {
            _accountLoads.Invalidate();
            QueueItems.Clear();
            StatusMessage = string.Empty;
            IsBusy = false;
            HasLoaded = false;
            RefreshCounts();
        }

        private Sati.Contracts.V1.AgencyActor CurrentActor() =>
            _sessionService.CurrentUser?.ToAgencyActor()
            ?? throw new UnauthorizedAccessException("A signed-in user is required.");
    }
}
