using System.Globalization;
using System.Windows.Data;

namespace Sati.Converters;

public sealed class PersonBillingComplianceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Person person && person.EvaluateComplianceGate(DateTime.Today).Passed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
