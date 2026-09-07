using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Edi;
using Sati.Models.Billing;
using Sati.Contracts.V1;
using Sati.Services;
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
        private readonly LatestRequestTracker _accountLoads = new();
        private readonly Dictionary<int, string> _pendingEdiKeys = [];
        private readonly HashSet<int> _generatedTestPeriodIds = [];
        private readonly HashSet<int> _generatedPeriodIds = [];
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
        public ObservableCollection<BillingPeriod> DraftBillingPeriods { get; } = [];
        public ObservableCollection<ClaimLine> SelectedPeriodLines { get; } = [];
        public ObservableCollection<BillingPeriod> GenerationPeriods { get; } = [];
        public ObservableCollection<BillingGenerationStageRow> StagedPeriods { get; } = [];
        public ObservableCollection<BillingGenerationStageRow> BlockedSubmittedPeriods { get; } = [];
        public ObservableCollection<BillingSubmissionHistoryDto> SubmissionHistory { get; } = [];
        public ObservableCollection<BillingSubmissionBatchRow> SubmissionBatches { get; } = [];
        public IReadOnlyList<MockClearinghouseScenarioOption> MockClearinghouseScenarios { get; } =
            MockClearinghouseScenarioOption.All;

        /// <summary>
        /// Counts of what is shown, by what a biller would do about it. These describe the
        /// filtered list rather than everything, so the header never disagrees with the
        /// rows underneath it.
        /// </summary>
        public int NotSubmittedCount =>
            SubmissionBatches.Count(item => item.Progress == BillingSubmissionProgress.NotSubmitted);

        public int NeedsAttentionCount =>
            SubmissionBatches.Count(item => item.Progress == BillingSubmissionProgress.NeedsAttention);

        public int AwaitingPayerCount =>
            SubmissionBatches.Count(item => item.Progress == BillingSubmissionProgress.AwaitingPayer);

        public int SettledCount =>
            SubmissionBatches.Count(item => item.Progress == BillingSubmissionProgress.Settled);

        /// <summary>One line stating what the list currently holds, in that order.</summary>
        public string SubmissionSummary =>
            $"{NotSubmittedCount} not submitted · {NeedsAttentionCount} need attention · " +
            $"{AwaitingPayerCount} awaiting payer · {SettledCount} settled";
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
        [ObservableProperty] private MockClearinghouseScenarioOption selectedMockClearinghouseScenario =
            MockClearinghouseScenarioOption.All[0];
        public bool HasLoaded { get; private set; }

        public bool HasSelectedPeriod => SelectedPeriod is not null;
        public bool HasDraftPeriods => DraftBillingPeriods.Count > 0;
        public bool HasSelectedPeriodLines => SelectedPeriodLines.Count > 0;
        public bool HasStagedPeriods => StagedPeriods.Count > 0;
        public bool HasBlockedSubmittedPeriods => BlockedSubmittedPeriods.Count > 0;
        public int SelectedPeriodIssueCount => SelectedPeriodLines.Count(line => !line.IsReadyForSubmission);
        public int SelectedPeriodReadyCount => SelectedPeriodLines.Count - SelectedPeriodIssueCount;
        public string SelectedPeriodLineSummary => SelectedPeriod is null
            ? "Choose a draft period to inspect its claim lines."
            : $"{SelectedPeriodLines.Count} claim {(SelectedPeriodLines.Count == 1 ? "line" : "lines")} · " +
              $"{SelectedPeriodReadyCount} ready · {SelectedPeriodIssueCount} need correction";
        public string DraftPeriodSummary => DraftBillingPeriods.Count == 0
            ? "No draft periods are waiting to be submitted. Submitted periods appear in the 837 range and Submission Home below."
            : $"{DraftBillingPeriods.Count} draft {(DraftBillingPeriods.Count == 1 ? "period is" : "periods are")} waiting. Submitted periods leave this list and move to the 837 workflow below.";
        public bool HasGeneratedFile => !string.IsNullOrWhiteSpace(LastGeneratedPath);
        public bool CanSubmitPeriod => !IsGenerating && SelectedPeriod is
            { Status: BillingStatus.Draft, Lines.Count: > 0 } period &&
            period.Lines.All(line => line.IsReadyForSubmission) &&
            !HasInvalidClaimAmounts(period);
        public string SubmitAvailabilityMessage => SelectedPeriod switch
        {
            null => "Choose a draft billing period to submit and lock.",
            { Status: BillingStatus.Draft, Lines.Count: 0 } =>
                "This draft has no claims and cannot be submitted.",
            { Status: BillingStatus.Draft } period when period.Lines.Any(line => !line.IsReadyForSubmission) =>
                $"{period.Lines.Count(line => !line.IsReadyForSubmission)} claim " +
                $"{(period.Lines.Count(line => !line.IsReadyForSubmission) == 1 ? "line needs" : "lines need")} correction before this period can be submitted.",
            { Status: BillingStatus.Draft } period when HasInvalidClaimAmounts(period) =>
                "This draft contains a claim with zero units or a $0 charge. Correct and rebuild the affected claim before submitting.",
            { Status: BillingStatus.Draft } period =>
                $"Ready: submitting will permanently lock {PeriodName(period)} with " +
                $"{period.Lines.Count} {(period.Lines.Count == 1 ? "claim" : "claims")}.",
            { Status: BillingStatus.Submitted } period =>
                $"{PeriodName(period)} is already submitted and locked. Use the range below to generate its 837P file.",
            { } period =>
                $"{PeriodName(period)} is {period.Status.ToString().ToLowerInvariant()} and cannot be submitted again."
        };
        public bool CanGenerateEdi => !IsGenerating && IsRangeValid && GenerationPeriods.Count > 0;
        public bool ShowsMockClearinghouse => _billingService.SupportsMockClearinghouse;
        public bool CanSubmitToMockClearinghouse =>
            ShowsMockClearinghouse && IsTestMode && !IsGenerating && MockSubmissionPeriods().Count > 0;
        public string MockClearinghouseAvailabilityMessage
        {
            get
            {
                if (!ShowsMockClearinghouse)
                    return "The mock clearinghouse is available only in Demo.";
                if (!IsTestMode)
                    return "Turn on Test mode and generate the 837P files before using the mock clearinghouse.";
                var ready = MockSubmissionPeriods().Count;
                return ready == 0
                    ? "Generate the test 837P files for this range first. Only files generated in this session can be submitted."
                    : $"{ready} generated test 837P {(ready == 1 ? "file is" : "files are")} ready for simulated submission.";
            }
        }
        public bool IsRangeValid => RangeStart.HasValue && RangeEnd.HasValue &&
            MonthStart(RangeStart.Value) <= MonthStart(RangeEnd.Value);
        public string RangeSummary => !IsRangeValid
            ? "Choose a valid beginning and ending billing month."
            : $"{StagedPeriods.Count} ready in staging · {GenerationPeriods.Count} selected · " +
              $"{BlockedSubmittedPeriods.Count} blocked before 837.";

        partial void OnSelectedPeriodChanged(BillingPeriod? value)
        {
            SelectedPeriodLines.Clear();
            if (value is not null)
            {
                foreach (var line in value.Lines.OrderBy(line => line.DateOfService).ThenBy(line => line.Id))
                    SelectedPeriodLines.Add(line);
            }
            OnPropertyChanged(nameof(HasSelectedPeriod));
            OnPropertyChanged(nameof(HasSelectedPeriodLines));
            OnPropertyChanged(nameof(SelectedPeriodIssueCount));
            OnPropertyChanged(nameof(SelectedPeriodReadyCount));
            OnPropertyChanged(nameof(SelectedPeriodLineSummary));
            OnPropertyChanged(nameof(CanSubmitPeriod));
            OnPropertyChanged(nameof(CanGenerateEdi));
            OnPropertyChanged(nameof(SubmitAvailabilityMessage));
        }

        partial void OnLastGeneratedPathChanged(string? value)
            => OnPropertyChanged(nameof(HasGeneratedFile));

        partial void OnIsGeneratingChanged(bool value)
        {
            OnPropertyChanged(nameof(CanSubmitPeriod));
            OnPropertyChanged(nameof(CanGenerateEdi));
            NotifyMockClearinghouseStateChanged();
        }

        partial void OnRangeStartChanged(DateTime? value) => RebuildGenerationPeriods();
        partial void OnRangeEndChanged(DateTime? value) => RebuildGenerationPeriods();
        partial void OnIsTestModeChanged(bool value)
        {
            ResetPendingBatch();
            _generatedTestPeriodIds.Clear();
            NotifyMockClearinghouseStateChanged();
        }
        partial void OnSubmissionSearchTextChanged(string? value) => ApplySubmissionFilters();
        partial void OnOutstandingOnlyChanged(bool value) => ApplySubmissionFilters();
        partial void OnSelectedSubmissionStatusChanged(string value) => ApplySubmissionFilters();

        public async Task LoadAsync(bool waitForExisting = false)
        {
            if (waitForExisting)
                await _loadGate.WaitAsync();
            else if (!await _loadGate.WaitAsync(0))
                return;
            var user = _sessionService.CurrentUser;
            var request = _accountLoads.Begin();

            try
            {
                var previouslySelectedId = SelectedPeriod?.Id;
                user = _sessionService.CurrentUser
                    ?? throw new UnauthorizedAccessException("A signed-in user is required.");
                var actor = user.ToAgencyActor();
                var periods = await _billingService.GetAllBillingPeriodsAsync(actor);
                var history = await _billingService.GetSubmissionHistoryAsync(actor);
                if (!_accountLoads.IsCurrent(request) || !ReferenceEquals(_sessionService.CurrentUser, user))
                    return;

                BillingPeriods.Clear();
                DraftBillingPeriods.Clear();
                GenerationPeriods.Clear();
                StagedPeriods.Clear();
                BlockedSubmittedPeriods.Clear();
                SubmissionHistory.Clear();
                _generatedTestPeriodIds.Clear();
                _generatedPeriodIds.Clear();
                ResetPendingBatch();

                foreach (var period in periods)
                {
                    BillingPeriods.Add(period);
                    if (period.Status == BillingStatus.Draft && period.Lines.Count > 0)
                        DraftBillingPeriods.Add(period);
                }

                SelectedPeriod = DraftBillingPeriods.SingleOrDefault(period => period.Id == previouslySelectedId)
                    ?? DraftBillingPeriods.FirstOrDefault();
                OnPropertyChanged(nameof(HasDraftPeriods));
                OnPropertyChanged(nameof(DraftPeriodSummary));

                foreach (var item in history)
                    SubmissionHistory.Add(item);
                RebuildSubmissionBatches();

                var progressedPeriodIds = SubmissionHistory
                    .Where(item => HasExchangeStage(item.Stage))
                    .Select(item => item.BillingPeriodId)
                    .ToHashSet();
                var submittedWithClaims = BillingPeriods
                    .Where(period => period.Status == BillingStatus.Submitted && period.Lines.Count > 0)
                    .Where(period => !progressedPeriodIds.Contains(period.Id))
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
                if (_accountLoads.IsCurrent(request) && ReferenceEquals(_sessionService.CurrentUser, user))
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
                await _billingService.SubmitBillingPeriodAsync(CurrentActor(), selectedId);
                HasLoaded = false;
                await LoadAsync();
                StatusMessage = "Billing period submitted and locked. It left the draft queue and moved into 837 staging.";
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
            var fingerprint = BatchFingerprint(periods, isTest);
            if (!string.Equals(_pendingBatchFingerprint, fingerprint, StringComparison.Ordinal))
                ResetPendingBatch(fingerprint);

            BillingPeriod? currentPeriod = null;
            try
            {
                IsGenerating = true;
                var completed = 0;
                foreach (var period in periods)
                {
                    currentPeriod = period;
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
                    if (isTest)
                        _generatedTestPeriodIds.Add(period.Id);
                    _generatedPeriodIds.Add(period.Id);
                    completed++;
                }

                await RefreshSubmissionHistoryAsync();
                RebuildGenerationPeriods();
                StatusMessage = periods.Count == 1
                    ? $"1 staged period was captured in an 837P file and removed from staging. File saved: {LastGeneratedPath}"
                    : $"{periods.Count} staged periods were captured in {periods.Count} 837P files and removed from staging. The last file is: {LastGeneratedPath}";
                if (isTest && _billingService.SupportsMockClearinghouse)
                    StatusMessage += " They are ready for the mock clearinghouse below.";
                ResetPendingBatch();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GenerateEdi failed: {ex.Message}");
                var periodName = currentPeriod is null
                    ? "the selected range"
                    : PeriodName(currentPeriod);
                var supportReference = ex is Sati.Data.Cloud.CloudApiException { CorrelationId.Length: > 0 } cloud
                    ? $" Support reference: {cloud.CorrelationId}."
                    : string.Empty;
                try
                {
                    await RefreshSubmissionHistoryAsync();
                    RebuildGenerationPeriods();
                    if (IsRangeValid)
                        _pendingBatchFingerprint = BatchFingerprint(GenerationPeriods, isTest);
                }
                catch (Exception refreshFailure)
                {
                    Debug.WriteLine($"Submission staging refresh after generation failure also failed: {refreshFailure.Message}");
                }
                StatusMessage = $"Could not generate {periodName}: {ex.Message}{supportReference}";
            }
            finally
            {
                IsGenerating = false;
                NotifyMockClearinghouseStateChanged();
            }
        }

        [RelayCommand]
        private async Task SubmitToMockClearinghouse()
        {
            var periods = MockSubmissionPeriods();
            if (!CanSubmitToMockClearinghouse || periods.Count == 0)
                return;

            var completed = 0;
            var responseTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var claimOutcomes = 0;
            var deposits = 0;
            try
            {
                IsGenerating = true;
                foreach (var period in periods)
                {
                    StatusMessage = $"Submitting test 837P file {completed + 1} of {periods.Count} to the mock clearinghouse...";
                    var result = await _billingService.SubmitToMockClearinghouseAsync(
                        CurrentActor(), period.Id, SelectedMockClearinghouseScenario.Scenario);
                    responseTypes.Add("999");
                    if (result.ClaimAcknowledgement is not null)
                        responseTypes.Add("277CA");
                    if (result.RemittanceAdvice is not null)
                        responseTypes.Add("835");
                    claimOutcomes += result.ClaimOutcomesRecorded;
                    deposits += result.DepositRecorded ? 1 : 0;
                    _generatedTestPeriodIds.Remove(period.Id);
                    completed++;
                }

                await RefreshSubmissionHistoryAsync();
                var responses = string.Join(", ", responseTypes.Order(StringComparer.OrdinalIgnoreCase));
                StatusMessage =
                    $"Mock clearinghouse processed {completed} test 837P {(completed == 1 ? "file" : "files")} as " +
                    $"{SelectedMockClearinghouseScenario.Label}. Responses recorded: {responses}. " +
                    $"Claim outcomes: {claimOutcomes}; remittance deposits: {deposits}. " +
                    "Review Submission Home below and the Remittances tab.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Mock clearinghouse submission failed: {ex.Message}");
                try
                {
                    await RefreshSubmissionHistoryAsync();
                }
                catch (Exception refreshFailure)
                {
                    Debug.WriteLine($"Submission history refresh after mock failure also failed: {refreshFailure.Message}");
                }
                StatusMessage = completed == 0
                    ? $"Mock clearinghouse submission failed: {ex.Message}"
                    : $"The mock clearinghouse processed {completed} file(s), then stopped: {ex.Message}";
            }
            finally
            {
                IsGenerating = false;
                NotifyMockClearinghouseStateChanged();
            }
        }

        private void RebuildGenerationPeriods()
        {
            if (_settingInitialRange)
                return;

            var previouslySelected = StagedPeriods
                .Where(row => row.IsSelected)
                .Select(row => row.Period.Id)
                .ToHashSet();
            GenerationPeriods.Clear();
            StagedPeriods.Clear();
            BlockedSubmittedPeriods.Clear();
            if (IsRangeValid)
            {
                var start = MonthStart(RangeStart!.Value);
                var end = MonthStart(RangeEnd!.Value);
                var progressedPeriodIds = SubmissionHistory
                    .Where(item => HasExchangeStage(item.Stage))
                    .Select(item => item.BillingPeriodId)
                    .ToHashSet();
                foreach (var period in BillingPeriods
                    .Where(period => period.Status == BillingStatus.Submitted && period.Lines.Count > 0)
                    .Where(period => !progressedPeriodIds.Contains(period.Id) &&
                        !_generatedPeriodIds.Contains(period.Id))
                    .Where(period =>
                    {
                        var month = new DateTime(period.Year, period.Month, 1);
                        return month >= start && month <= end;
                    })
                    .OrderBy(period => period.Year)
                    .ThenBy(period => period.Month)
                    .ThenBy(period => period.UserId))
                {
                    var row = new BillingGenerationStageRow(
                        period,
                        previouslySelected.Count == 0 || previouslySelected.Contains(period.Id),
                        RebuildSelectedGenerationPeriods);
                    if (period.Lines.All(line => line.IsReadyForSubmission) && !HasInvalidClaimAmounts(period))
                        StagedPeriods.Add(row);
                    else
                        BlockedSubmittedPeriods.Add(row);
                }
            }

            RebuildSelectedGenerationPeriods();
            OnPropertyChanged(nameof(IsRangeValid));
            OnPropertyChanged(nameof(CanGenerateEdi));
            OnPropertyChanged(nameof(RangeSummary));
            OnPropertyChanged(nameof(HasStagedPeriods));
            OnPropertyChanged(nameof(HasBlockedSubmittedPeriods));
            NotifyMockClearinghouseStateChanged();
        }

        private void RebuildSelectedGenerationPeriods()
        {
            GenerationPeriods.Clear();
            foreach (var row in StagedPeriods.Where(row => row.IsSelected))
                GenerationPeriods.Add(row.Period);
            OnPropertyChanged(nameof(CanGenerateEdi));
            OnPropertyChanged(nameof(RangeSummary));
        }

        private List<BillingPeriod> MockSubmissionPeriods() => BillingPeriods
            .Where(period => _generatedTestPeriodIds.Contains(period.Id))
            .ToList();

        [RelayCommand]
        private async Task ReturnToDraft(BillingGenerationStageRow? row)
        {
            if (row is null || IsGenerating || !BlockedSubmittedPeriods.Contains(row))
                return;

            try
            {
                IsGenerating = true;
                StatusMessage = $"Returning {PeriodName(row.Period)} to the draft queue...";
                await _billingService.ReturnBillingPeriodToDraftAsync(CurrentActor(), row.Period.Id);
                HasLoaded = false;
                await LoadAsync();
                StatusMessage = $"{PeriodName(row.Period)} was returned to the draft queue. Its claim issues must be corrected before it can be submitted again.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Return billing period to draft failed: {ex.Message}");
                StatusMessage = $"Could not return the billing period to draft: {ex.Message}";
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private async Task RefreshSubmissionHistoryAsync()
        {
            var account = _sessionService.CurrentUser
                ?? throw new UnauthorizedAccessException("A signed-in user is required.");
            var request = _accountLoads.Begin();
            var history = await _billingService.GetSubmissionHistoryAsync(account.ToAgencyActor());
            if (!_accountLoads.IsCurrent(request) || !ReferenceEquals(_sessionService.CurrentUser, account))
                return;
            SubmissionHistory.Clear();
            foreach (var item in history)
                SubmissionHistory.Add(item);
            RebuildSubmissionBatches();
        }

        private void NotifyMockClearinghouseStateChanged()
        {
            OnPropertyChanged(nameof(CanSubmitToMockClearinghouse));
            OnPropertyChanged(nameof(MockClearinghouseAvailabilityMessage));
        }

        public void ClearForAccountSwitch()
        {
            _accountLoads.Invalidate();
            SelectedPeriod = null;
            BillingPeriods.Clear();
            DraftBillingPeriods.Clear();
            SelectedPeriodLines.Clear();
            GenerationPeriods.Clear();
            StagedPeriods.Clear();
            BlockedSubmittedPeriods.Clear();
            SubmissionHistory.Clear();
            SubmissionBatches.Clear();
            _allSubmissionBatches.Clear();
            _generatedTestPeriodIds.Clear();
            _generatedPeriodIds.Clear();
            ResetPendingBatch();
            LastGeneratedPath = null;
            StatusMessage = null;
            RangeStart = null;
            RangeEnd = null;
            SubmissionSearchText = null;
            OutstandingOnly = true;
            SubmissionStatusFilters.Clear();
            SubmissionStatusFilters.Add("All statuses");
            SelectedSubmissionStatus = "All statuses";
            SelectedMockClearinghouseScenario = MockClearinghouseScenarioOption.All[0];
            HasLoaded = false;
            OnPropertyChanged(nameof(HasDraftPeriods));
            OnPropertyChanged(nameof(HasSelectedPeriodLines));
            OnPropertyChanged(nameof(SelectedPeriodIssueCount));
            OnPropertyChanged(nameof(SelectedPeriodReadyCount));
            OnPropertyChanged(nameof(SelectedPeriodLineSummary));
            OnPropertyChanged(nameof(DraftPeriodSummary));
            OnPropertyChanged(nameof(HasStagedPeriods));
            OnPropertyChanged(nameof(HasBlockedSubmittedPeriods));
            OnPropertyChanged(nameof(RangeSummary));
            NotifyMockClearinghouseStateChanged();
        }

        private void ResetPendingBatch(string? fingerprint = null)
        {
            _pendingEdiKeys.Clear();
            _pendingBatchFingerprint = fingerprint;
        }

        private string BatchFingerprint(IEnumerable<BillingPeriod> periods, bool isTest) =>
            $"{MonthStart(RangeStart!.Value):yyyyMM}:{MonthStart(RangeEnd!.Value):yyyyMM}:" +
            $"{isTest}:{string.Join(',', periods.Select(period => period.Id))}";

        private static bool HasExchangeStage(string stage) =>
            Enum.TryParse<BillingSubmissionStage>(stage, out _);

        private void RebuildSubmissionBatches()
        {
            _allSubmissionBatches.Clear();
            var periodsWithSubmissionEvents = SubmissionHistory
                .Select(item => item.BillingPeriodId)
                .ToHashSet();
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

            // A claim line leaves the billing queue as soon as it enters a period. Until
            // that period produces its first submission event, this derived read-model
            // row is the only place the unsubmitted work remains visible. Its age comes
            // from service delivery because timely-filing limits run from that date.
            foreach (var period in BillingPeriods.Where(period =>
                         period.Lines.Count > 0 && !periodsWithSubmissionEvents.Contains(period.Id)))
            {
                var oldestServiceDate = period.Lines.Min(line => line.DateOfService).Date;
                var oldestServiceDateUtc = DateTime.SpecifyKind(
                    oldestServiceDate, DateTimeKind.Local).ToUniversalTime();
                _allSubmissionBatches.Add(new BillingSubmissionBatchRow(
                    period.Id, period.Year, period.Month,
                    period.User?.DisplayName ?? $"User {period.UserId}",
                    period.Lines.Count, period.Lines.Sum(line => line.ChargeAmount),
                    null, oldestServiceDateUtc,
                    BillingSubmissionProgressRules.Describe(BillingSubmissionProgress.NotSubmitted),
                    null, null, null, false, BillingSubmissionProgress.NotSubmitted));
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
            OnPropertyChanged(nameof(NotSubmittedCount));
            OnPropertyChanged(nameof(NeedsAttentionCount));
            OnPropertyChanged(nameof(AwaitingPayerCount));
            OnPropertyChanged(nameof(SettledCount));
            OnPropertyChanged(nameof(SubmissionSummary));
        }

        private static DateTime MonthStart(DateTime value) => new(value.Year, value.Month, 1);

        private static bool HasInvalidClaimAmounts(BillingPeriod period) =>
            period.Lines.Any(line => line.Units is null or <= 0 || line.ChargeAmount <= 0);

        private static string PeriodName(BillingPeriod period)
        {
            var manager = string.IsNullOrWhiteSpace(period.CaseManagerName)
                ? $"case manager #{period.UserId}"
                : period.CaseManagerName;
            return $"{manager}'s {new DateTime(period.Year, period.Month, 1):MMMM yyyy} period";
        }

        private AgencyActor CurrentActor() => _sessionService.CurrentUser?.ToAgencyActor()
            ?? throw new UnauthorizedAccessException("A signed-in user is required.");

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

    public sealed record MockClearinghouseScenarioOption(
        MockClearinghouseScenario Scenario,
        string Label,
        string Explanation)
    {
        public static IReadOnlyList<MockClearinghouseScenarioOption> All { get; } =
        [
            new(MockClearinghouseScenario.Accepted, "Accepted and paid", "999 and 277CA accepted; 835 pays the claims in full."),
            new(MockClearinghouseScenario.SyntaxRejected, "837 syntax rejected", "999 rejects the file; no claims or remittance follow."),
            new(MockClearinghouseScenario.ClaimsRejected, "Claims rejected", "999 accepts the file; 277CA rejects every claim."),
            new(MockClearinghouseScenario.PartiallyAccepted, "Partially accepted", "277CA accepts some claims and rejects others."),
            new(MockClearinghouseScenario.PartialPayment, "Partially paid", "835 pays less than billed and records a contractual adjustment."),
            new(MockClearinghouseScenario.Denied, "Denied on remittance", "Claims are accepted, then denied with a reason code on the 835."),
            new(MockClearinghouseScenario.ProviderLevelAdjustment, "Provider-level adjustment", "835 includes an adjustment that changes the deposit total."),
            new(MockClearinghouseScenario.Reversal, "Payment reversed", "835 reverses a previously paid claim."),
        ];
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
        /// <summary>The heading this batch sits under: not submitted, needs attention, awaiting payer, or settled.</summary>
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
