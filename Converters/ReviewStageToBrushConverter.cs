using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Sati.Converters
{
    // Maps a ReviewStage to the fill of its block in the caseload grid. The
    // first three steps deepen along the warm palette — pale for untouched,
    // darker as the workflow advances — so a row of gaps reads as light and a
    // completed quarter reads as solid. Scanning for pale blocks is the whole
    // point of the grid.
    //
    // Logged breaks the warm ramp for the sage used elsewhere as "done"
    // (FormWorkflowState.Completed borders on the task board). The hue shift is
    // the signal: terminal state, not just further along. It stays dark enough
    // for the light-foreground trigger in CellBlockTemplate.
    public class ReviewStageToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush NotStarted = Freeze("#F5E6D3");
        private static readonly SolidColorBrush Requested = Freeze("#B08553");
        private static readonly SolidColorBrush Received = Freeze("#A8C5D6");
        private static readonly SolidColorBrush Logged = Freeze("#6B8E6B");
        private static readonly SolidColorBrush Fallback = Freeze("#E8D0B8");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not ReviewStage stage)
                return Fallback;

            return stage switch
            {
                ReviewStage.NotStarted => NotStarted,
                ReviewStage.Requested => Requested,
                ReviewStage.Received => Received,
                ReviewStage.Logged => Logged,
                _ => Fallback
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("ReviewStageToBrushConverter is one-way.");

        // Freezing makes a brush immutable and shareable across threads, which
        // lets WPF skip change-tracking on it. Standard practice for static
        // brushes used in many places; measurable at grid scale.
        private static SolidColorBrush Freeze(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}