using CommunityToolkit.Mvvm.ComponentModel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Sati.Data;
using Sati.Models;
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

        public StatisticsViewModel(
            ISessionService sessionService,
            INoteService noteService,
            IIncentiveService incentiveService,
            IExemptDateService exemptDateService)
        {
            _sessionService = sessionService;
            _noteService = noteService;
            _incentiveService = incentiveService;
            _exemptDateService = exemptDateService;
        }

        public ObservableCollection<ProductivityMonth> Months { get; } = [];

        [ObservableProperty] private PlotModel? unitsChartModel;
        [ObservableProperty] private string windowLabel = string.Empty;
        [ObservableProperty] private int totalUnits;
        [ObservableProperty] private decimal totalIncentive;
        [ObservableProperty] private bool hasData;

        // Rebuilt on every navigate, so the history is current each visit.
        public async Task LoadAsync()
        {
            var user = _sessionService.CurrentUser;
            if (user is null) return;

            // The 12 completed months ending last month. The current (partial) month is
            // live on Overview; a half-filled bar beside twelve finished ones reads as a dip.
            var anchor = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var window = Enumerable.Range(1, 12)
                .Select(i => anchor.AddMonths(-i))  // i = 1..12  → newest..oldest
                .Reverse()                          // flip to oldest..newest for the chart
                .ToList();

            // Notes and exempt days both come back per calendar year; the window spans
            // at most two years, so this is one or two round-trips each.
            var years = window.Select(m => m.Year).Distinct().ToList();

            var notes = new List<Note>();
            foreach (var year in years)
                notes.AddRange(await _noteService.GetByYearAsync(user.Id, year));

            var exempt = new List<ExemptDate>();
            foreach (var year in years)
                exempt.AddRange(await _exemptDateService.GetByYearAsync(user.Id, year));

            // Exempt (non-productivity) days per month — the same source the live dashboard
            // queries, keyed by (year, month) to line up with the snapshots.
            var exemptCountByMonth = exempt
                .GroupBy(e => (e.Date.Year, e.Date.Month))
                .ToDictionary(g => g.Key, g => g.Count());

            // Productive units = Logged + Approved (Approved is where logged notes land after
            // supervisor sign-off, so excluding it would erase most of a closed month).
            // Keyed by a (year, month) value tuple — cheap, structural-equality dictionary key.
            var unitsByMonth = notes
                .Where(n => n.Status is NoteStatus.Logged or NoteStatus.Approved
                         && n.EventDate.HasValue)
                .GroupBy(n => (n.EventDate!.Value.Year, n.EventDate.Value.Month))
                .ToDictionary(g => g.Key, g => g.Sum(n => n.Units ?? 0));

            // Read-only snapshots — GetHistoryAsync creates and mutates nothing.
            var snapshots = (await _incentiveService.GetHistoryAsync(user.Id))
                .ToDictionary(i => (i.Year, i.Month));

            Months.Clear();
            foreach (var month in window)
            {
                var key = (month.Year, month.Month);
                var units = unitsByMonth.GetValueOrDefault(key, 0);
                snapshots.TryGetValue(key, out var snapshot);

                // Knock that month's exempt days off the scheduled days, mirroring the live
                // dashboard's effective-days math. Safe to mutate: GetHistoryAsync hands back
                // detached AsNoTracking copies and we never SaveChanges, so this can't persist.
                // Both Threshold and Calculate(...) derive from DaysScheduled, so this single
                // adjustment corrects the attainment and the incentive at once.
                if (snapshot is not null)
                {
                    var exemptDays = exemptCountByMonth.GetValueOrDefault(key, 0);
                    snapshot.DaysScheduled = Math.Max(0, snapshot.DaysScheduled - exemptDays);
                }

                int? threshold = snapshot?.Threshold;

                // Relational pattern: `is > 0` is false when threshold is null, so this guards
                // both "no snapshot" and "zero threshold" (no divide-by-zero) in one test.
                decimal? attainment = threshold is > 0
                    ? Math.Round(100m * units / threshold.Value, 0)
                    : null;

                decimal? incentive = snapshot?.Calculate(units);

                var statusLevel = attainment switch
                {
                    null => "Unknown",
                    >= 100 => "Ok",
                    >= 50 => "Warning",
                    _ => "Danger"
                };

                Months.Add(new ProductivityMonth(
                    MonthLabel: month.ToString("MMM yyyy"),
                    Units: units,
                    Threshold: threshold,
                    AttainmentPercent: attainment,
                    Incentive: incentive,
                    StatusLevel: statusLevel));
            }

            WindowLabel = $"{window[0]:MMM yyyy} – {window[^1]:MMM yyyy}";
            TotalUnits = Months.Sum(m => m.Units);
            TotalIncentive = Months.Sum(m => m.Incentive ?? 0);
            HasData = Months.Any(m => m.Units > 0);

            UnitsChartModel = BuildUnitsChart(Months);
        }

        private static PlotModel BuildUnitsChart(IReadOnlyList<ProductivityMonth> months)
        {
            var model = new PlotModel
            {
                Background = OxyColors.Transparent,
                PlotAreaBackground = OxyColors.Transparent,
                TextColor = OxyColor.FromRgb(0x3D, 0x2B, 0x1F),
                PlotMargins = new OxyThickness(60, 10, 16, 30),
            };

            // Months down the left as a horizontal bar chart — OxyPlot's BarSeries (the one
            // TeamOverview uses) is horizontal; the vertical ColumnSeries isn't in this build.
            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                TextColor = OxyColor.FromRgb(0x3D, 0x2B, 0x1F),
                TicklineColor = OxyColors.Transparent,
                MajorGridlineStyle = LineStyle.None,
                GapWidth = 0.4,
            };

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0,
                Title = "Units (Logged + Approved)",
                TitleColor = OxyColor.FromRgb(0x8A, 0x7A, 0x6A),
                TextColor = OxyColor.FromRgb(0x3D, 0x2B, 0x1F),
                TicklineColor = OxyColor.FromRgb(0xED, 0xD9, 0xC0),
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(80, 0x3D, 0x2B, 0x1F),
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
                        "Ok" => OxyColor.FromRgb(0x5A, 0x8A, 0x5A),
                        "Warning" => OxyColor.FromRgb(0xC8, 0x79, 0x41),
                        "Danger" => OxyColor.FromRgb(0xA6, 0x60, 0x7A),
                        _ => OxyColor.FromRgb(0xD4, 0xA8, 0x82),
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