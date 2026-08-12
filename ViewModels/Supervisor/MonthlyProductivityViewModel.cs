using CommunityToolkit.Mvvm.ComponentModel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using Sati.Helpers;

namespace Sati.ViewModels.Supervisor
{
    public partial class MonthlyProductivityViewModel : ObservableObject
    {
        [ObservableProperty] private IReadOnlyList<CaseManagerSummaryViewModel> caseManagers = [];
        [ObservableProperty] private PlotModel? statusChartModel;

        [ObservableProperty] private int teamLogged;
        [ObservableProperty] private int teamPending;
        [ObservableProperty] private int teamAbandoned;
        [ObservableProperty] private int teamScheduled;
        [ObservableProperty] private int teamCancelled;
        [ObservableProperty] private int teamDelayed;

        public void Refresh(IReadOnlyList<CaseManagerSummaryViewModel> managers)
        {
            StatusChartModel = null; 
          CaseManagers = managers;
            TeamLogged = managers.Sum(cm => cm.LoggedCount);
            TeamPending = managers.Sum(cm => cm.PendingCount);
            TeamAbandoned = managers.Sum(cm => cm.AbandonedCount);
            TeamScheduled = managers.Sum(cm => cm.ScheduledCount);
            TeamCancelled = managers.Sum(cm => cm.CancelledCount);
            TeamDelayed = managers.Sum(cm => cm.DelayedCount);
            StatusChartModel = BuildStatusChart(managers);
        }

        private static PlotModel BuildStatusChart(IReadOnlyList<CaseManagerSummaryViewModel> managers)
        {
            var textColor = PlotTheme.Color(
                "TextPrimaryBrush", OxyColor.FromRgb(0x3D, 0x2B, 0x1F));

            var model = new PlotModel
            {
                Background = OxyColors.Transparent,
                PlotAreaBackground = OxyColors.Transparent,
                TextColor = textColor,
            };

            model.Legends.Add(new Legend
            {
                LegendPlacement = LegendPlacement.Outside,
                LegendPosition = LegendPosition.BottomCenter,
                LegendOrientation = LegendOrientation.Horizontal,
                LegendTextColor = textColor,
                LegendBackground = OxyColors.Transparent,
                LegendBorder = OxyColors.Transparent,
            });

            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                TextColor = textColor,
                TicklineColor = OxyColors.Transparent,
            };

            foreach (var cm in managers)
                categoryAxis.Labels.Add(cm.DisplayName);

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0,
                Title = "Notes",
                TextColor = textColor,
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = PlotTheme.ColorWithAlpha(
                    "TextPrimaryBrush", 80, textColor),
            };

            // Each status is a stacked bar series. Order matters visually.
            var statusSeries = new (string Label, OxyColor Color, Func<CaseManagerSummaryViewModel, int> Getter)[]
            {
                ("Logged",    PlotTheme.Color("CompliantBrush", OxyColor.FromRgb(0x5A, 0x8A, 0x5A)), cm => cm.LoggedCount),
                ("Scheduled", PlotTheme.Color("InfoBrush", OxyColor.FromRgb(0x5B, 0x7F, 0xA6)), cm => cm.ScheduledCount),
                ("Pending",   PlotTheme.Color("WarningBrush", OxyColor.FromRgb(0xC8, 0x79, 0x41)), cm => cm.PendingCount),
                ("Delayed",   PlotTheme.Color("OpenedFormBrush", OxyColor.FromRgb(0xD4, 0xA0, 0x60)), cm => cm.DelayedCount),
                ("Cancelled", PlotTheme.Color("TextMutedBrush", OxyColor.FromRgb(0x8A, 0x7A, 0x6A)), cm => cm.CancelledCount),
                ("Abandoned", PlotTheme.Color("OverdueBrush", OxyColor.FromRgb(0xA6, 0x60, 0x7A)), cm => cm.AbandonedCount),
            };

            foreach (var (label, color, getter) in statusSeries)
            {
                var series = new BarSeries
                {
                    Title = label,
                    IsStacked = true,
                    FillColor = color,
                    StrokeColor = OxyColors.Transparent,
                    BarWidth = 0.6,
                };
                foreach (var cm in managers)
                    series.Items.Add(new BarItem { Value = getter(cm) });
                model.Series.Add(series);
            }

            model.Axes.Add(categoryAxis);
            model.Axes.Add(valueAxis);

            return model;
        }
    }
}
