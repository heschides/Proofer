using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Contracts.V1;
using Sati.Data.Billing;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Billing;

public partial class BillingAlertsViewModel(IBillingService billingService) : ObservableObject
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly List<BillingWorklistRow> _allItems = [];
    public ObservableCollection<BillingWorklistRow> Items { get; } = [];
    public ObservableCollection<string> StatusFilters { get; } = ["All statuses"];
    public ObservableCollection<string> AgingFilters { get; } = ["All ages", "0–29", "30–59", "60–89", "90–119", "120+"];

    [ObservableProperty] private string? searchText;
    [ObservableProperty] private string selectedStatus = "All statuses";
    [ObservableProperty] private string selectedAging = "All ages";
    [ObservableProperty] private string? statusMessage;
    public bool HasLoaded { get; private set; }

    partial void OnSearchTextChanged(string? value) => ApplyFilters();
    partial void OnSelectedStatusChanged(string value) => ApplyFilters();
    partial void OnSelectedAgingChanged(string value) => ApplyFilters();

    public async Task LoadAsync()
    {
        if (!await _loadGate.WaitAsync(0)) return;
        try
        {
            _allItems.Clear();
            var today = DateTime.Today;
            foreach (var outcome in await billingService.GetRemittanceOutcomesAsync())
            {
                if (outcome.Status == RemittanceClaimStatus.Paid.ToString()) continue;
                var age = Math.Max(0, (today - outcome.ReceivedAtUtc.ToLocalTime().Date).Days);
                _allItems.Add(new BillingWorklistRow(
                    outcome.Id, outcome.ClaimReference, outcome.PayerName, outcome.ReceivedAtUtc,
                    outcome.Status, age, AgingBucket(age), outcome.BilledAmount, outcome.PaidAmount,
                    outcome.ReasonCode,
                    ClaimAdjustmentReasonCatalog.Humanize(outcome.ReasonCode, outcome.Explanation),
                    outcome.PaymentReference, outcome.IsSynthetic));
            }

            StatusFilters.Clear();
            StatusFilters.Add("All statuses");
            foreach (var status in _allItems.Select(item => item.Status).Distinct().Order())
                StatusFilters.Add(status);
            ApplyFilters();
            HasLoaded = true;
            StatusMessage = _allItems.Count == 0
                ? "There are no denied, unpaid, reversed, or review-needed claims."
                : $"{_allItems.Count} claim(s) need billing follow-up.";
        }
        catch (Exception ex) { StatusMessage = $"Unable to load the denial worklist: {ex.Message}"; }
        finally { _loadGate.Release(); }
    }

    private void ApplyFilters()
    {
        var search = SearchText?.Trim();
        var filtered = _allItems
            .Where(item => SelectedStatus == "All statuses" || item.Status == SelectedStatus)
            .Where(item => SelectedAging == "All ages" || item.AgingBucket == SelectedAging)
            .Where(item => string.IsNullOrWhiteSpace(search) ||
                item.ClaimReference.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.PayerName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (item.ReasonCode?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                item.HumanReason.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.ReceivedAtUtc.ToString("yyyy-MM-dd").Contains(search, StringComparison.OrdinalIgnoreCase))
            // Oldest first, which is the same ordering the previous
            // "AgeDays descending, then ReceivedAtUtc ascending" produced — AgeDays is
            // computed from ReceivedAtUtc, so the second key only ever broke ties the first
            // key had already created within a single day. Saying it once is clearer, and
            // Id keeps the order stable when two outcomes arrive in the same second.
            .OrderBy(item => item.ReceivedAtUtc)
            .ThenBy(item => item.Id)
            .ToList();
        Items.Clear();
        foreach (var item in filtered) Items.Add(item);
    }

    private static string AgingBucket(int days) => days switch
    {
        >= 120 => "120+", >= 90 => "90–119", >= 60 => "60–89", >= 30 => "30–59", _ => "0–29"
    };
}

public sealed record BillingWorklistRow(
    long Id, string ClaimReference, string PayerName, DateTime ReceivedAtUtc, string Status,
    int AgeDays, string AgingBucket, decimal BilledAmount, decimal PaidAmount, string? ReasonCode,
    string HumanReason, string? PaymentReference, bool IsSynthetic);
