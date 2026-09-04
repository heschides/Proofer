using System.Globalization;
using System.Windows.Data;

namespace Sati.Converters;

/// <summary>
/// Person.CreatedAtUtc is backfilled to DateTime.MinValue for any record that existed before
/// creation-date tracking shipped — showing that literal date ("Jan 1, 0001") reads as a bug.
/// This names it for what it is instead, since a record with this value is also permanently
/// outside the 20-day deletion window: HANDOFF_CLIENT_DELETION_POLICY.md's "no guessed date"
/// design intentionally placed it there rather than infer a real one.
/// </summary>
public sealed class CreatedAtLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime createdAtUtc
            ? createdAtUtc == default
                ? "Predates change tracking"
                : $"Created {createdAtUtc.ToLocalTime():MMM d, yyyy}"
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
