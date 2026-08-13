using System.Globalization;
using System.Text.RegularExpressions;

namespace Sati.Contracts.V1;

/// <summary>
/// Payer-neutral arithmetic and format checks used by both the desktop and API billing paths.
/// Eligibility and agency-specific code selection remain explicit workflow concerns.
/// </summary>
public static partial class BillingRules
{
    public static decimal CalculateSection13Units(int? minutes)
    {
        if (minutes is null || minutes <= 0)
            return 0m;

        // MaineCare Section 13 grants a one-unit minimum for a substantive contact
        // lasting up to 15 minutes. Longer contacts retain partial 15-minute units.
        if (minutes <= 15)
            return 1m;

        return Math.Round(minutes.Value / 15m, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal CalculateCharge(decimal units, decimal unitRate) =>
        Math.Round(units * unitRate, 2, MidpointRounding.AwayFromZero);

    public static DateTime MaineBusinessDate(DateTimeOffset utcNow)
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        return TimeZoneInfo.ConvertTime(utcNow, eastern).Date;
    }

    public static bool IsValidNpi(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !TenDigits().IsMatch(value))
            return false;

        // The NPI check digit uses the Luhn algorithm with the health-care prefix 80840.
        var payload = "80840" + value[..9];
        var sum = 0;
        var doubleDigit = true;
        for (var index = payload.Length - 1; index >= 0; index--)
        {
            var digit = payload[index] - '0';
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }
            sum += digit;
            doubleDigit = !doubleDigit;
        }

        var expected = (10 - (sum % 10)) % 10;
        return value[9] - '0' == expected;
    }

    public static bool IsValidProcedureCode(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ProcedureCode().IsMatch(value);

    public static bool IsValidModifier(string? value) =>
        string.IsNullOrWhiteSpace(value) || Modifier().IsMatch(value);

    public static bool IsValidDiagnosisCode(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DiagnosisCode().IsMatch(value);

    public static string FormatDecimal(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    public static bool IsSafeX12Element(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            return false;
        return value.All(character => character is >= ' ' and <= '~' &&
            character is not ('*' or '~' or ':' or '^'));
    }

    [GeneratedRegex("^[0-9]{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex TenDigits();

    [GeneratedRegex("^[A-Z0-9]{4,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProcedureCode();

    [GeneratedRegex("^[A-Z0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex Modifier();

    [GeneratedRegex("^[A-TV-Z][0-9][0-9A-Z](?:\\.[0-9A-Z]{1,4})?$", RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosisCode();
}
