using System.Globalization;
using System.Windows.Data;

namespace Sati.Converters;

public sealed class NullableBoolChoiceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool selected || !bool.TryParse(parameter?.ToString(), out var option))
            return false;
        return selected == option;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || !bool.TryParse(parameter?.ToString(), out var option))
            return Binding.DoNothing;
        return (bool?)option;
    }
}
