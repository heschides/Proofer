using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Edi;
using Sati.Models.Billing;
using Sati.Contracts.V1;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Sati.ViewModels.Billing
{
    public partial class BillingSubmissionsViewModel : ObservableObject
    {
        private readonly IBillingService _billingService;
        private readonly IEdiService _ediService;
        private readonly ISessionService _sessionService;
        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private readonly Dictionary<int, string> _pendingEdiKeys = [];
        private string? _pendingBatchFingerprint;
        private bool _settingInitialRange;

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
        public ObservableCollection<BillingPeriod> GenerationPeriods { get; } = [];
        public ObservableCollection<BillingSubmissionHistoryDto> SubmissionHistory { get; } = [];
        public ObservableCollection<BillingSubmissionBatchRow> SubmissionBatches { get; } = [];

        /// <summary>
        /// Counts of what is shown, by what a biller would do about it. These describe the
        /// filtered list rather than everything, so the header never disagrees with the
        /// rows underneath it.
        /// </summary>
        public int NeedsAttentionCount =>
            SubmissionBatches.Count(item => item.Progress == BillingSubmissionProgress.NeedsAttention);

        public int AwaitingPayerCount =>
            SubmissionBatches.Count(item => item.Progress == BillingSubmissionProgress.AwaitingPayer);

        public int SettledCount =>
            SubmissionBatches.Count(item => item.Progress == BillingSubmissionProgress.Settled);

        /// <summary>One line stating what the list currently holds, in that order.</summary>
        public string SubmissionSummary =>
            $"{NeedsAttentionCount} need attention · {AwaitingPayerCount} awaiting payer · {SettledCount} settled";
        public ObservableCollection<string> SubmissionStatusFilters { get; } = ["All statuses"];
        private readonly List<BillingSubmissionBatchRow> _allSubmissionBatches = [];

        [ObservableProperty] private BillingPeriod? selectedPeriod;
        [ObservableProperty] private string? lastGeneratedPath;
        [ObservableProperty] private string? statusMessage;
        [ObservableProperty] private bool isGenerating;
        [ObservableProperty] private bool isTestMode = true;
        [ObservableProperty] private DateTime? rangeStart;
        [ObservableProperty] private DateTime? rangeEnd;
        [ObservableProperty] private string? submissionSearchText;
        [ObservableProperty] private bool outstandingOnly = true;
        [ObservableProperty] private string selectedSubmissionStatus = "All statuses";
        public bool HasLoaded { get; private set; }

        public bool HasSelectedPeriod => SelectedPeriod is not null;
        public bool HasGeneratedFile => !string.IsNullOrWhiteSpace(LastGeneratedPath);
        public bool CanSubmitPeriod => !IsGenerating && SelectedPeriod is
            { Status: BillingStatus.Draft, Lines.Count: > 0 };
        public bool CanGenerateEdi => !IsGenerating && IsRangeValid && GenerationPeriods.Count > 0;
        public bool IsRangeValid => RangeStart.HasValue && RangeEnd.HasValue &&
            MonthStart(RangeStart.Value) <= MonthStart(RangeEnd.Value);
        public string RangeSummary => !IsRangeValid
            ? "Choose a valid beginning and ending billing month."
            : GenerationPeriods.Count == 0
                ? "No submitted billing periods with claims are in this range."
                : $"{GenerationPeriods.Count} submitted billing period(s) will produce " +
                  $"{GenerationPeriods.Count} separate 837P file(s).";

        partial void OnSelectedPeriodChanged(BillingPeriod? value)
        {
            OnPropertyChanged(nameof(HasSelectedPeriod));
            OnPropertyChanged(nameof(CanSubmitPeriod));
            OnPropertyChanged(nameof(CanGenerateEdi));
        }

        partial void OnLastGeneratedPathChanged(string? value)
            => OnPropertyChanged(nameof(HasGeneratedFile));

        partial void OnIsGeneratingChanged(bool value)
        {
            OnPropertyChanged(nameof(CanSubmitPeriod));
            OnPropertyChanged(nameof(CanGenerateEdi));
        }

        partial void OnRangeStartChanged(DateTime? value) => RebuildGenerationPeriods();
        partial void OnRangeEndChanged(DateTime? value) => RebuildGenerationPeriods();
        partial void OnIsTestModeChanged(bool value) => ResetPendingBatch();
        partial void OnSubmissionSearchTextChanged(string? value) => ApplySubmissionFilters();
        partial void OnOutstandingOnlyChanged(bool value) => ApplySubmissionFilters();
        partial void OnSelectedSubmissionStatusChanged(string value) => ApplySubmissionFilters();

        public async Task LoadAsync()
        {
            if (!await _loadGate.WaitAsync(0))
                return;

            try
            {
                BillingPeriods.Clear();
                GenerationPeriods.Clear();
                SubmissionHistory.Clear();
                var user = _sessionService.CurrentUser!;
                var periods = user.Role is UserRole.Admin or UserRole.Supervisor
                    ? await _billingService.GetAllBillingPeriodsAsync()
                    : await _billingService.GetBillingPeriodsAsync(user.Id);
                var history = await _billingService.GetSubmissionHistoryAsync();

                foreach (var period in periods)
                    BillingPeriods.Add(period);

                foreach (var item in history)
                    SubmissionHistory.Add(item);
                RebuildSubmissionBatches();

                var submittedWithClaims = BillingPeriods
                    .Where(period => period.Status == BillingStatus.Submitted && period.Lines.Count > 0)
                    .ToList();
                _settingInitialRange = true;
                RangeStart = submittedWithClaims.Count == 0
                    ? MonthStart(DateTime.Today)
                    : submittedWithClaims.Min(period => new DateTime(period.Year, period.Month, 1));
                RangeEnd = submittedWithClaims.Count == 0
                    ? MonthStart(DateTime.Today)
                    : submittedWithClaims.Max(period => new DateTime(period.Year, period.Month, 1));
                _settingInitialRange = false;
                RebuildGenerationPeriods();

                HasLoaded = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BillingSubmissionsViewModel.LoadAsync failed: {ex.Message}");
                StatusMessage = "Failed to load billing periods.";
            }
            finally
            {
                _loadGate.Release();
            }
        }

        [RelayCommand]
        private async Task SubmitPeriod()
        {
            if (!CanSubmitPeriod || SelectedPeriod is null)
                return;

            try
            {
                IsGenerating = true;
                StatusMessage = "Submitting and locking billing period...";
                var selectedId = SelectedPeriod.Id;
                await _billingService.SubmitBillingPeriodAsync(selectedId);
                HasLoaded = false;
                await LoadAsync();
                SelectedPeriod = BillingPeriods.SingleOrDefault(period => period.Id == selectedId);
                StatusMessage = "Billing period submitted and locked. It is ready for 837P generation.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Submit billing period failed: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsGenerating = false;
            }
        }

        [RelayCommand]
        private async Task GenerateEdi()
        {
            if (!CanGenerateEdi)
                return;

            var periods = GenerationPeriods.ToList();
            var isTest = IsTestMode;
            var fingerprint = $"{MonthStart(RangeStart!.Value):yyyyMM}:{MonthStart(RangeEnd!.Value):yyyyMM}:" +
                $"{isTest}:{string.Join(',', periods.Select(period => period.Id))}";
            if (!string.Equals(_pendingBatchFingerprint, fingerprint, StringComparison.Ordinal))
                ResetPendingBatch(fingerprint);

            try
            {
                IsGenerating = true;
                var completed = 0;
                foreach (var period in periods)
                {
                    StatusMessage = $"Generating 837P file {completed + 1} of {periods.Count}...";
                    if (!_pendingEdiKeys.TryGetValue(period.Id, out var key))
                    {
                        key = Guid.NewGuid().ToString("N");
                        _pendingEdiKeys[period.Id] = key;
                    }

                    LastGeneratedPath = await _ediService.GenerateAndSaveAsync(
                        period.Id,
                        isTest,
                        key);
                    completed++;
                }

                StatusMessage = periods.Count == 1
                    ? $"1 file saved: {LastGeneratedPath}"
                    : $"{periods.Count} files saved. The last file is: {LastGeneratedPath}";
                ResetPendingBatch();
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

        private void RebuildGenerationPeriods()
        {
            if (_settingInitialRange)
                return;

            GenerationPeriods.Clear();
            if (IsRangeValid)
            {
                var start = MonthStart(RangeStart!.Value);
                var end = MonthStart(RangeEnd!.Value);
                foreach (var period in BillingPeriods
                    .Where(period => period.Status == BillingStatus.Submitted && period.Lines.Count > 0)
                    .Where(period =>
                    {
                        var month = new DateTime(period.Year, period.Month, 1);
                        return month >= start && month <= end;
                    })
                    .OrderBy(period => period.Year)
                    .ThenBy(period => period.Month)
                    .ThenBy(period => period.UserId))
                {
                    GenerationPeriods.Add(period);
                }
            }

            ResetPendingBatch();
            OnPropertyChanged(nameof(IsRangeValid));
            OnPropertyChanged(nameof(CanGenerateEdi));
            OnPropertyChanged(nameof(RangeSummary));
        }

        private void ResetPendingBatch(string? fingerprint = null)
        {
            _pendingEdiKeys.Clear();
            _pendingBatchFingerprint = fingerprint;
        }

        private void RebuildSubmissionBatches()
        {
            _allSubmissionBatches.Clear();
            foreach (var group in SubmissionHistory.GroupBy(item => item.BillingPeriodId))
            {
                var ordered = group.OrderBy(item => item.OccurredAtUtc).ToList();
                var latest = ordered[^1];
                var period = BillingPeriods.SingleOrDefault(item => item.Id == group.Key);
                var transmitted = ordered.FirstOrDefault(item => item.Stage == BillingSubmissionStage.Transmitted.ToString());
                _allSubmissionBatches.Add(new BillingSubmissionBatchRow(
                    latest.BillingPeriodId, latest.Year, latest.Month, latest.CaseManagerName,
                    latest.ClaimCount, period?.Lines.Sum(line => line.ChargeAmount) ?? 0m,
                    transmitted?.OccurredAtUtc, latest.OccurredAtUtc, latest.Stage,
                    latest.Reference, latest.ResponseType, latest.Explanation, latest.IsSynthetic,
                    BillingSubmissionProgressRules.Classify(latest.Stage)));
            }

            SubmissionStatusFilters.Clear();
            SubmissionStatusFilters.Add("All statuses");
            foreach (var status in _allSubmissionBatches.Select(item => item.CurrentStatus).Distinct().Order())
                SubmissionStatusFilters.Add(status);
            if (!SubmissionStatusFilters.Contains(SelectedSubmissionStatus))
                SelectedSubmissionStatus = "All statuses";
            ApplySubmissionFilters();
        }

        private void ApplySubmissionFilters()
        {
            var search = SubmissionSearchText?.Trim();
            var filtered = _allSubmissionBatches
                .Where(item => !OutstandingOnly || item.IsOutstanding)
                .Where(item => SelectedSubmissionStatus == "All statuses" || item.CurrentStatus == SelectedSubmissionStatus)
                .Where(item => string.IsNullOrWhiteSpace(search) ||
                    item.CaseManagerName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (item.Reference?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    item.CurrentStatus.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    $"{item.Year:D4}-{item.Month:D2}".Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Work first, then what is merely waiting, then what is done — and within each
            // group, oldest first where age means urgency. Sorting the whole list by one
            // timestamp put a rejection from three weeks ago below a payment from an hour
            // ago, which is the ambiguity this list existed to remove.
            var ordered = filtered
                .GroupBy(item => item.Progress)
                .OrderBy(group => BillingSubmissionProgressRules.SortOrder(group.Key))
                .SelectMany(group => BillingSubmissionProgressRules.OldestFirst(group.Key)
                    ? group.OrderBy(item => item.LastActivityAtUtc)
                           .ThenBy(item => item.BillingPeriodId)
                    : group.OrderByDescending(item => item.LastActivityAtUtc)
                           .ThenBy(item => item.BillingPeriodId));

            SubmissionBatches.Clear();
            foreach (var item in ordered)
                SubmissionBatches.Add(item);
            OnPropertyChanged(nameof(NeedsAttentionCount));
            OnPropertyChanged(nameof(AwaitingPayerCount));
            OnPropertyChanged(nameof(SettledCount));
            OnPropertyChanged(nameof(SubmissionSummary));
        }

        private static DateTime MonthStart(DateTime value) => new(value.Year, value.Month, 1);

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

    public sealed record BillingSubmissionBatchRow(
        int BillingPeriodId,
        int Year,
        int Month,
        string CaseManagerName,
        int ClaimCount,
        decimal DollarValue,
        DateTime? SentAtUtc,
        DateTime LastActivityAtUtc,
        string CurrentStatus,
        string? Reference,
        string? ResponseType,
        string? Explanation,
        bool IsSynthetic,
        BillingSubmissionProgress Progress)
    {
        /// <summary>The heading this batch sits under: needs attention, awaiting payer, or settled.</summary>
        public string ProgressName => BillingSubmissionProgressRules.Describe(Progress);

        /// <summary>
        /// What <see cref="LastActivityAtUtc"/> means for this batch. A single timestamp
        /// column whose meaning changes by row is worse than no column; naming it per row
        /// is what makes it readable.
        /// </summary>
        public string ActivityLabel => BillingSubmissionProgressRules.DescribeActivity(Progress);

        /// <summary>Local-time activity date, which is the one a biller is reading against a calendar.</summary>
        public DateTime ActivityLocal => LastActivityAtUtc.ToLocalTime();

        /// <summary>The billing month this batch covers, distinct from when anything happened to it.</summary>
        public string BillingMonth => $"{Year:D4}-{Month:D2}";

        /// <summary>Days since the last thing happened, which is the age a stuck batch is judged by.</summary>
        public int DaysSinceActivity =>
            Math.Max(0, (DateTime.Now.Date - LastActivityAtUtc.ToLocalTime().Date).Days);

        /// <summary>Still needs something from somebody, whether work or patience.</summary>
        public bool IsOutstanding => Progress != BillingSubmissionProgress.Settled;
    }
}
