using CommunityToolkit.Mvvm.ComponentModel;
using OxyPlot;
using CommunityToolkit.Mvvm.Input;
using OxyPlot.Axes;
using OxyPlot.Series;
using Sati.Data;
using Sati.Helpers;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;

namespace Sati.ViewModels
{
    // One row of the history table. A record because it's an immutable snapshot of a
    // computed month — nothing about a past month changes once we've built it.
    public record ProductivityMonth(
        string MonthLabel,
        int Units,
        int? Threshold,
        decimal? AttainmentPercent,
        decimal? Incentive,
        string StatusLevel);

    public partial class StatisticsViewModel : ObservableObject
    {
        private readonly ISessionService _sessionService;
        private readonly IProductivityReportService _productivityReportService;
        private readonly IIncentiveService _incentiveService;
        private readonly IExemptDateService _exemptDateService;
        private readonly IConsumerBillingLossReportService _billingLossReportService;
        private readonly LatestRequestTracker _loadRequests = new();

        public StatisticsViewModel(
            ISessionService sessionService,
            IProductivityReportService productivityReportService,
            IIncentiveService incentiveService,
            IExemptDateService exemptDateService,
            IConsumerBillingLossReportService billingLossReportService,
            ThemeService themeService)
        {
            _sessionService = sessionService;
            _productivityReportService = productivityReportService;
            _incentiveService = incentiveService;
            _exemptDateService = exemptDateService;
            _billingLossReportService = billingLossReportService;
            themeService.ThemeChanged += (_, _) =>
            {
                if (Months.Count > 0)
                    UnitsChartModel = BuildUnitsChart(Months);
            };
        }

        public ObservableCollection<ProductivityMonth> Months { get; } = [];
        public ObservableCollection<ConsumerBillingLossRow> ConsumerBillingRows { get; } = [];

        [ObservableProperty] private PlotModel? unitsChartModel;
        [ObservableProperty] private string windowLabel = string.Empty;
        [ObservableProperty] private int totalUnits;
        [ObservableProperty] private decimal? totalIncentive;
        [ObservableProperty] private bool hasData;
        [ObservableProperty] private bool hasConsumerBillingRows;
        [ObservableProperty] private int totalBillableWorkUnits;
        [ObservableProperty] private int totalNonBillableWorkUnits;
        [ObservableProperty] private string totalLostWorkPercentageLabel = "—";
        [ObservableProperty] private DateTime? selectedStartDate =
            new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        [ObservableProperty] private DateTime? selectedEndDate = DateTime.Today;
        [ObservableProperty] private string dateFilterMessage = string.Empty;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private bool hasLoadError;
        [ObservableProperty] private string loadErrorMessage = string.Empty;

        [RelayCommand]
        private async Task ShowThisMonthAsync()
        {
            SelectedStartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            SelectedEndDate = DateTime.Today;
            await LoadAsync();
        }

        [RelayCommand]
        private async Task ApplyDateWindowAsync() => await LoadAsync();

        // Rebuilt on every navigation and filter application. Independent reads
        // are started together, and only the newest request may publish results.
        public async Task LoadAsync()
        {
            var request = _loadRequests.Begin();
            HasLoadError = false;
            LoadErrorMessage = string.Empty;
            var user = _sessionService.CurrentUser;
            if (user is null)
            {
                IsLoading = false;
                return;
            }

            if (SelectedStartDate is not DateTime selectedStart
                || SelectedEndDate is not DateTime selectedEnd)
            {
                DateFilterMessage = "Choose both a start date and an end date.";
                IsLoading = false;
                return;
            }

            var windowStart = selectedStart.Date;
            var windowEnd = selectedEnd.Date;
            if (windowEnd < windowStart)
            {
                DateFilterMessage = "The end date must be on or after the start date.";
                IsLoading = false;
                return;
            }
            if (windowStart.Year < 2000 || windowEnd.Year > 2200 ||
                (windowEnd - windowStart).TotalDays > 3_660)
            {
                DateFilterMessage =
                    "Choose a reporting window from 2000 through 2200 that is no longer than 10 years.";
                IsLoading = false;
                return;
            }

            DateFilterMessage = string.Empty;
            IsLoading = true;

            try
            {
                var window = BuildMonthWindow(windowStart, windowEnd);
                var years = window.Select(m => m.Year).Distinct().ToList();
                var productivityTask = _productivityReportService.GetUnitsAsync(
                    windowStart, windowEnd);
                var billingLossTask = _billingLossReportService.GetAsync(
                    user.Id, windowStart, windowEnd);
                var historyTask = _incentiveService.GetHistoryAsync(user.Id);
                var exemptTasks = years
                    .Select(year => _exemptDateService.GetByYearAsync(user.Id, year))
                    .ToArray();

                // Partial months need an exact eligible-day count. Start those calls
                // before awaiting any database or API request so network latency overlaps.
                var eligibleDayTasks = new Dictionary<(int Year, int Month), Task<int>>();
                foreach (var month in window)
                {
                    var periodStart = month > windowStart ? month : windowStart;
                    var monthEnd = month.AddMonths(1).AddDays(-1);
                    var periodEnd = monthEnd < windowEnd ? monthEnd : windowEnd;
                    if (periodStart != month || periodEnd != monthEnd)
                    {
                        eligibleDayTasks[(month.Year, month.Month)] =
                            _incentiveService.GetEligibleDaysAsync(periodStart, periodEnd);
                    }
                }

                var exemptResultsTask = Task.WhenAll(exemptTasks);
                var eligibleDaysCompletion = Task.WhenAll(eligibleDayTasks.Values);
                await Task.WhenAll(
                    productivityTask,
                    billingLossTask,
                    historyTask,
                    exemptResultsTask,
                    eligibleDaysCompletion);

                if (!_loadRequests.IsCurrent(request))
                    return;

                var unitsByMonth = (await productivityTask)
                    .ToDictionary(item => (item.Year, item.Month), item => item.Units);
                var exempt = (await exemptResultsTask).SelectMany(items => items).ToList();

                // Detached history snapshots can be safely adjusted in memory for
                // a partial first/last month; report reads never persist an Incentive.
                var snapshots = (await historyTask)
                    .ToDictionary(i => (i.Year, i.Month));
                var computedMonths = new List<ProductivityMonth>(window.Count);
                foreach (var month in window)
                {
                    var key = (month.Year, month.Month);
                    var units = unitsByMonth.GetValueOrDefault(key, 0);
                    snapshots.TryGetValue(key, out var snapshot);

                    var periodStart = month > windowStart ? month : windowStart;
                    var monthEnd = month.AddMonths(1).AddDays(-1);
                    var periodEnd = monthEnd < windowEnd ? monthEnd : windowEnd;
                    var coversFullMonth = periodStart == month && periodEnd == monthEnd;

                    if (snapshot is not null)
                    {
                        var exemptDays = exempt.Count(e => e.Date.Date >= periodStart
                                                        && e.Date.Date <= periodEnd);
                        var eligibleDays = coversFullMonth
                            ? snapshot.DaysScheduled
                            : await eligibleDayTasks[key];
                        snapshot.DaysScheduled = Math.Max(0, eligibleDays - exemptDays);
                    }

                    int? threshold = snapshot?.Threshold;
                    decimal? attainment = threshold is > 0
                        ? Math.Round(100m * units / threshold.Value, 0)
                        : null;
                    // Incentives are monthly awards. A partial date window can show
                    // prorated attainment, but must not claim a prorated award.
                    decimal? incentive = snapshot is not null && coversFullMonth
                        ? snapshot.Calculate(units)
                        : null;

                    var statusLevel = attainment switch
                    {
                        null => "Unknown",
                        >= 100 => "Ok",
                        >= 50 => "Warning",
                        _ => "Danger"
                    };

                    computedMonths.Add(new ProductivityMonth(
                        MonthLabel: FormatPeriodLabel(periodStart, periodEnd, month, monthEnd),
                        Units: units,
                        Threshold: threshold,
                        AttainmentPercent: attainment,
                        Incentive: incentive,
                        StatusLevel: statusLevel));
                }

                if (!_loadRequests.IsCurrent(request))
                    return;

                Months.Clear();
                foreach (var month in computedMonths)
                    Months.Add(month);

                WindowLabel = FormatWindowLabel(windowStart, windowEnd);
                TotalUnits = computedMonths.Sum(m => m.Units);
                TotalIncentive = computedMonths.All(m => m.Incentive.HasValue)
                    ? computedMonths.Sum(m => m.Incentive!.Value)
                    : null;
                HasData = computedMonths.Any(m => m.Units > 0);
                UnitsChartModel = BuildUnitsChart(computedMonths);

                var billingLossReport = await billingLossTask;
                ConsumerBillingRows.Clear();
                foreach (var row in billingLossReport.Consumers)
                    ConsumerBillingRows.Add(row);

                TotalBillableWorkUnits = billingLossReport.TotalBillableUnits;
                TotalNonBillableWorkUnits = billingLossReport.TotalNonBillableUnits;
                TotalLostWorkPercentageLabel = billingLossReport.LostWorkPercentage is decimal percentage
                    ? $"{percentage:0.0}%"
                    : "—";
                HasConsumerBillingRows = ConsumerBillingRows.Count > 0;
            }
            catch (Exception ex)
            {
                if (!_loadRequests.IsCurrent(request))
                    return;

                var reference = AppErrorLog.Record(ex, "statistics.load");
                HasLoadError = true;
                LoadErrorMessage =
                    "Statistics could not be loaded. Try Apply again. " +
                    $"Support reference: {reference}.";
            }
            finally
            {
                if (_loadRequests.IsCurrent(request))
                    IsLoading = false;
            }
        }

        private static List<DateTime> BuildMonthWindow(DateTime start, DateTime end)
        {
            var months = new List<DateTime>();
            for (var month = new DateTime(start.Year, start.Month, 1);
                 month <= end;
                 month = month.AddMonths(1))
            {
                months.Add(month);
            }

            return months;
        }

        private static string FormatPeriodLabel(
            DateTime periodStart,
            DateTime periodEnd,
            DateTime monthStart,
            DateTime monthEnd)
        {
            if (periodStart == monthStart && periodEnd == monthEnd)
                return monthStart.ToString("MMM yyyy");
            if (periodStart == periodEnd)
                return periodStart.ToString("MMM d, yyyy");
            if (periodStart.Month == periodEnd.Month && periodStart.Year == periodEnd.Year)
                return $"{periodStart:MMM d}–{periodEnd:d, yyyy}";

            return $"{periodStart:MMM d, yyyy}–{periodEnd:MMM d, yyyy}";
        }

        private static string FormatWindowLabel(DateTime start, DateTime end)
        {
            if (start == end)
                return start.ToString("MMMM d, yyyy");
            if (start.Month == end.Month && start.Year == end.Year)
                return $"{start:MMMM d}–{end:d, yyyy}";
            if (start.Year == end.Year)
                return $"{start:MMM d}–{end:MMM d, yyyy}";

            return $"{start:MMM d, yyyy}–{end:MMM d, yyyy}";
        }

        private static PlotModel BuildUnitsChart(IReadOnlyList<ProductivityMonth> months)
        {
            var textColor = PlotTheme.Color(
                "TextPrimaryBrush", OxyColor.FromRgb(0x3D, 0x2B, 0x1F));
            var mutedColor = PlotTheme.Color(
                "TextMutedBrush", OxyColor.FromRgb(0x8A, 0x7A, 0x6A));
            var inputBorderColor = PlotTheme.Color(
                "InputBorderBrush", OxyColor.FromRgb(0xED, 0xD9, 0xC0));

            var model = new PlotModel
            {
                Background = OxyColors.Transparent,
                PlotAreaBackground = OxyColors.Transparent,
                TextColor = textColor,
                PlotMargins = new OxyThickness(60, 10, 16, 30),
            };

            // Months down the left as a horizontal bar chart — OxyPlot's BarSeries (the one
            // TeamOverview uses) is horizontal; the vertical ColumnSeries isn't in this build.
            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                TextColor = textColor,
                TicklineColor = OxyColors.Transparent,
                MajorGridlineStyle = LineStyle.None,
                GapWidth = 0.4,
            };

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0,
                Title = "Units (Logged + Approved)",
                TitleColor = mutedColor,
                TextColor = textColor,
                TicklineColor = inputBorderColor,
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = PlotTheme.ColorWithAlpha(
                    "TextPrimaryBrush", 80, textColor),
            };

            var series = new BarSeries
            {
                StrokeColor = OxyColors.Transparent,
                BarWidth = 0.6,
            };

            foreach (var m in months)
            {
                categoryAxis.Labels.Add(m.MonthLabel);
                series.Items.Add(new BarItem
                {
                    Value = m.Units,
                    // Same status palette as TeamOverview, plus a muted tone for months with
                    // no threshold snapshot (we can't judge attainment, so we don't pretend to).
                    Color = m.StatusLevel switch
                    {
                        "Ok" => PlotTheme.Color("CompliantBrush", OxyColor.FromRgb(0x5A, 0x8A, 0x5A)),
                        "Warning" => PlotTheme.Color("WarningBrush", OxyColor.FromRgb(0xC8, 0x79, 0x41)),
                        "Danger" => PlotTheme.Color("OverdueBrush", OxyColor.FromRgb(0xA6, 0x60, 0x7A)),
                        _ => PlotTheme.Color("AccentBrush", OxyColor.FromRgb(0xD4, 0xA8, 0x82)),
                    },
                });
            }

            model.Axes.Add(categoryAxis);
            model.Axes.Add(valueAxis);
            model.Series.Add(series);
            return model;
        }
    }
}
