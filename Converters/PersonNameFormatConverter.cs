using System.Globalization;
using System.Windows.Data;
using Sati.Models;

namespace Sati.Converters;

/// <summary>
/// Formats a Person's name as "Last, First" when the consumer-picker sort preference is on, or
/// "First Last" otherwise. Deliberately not a property on Person itself — display format driven
/// by a personal UI preference does not belong on the shared domain model.
/// </summary>
public sealed class PersonNameFormatConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not Person person)
            return string.Empty;

        var lastNameFirst = values[1] is true;
        return lastNameFirst
            ? ((person.LastName ?? string.Empty) + ", " + (person.FirstName ?? string.Empty)).Trim(' ', ',')
            : person.FullName;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
