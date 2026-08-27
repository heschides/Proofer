using System.Globalization;
using System.Windows.Data;

namespace Sati.Converters;

public sealed class PersonBillingComplianceConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var requirements = values.Length > 1 && values[1] is Contracts.V1.BillingComplianceRequirements configured
            ? configured
            : Contracts.V1.BillingComplianceGate.DefaultRequirements;
        return values.Length > 0 && values[0] is Person person &&
               person.EvaluateComplianceGate(DateTime.Today, requirements: requirements).Passed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
