using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data.Billing;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Sati.ViewModels.Billing
{
    public partial class BillingQueueViewModel : ObservableObject
    {
        private readonly IBillingService _billingService;

        public ObservableCollection<BillingQueueItemViewModel> QueueItems { get; } = [];

        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private string statusMessage = string.Empty;
        public bool HasLoaded { get; private set; }

        public int ValidCount => QueueItems.Count(r => r.IsValid);
        public int InvalidCount => QueueItems.Count(r => !r.IsValid);
        public int SelectedValidCount => QueueItems.Count(r => r.IsSelected && r.IsValid);

        public BillingQueueViewModel(IBillingService billingService)
        {
            _billingService = billingService;
        }

        public async Task LoadAsync()
        {
            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                Debug.WriteLine($"[BillingQueue] LoadAsync started — {DateTime.Now:HH:mm:ss.fff}");
                var configuration = await _billingService.GetBillingConfigurationAsync();
                var notes = await _billingService.GetApprovedUnbilledNotesAsync();
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
            catch
            {
                StatusMessage = "The billing queue could not be loaded. Please try Refresh.";
                throw;
            }
            finally
            {
                IsBusy = false;
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
    }
}
