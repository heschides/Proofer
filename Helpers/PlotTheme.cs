using OxyPlot;
using System.Windows;
using System.Windows.Media;

namespace Sati.Helpers
{
    /// <summary>
    /// Bridges WPF theme brushes into OxyPlot, whose colors cannot use
    /// DynamicResource directly. Resolve colors each time a plot is rebuilt.
    /// </summary>
    public static class PlotTheme
    {
        public static OxyColor Color(string resourceKey, OxyColor fallback)
        {
            if (Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush)
            {
                var color = brush.Color;
                return OxyColor.FromArgb(color.A, color.R, color.G, color.B);
            }

            return fallback;
        }

        public static OxyColor ColorWithAlpha(
            string resourceKey,
            byte alpha,
            OxyColor fallback)
        {
            var color = Color(resourceKey, fallback);
            return OxyColor.FromArgb(alpha, color.R, color.G, color.B);
        }
    }
}
