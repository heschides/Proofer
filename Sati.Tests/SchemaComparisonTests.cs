using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Exercises the rule that decides how two descriptions of a database schema
/// differ. The reconciliation that has to fix the drift, the API's readiness
/// gate, and the migrator's verify step all read this one answer, so the cases
/// that matter are the asymmetric ones: which direction a difference points, and
/// which findings a partial model is allowed to make.
/// </summary>
public sealed class SchemaComparisonTests
{
    private static SchemaSnapshot Model(bool describesEveryTable, params SchemaTable[] tables) =>
        new("The model", tables, describesEveryTable);

    private static SchemaSnapshot Database(params SchemaTable[] tables) =>
        new("the database", tables, DescribesEveryTable: true);

    private static SchemaTable Table(string name, params string[] columns) =>
        new(name, columns.Select(column => new SchemaColumn(column, IsNullable: true)).ToList());

    [Fact]
    public void MatchingSchemasReportNothing()
    {
        var differences = SchemaComparison.Compare(
            Model(true, Table("People", "Id", "DisplayName")),
            Database(Table("People", "Id", "DisplayName")));

        Assert.Empty(differences);
    }

    [Fact]
    public void AColumnTheModelNeedsAndTheDatabaseLacksBlocksQueries()
    {
        var differences = SchemaComparison.Compare(
            Model(true, Table("People", "Id", "MedicalKind")),
            Database(Table("People", "Id")));

        var difference = Assert.Single(differences);
        Assert.Equal(SchemaDifferenceKind.MissingColumn, difference.Kind);
        Assert.Equal("People.MedicalKind", difference.ObjectName);
        Assert.True(difference.PreventsQueries);
    }

    [Fact]
    public void AWholeMissingTableIsReportedOnceRatherThanPerColumn()
    {
        var differences = SchemaComparison.Compare(
            Model(true, Table("RemittanceDeposits", "Id", "PayerName", "EftDepositAmount")),
            Database());

        var difference = Assert.Single(differences);
        Assert.Equal(SchemaDifferenceKind.MissingTable, difference.Kind);
        Assert.Equal("RemittanceDeposits", difference.ObjectName);
        Assert.True(difference.PreventsQueries);
    }

    /// <summary>
    /// The drift that breaks releases. A column the database acquired outside the
    /// migration chain is invisible to EF at run time and is exactly what makes a
    /// generated idempotent script fail with SQL 2705, so it has to be reported
    /// without being treated as a reason to take the API out of service.
    /// </summary>
    [Fact]
    public void AColumnTheDatabaseHasAndNoModelKnowsIsReportedButDoesNotBlockQueries()
    {
        var differences = SchemaComparison.Compare(
            Model(true, Table("People", "Id")),
            Database(Table("People", "Id", "AcquiredOutsideTheChain")));

        var difference = Assert.Single(differences);
        Assert.Equal(SchemaDifferenceKind.UnexpectedColumn, difference.Kind);
        Assert.Equal("People.AcquiredOutsideTheChain", difference.ObjectName);
        Assert.False(difference.PreventsQueries);
    }

    /// <summary>
    /// ApiDbContext maps only the tables the API serves. Reporting every table it
    /// omits as drift would bury the real findings under the desktop-only schema,
    /// so a partial source may report what it needs and nothing about what it does
    /// not describe.
    /// </summary>
    [Fact]
    public void APartialModelReportsWhatItNeedsAndStaysSilentAboutTheRest()
    {
        var expected = Model(false, Table("People", "Id", "MedicalKind"));
        var actual = Database(
            Table("People", "Id"),
            Table("DesktopOnlyScratchpads", "Id", "Body"));

        var differences = SchemaComparison.Compare(expected, actual);

        var difference = Assert.Single(differences);
        Assert.Equal(SchemaDifferenceKind.MissingColumn, difference.Kind);
        Assert.Equal("People.MedicalKind", difference.ObjectName);
    }

    [Fact]
    public void AnAuthoritativeModelDoesReportATableItDoesNotDescribe()
    {
        var differences = SchemaComparison.Compare(
            Model(true, Table("People", "Id")),
            Database(Table("People", "Id"), Table("OrphanedTable", "Id")));

        var difference = Assert.Single(differences);
        Assert.Equal(SchemaDifferenceKind.UnexpectedTable, difference.Kind);
        Assert.Equal("OrphanedTable", difference.ObjectName);
    }

    [Fact]
    public void ANullabilityDisagreementIsReportedWithBothReadings()
    {
        var expected = new SchemaSnapshot(
            "The model",
            [new SchemaTable("People", [new SchemaColumn("MaineCareId", IsNullable: false)])],
            DescribesEveryTable: true);
        var actual = new SchemaSnapshot(
            "the database",
            [new SchemaTable("People", [new SchemaColumn("MaineCareId", IsNullable: true)])],
            DescribesEveryTable: true);

        var difference = Assert.Single(SchemaComparison.Compare(expected, actual));

        Assert.Equal(SchemaDifferenceKind.NullabilityMismatch, difference.Kind);
        Assert.Contains("not null", difference.Detail);
        Assert.Contains("nullable", difference.Detail);
        Assert.False(difference.PreventsQueries);
    }

    [Fact]
    public void TableAndColumnNamesAreMatchedCaseInsensitively()
    {
        var differences = SchemaComparison.Compare(
            Model(true, Table("people", "id")),
            Database(Table("People", "Id")));

        Assert.Empty(differences);
    }

    [Fact]
    public void AMigrationAppliedButAbsentFromTheChainIsReported()
    {
        var differences = SchemaComparison.CompareHistory(
            chainMigrationIds: ["20260829231646_AddBillingExchangeHistory"],
            appliedMigrationIds: ["20260829231646_AddBillingExchangeHistory", "20260101000000_FromAnotherBuild"]);

        var difference = Assert.Single(differences);
        Assert.Equal(MigrationHistoryDifferenceKind.AppliedButNotInChain, difference.Kind);
        Assert.Equal("20260101000000_FromAnotherBuild", difference.MigrationId);
    }

    [Fact]
    public void AMigrationInTheChainWithNoHistoryRowIsReported()
    {
        var differences = SchemaComparison.CompareHistory(
            chainMigrationIds: ["20260830001538_AddRemittanceDeposits"],
            appliedMigrationIds: []);

        var difference = Assert.Single(differences);
        Assert.Equal(MigrationHistoryDifferenceKind.InChainButNotApplied, difference.Kind);
        Assert.Equal("20260830001538_AddRemittanceDeposits", difference.MigrationId);
    }

    /// <summary>
    /// The API owns no migration chain — all of them belong to SatiContext in the
    /// desktop project. Passing an empty chain must leave the history verdict empty
    /// rather than declaring every applied migration unrecognized.
    /// </summary>
    [Fact]
    public void ACallerWithNoChainGetsAppliedIdsAsDataAndNoHistoryVerdict()
    {
        var report = SchemaComparison.Report(
            Model(false, Table("People", "Id")),
            Database(Table("People", "Id")),
            chainMigrationIds: [],
            appliedMigrationIds: ["20260829231646_AddBillingExchangeHistory"]);

        Assert.Empty(report.HistoryDifferences);
        Assert.Equal(["20260829231646_AddBillingExchangeHistory"], report.AppliedMigrations);
        Assert.True(report.IsClean);
    }

    [Fact]
    public void ReportSeparatesBlockingDifferencesFromTheRest()
    {
        var report = SchemaComparison.Report(
            Model(true, Table("People", "Id", "MedicalKind")),
            Database(Table("People", "Id", "AcquiredOutsideTheChain")),
            chainMigrationIds: [],
            appliedMigrationIds: []);

        Assert.False(report.IsClean);
        Assert.Equal(2, report.Differences.Count);
        var blocking = Assert.Single(report.Blocking);
        Assert.Equal("People.MedicalKind", blocking.ObjectName);
    }
}
