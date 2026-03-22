using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Controls;
using System.Windows.Data;
using static Sati.Enums;

namespace Sati.Converters
{
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is null || string.IsNullOrEmpty(parameter.ToString()))
                return value is null;
            return value?.ToString() == parameter?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is true)
            {
                if (parameter is null || string.IsNullOrEmpty(parameter.ToString()))
                    //cast suppresses null warning
                    return (NoteType?)null!;

                return Enum.Parse(typeof(NoteType), parameter!.ToString()!);
            }
                return Binding.DoNothing;
            
        }
    }
}
