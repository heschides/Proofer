using System;
using System.Globalization;
using System.Windows.Data;

namespace Sati.Converters
{
    /// <summary>
    /// Scales a 0–1 position along the service day to pixels against the track's
    /// measured width. Keeping the math here rather than in the view model lets
    /// the bar resize with its panel without the view model knowing about layout.
    /// </summary>
    public class TimelineFractionConverter : IMultiValueConverter
    {
        /// <summary>Floor applied to the result, so a very short note stays visible.</summary>
        public double MinimumPixels { get; set; }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 ||
                values[0] is not double fraction ||
                values[1] is not double trackWidth ||
                double.IsNaN(trackWidth) || double.IsInfinity(trackWidth) || trackWidth <= 0)
            {
                return MinimumPixels;
            }

            return Math.Max(MinimumPixels, Math.Round(fraction * trackWidth));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
