using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace Sati.Converters
{
    public class BoolToWidthConverter : IValueConverter
    {
        public double TrueWidth { get; set; } = 240;
        public double FalseWidth { get; set; } = 0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? TrueWidth : FalseWidth;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
