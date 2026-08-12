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
        private readonly INoteService _noteService;
        private readonly IIncentiveService _incentiveService;
        private readonly IExemptDateService _exemptDateService;
        private readonly IConsumerBillingLossReportService _billingLossReportService;

        public StatisticsViewModel(
            ISessionService sessionService,
            INoteService noteService,
            IIncentiveService incentiveService,
            IExemptDateService exemptDateService,
            IConsumerBillingLossReportService billingLossReportService,
            ThemeService themeService)
        {
            _sessionService = sessionService;
            _noteService = noteService;
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

        [RelayCommand]
        private async Task ShowThisMonthAsync()
        {
            SelectedStartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            SelectedEndDate = DateTime.Today;
            await LoadAsync();
        }

        [RelayCommand]
        private async Task ApplyDateWindowAsync() => await LoadAsync();

        // Rebuilt on every navigate and every filter application.
        public async Task LoadAsync()
        {
            var user = _sessionService.CurrentUser;
            if (user is null) return;

            if (SelectedStartDate is not DateTime selectedStart
                || SelectedEndDate is not DateTime selectedEnd)
            {
                DateFilterMessage = "Choose both a start date and an end date.";
                return;
            }

            var windowStart = selectedStart.Date;
            var windowEnd = selectedEnd.Date;
            if (windowEnd < windowStart)
            {
                DateFilterMessage = "The end date must be on or after the start date.";
                return;
            }

            DateFilterMessage = string.Empty;
            var window = BuildMonthWindow(windowStart, windowEnd);
            var billingLossTask = _billingLossReportService.GetAsync(
                user.Id, windowStart, windowEnd);

            var years = window.Select(m => m.Year).Distinct().ToList();

            var notes = new List<Note>();
            foreach (var year in years)
                notes.AddRange(await _noteService.GetByYearAsync(user.Id, year));

            var exempt = new List<ExemptDate>();
            foreach (var year in years)
                exempt.AddRange(await _exemptDateService.GetByYearAsync(user.Id, year));

            // Productive units retain the page's established Logged + Approved definition,
            // but are now clipped to the exact selected dates rather than whole months.
            var unitsByMonth = notes
                .Where(n => n.Status is NoteStatus.Logged or NoteStatus.Approved
                         && n.EventDate.HasValue
                         && n.EventDate.Value.Date >= windowStart
                         && n.EventDate.Value.Date <= windowEnd)
                .GroupBy(n => (n.EventDate!.Value.Year, n.EventDate.Value.Month))
                .ToDictionary(g => g.Key, g => g.Sum(n => n.Units ?? 0));

            // Detached history snapshots can be safely adjusted in memory for a partial
            // first/last month; no report read creates or persists an Incentive row.
            var snapshots = (await _incentiveService.GetHistoryAsync(user.Id))
                .ToDictionary(i => (i.Year, i.Month));

            Months.Clear();
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
                        : await _incentiveService.GetEligibleDaysAsync(periodStart, periodEnd);
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

                Months.Add(new ProductivityMonth(
                    MonthLabel: FormatPeriodLabel(periodStart, periodEnd, month, monthEnd),
                    Units: units,
                    Threshold: threshold,
                    AttainmentPercent: attainment,
                    Incentive: incentive,
                    StatusLevel: statusLevel));
            }

            WindowLabel = FormatWindowLabel(windowStart, windowEnd);
            TotalUnits = Months.Sum(m => m.Units);
            TotalIncentive = Months.All(m => m.Incentive.HasValue)
                ? Months.Sum(m => m.Incentive!.Value)
                : null;
            HasData = Months.Any(m => m.Units > 0);
            UnitsChartModel = BuildUnitsChart(Months);

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
