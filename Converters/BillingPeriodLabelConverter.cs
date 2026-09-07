using Sati.Models.Billing;
using System.Globalization;
using System.Windows.Data;

namespace Sati.Converters;

/// <summary>Turns a billing period into an unambiguous choice for billing staff.</summary>
public sealed class BillingPeriodLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not BillingPeriod period || period.Month is < 1 or > 12)
            return string.Empty;

        var month = new DateTime(period.Year, period.Month, 1).ToString("MMMM yyyy", culture);
        var manager = string.IsNullOrWhiteSpace(period.CaseManagerName)
            ? $"Case manager #{period.UserId}"
            : period.CaseManagerName;
        var claims = $"{period.Lines.Count} {(period.Lines.Count == 1 ? "claim" : "claims")}";
        var hasInvalidClaim = period.Lines.Any(line =>
            !line.IsReadyForSubmission || line.Units is null or <= 0 || line.ChargeAmount <= 0);
        var status = period.Status switch
        {
            BillingStatus.Draft when hasInvalidClaim => "Draft — needs claim correction",
            BillingStatus.Draft => "Draft — ready to submit",
            BillingStatus.Submitted when hasInvalidClaim => "Submitted and locked — claim cannot generate",
            BillingStatus.Submitted => "Submitted and locked",
            BillingStatus.Accepted => "Accepted",
            BillingStatus.Rejected => "Rejected",
            _ => period.Status.ToString()
        };

        return $"{month} — {manager} — {claims} — {status}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
