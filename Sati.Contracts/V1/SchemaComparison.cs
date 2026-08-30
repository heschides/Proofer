namespace Sati.Contracts.V1;

/// <summary>One column as some source describes it.</summary>
public sealed record SchemaColumn(string Name, bool IsNullable);

/// <summary>One table and the columns a source believes it has.</summary>
public sealed record SchemaTable(string Name, IReadOnlyList<SchemaColumn> Columns);

/// <summary>
/// What one source believes the database schema is.
/// </summary>
/// <param name="Source">
/// Human-readable origin — "ApiDbContext model", "SatiDemo", "SatiContext model".
/// It appears in the report, so a reader can tell which two things disagreed.
/// </param>
/// <param name="DescribesEveryTable">
/// Whether this source claims to describe the whole database. <c>SatiContext</c>
/// owns the migration chain and does. <c>ApiDbContext</c> is a second, partial
/// model over the same database and does not — it maps only the tables the API
/// serves. Comparing a partial source and reporting everything it omits as
/// "unexpected" would bury the real drift under every desktop-only table, so
/// <see cref="SchemaComparison.Compare"/> suppresses the unexpected-object
/// findings when the expected source is partial.
/// </param>
public sealed record SchemaSnapshot(
    string Source,
    IReadOnlyList<SchemaTable> Tables,
    bool DescribesEveryTable);

/// <summary>How two schema sources disagree about one object.</summary>
public enum SchemaDifferenceKind
{
    /// <summary>The expected source needs a table the database does not have.</summary>
    MissingTable,

    /// <summary>The expected source needs a column the database does not have.</summary>
    MissingColumn,

    /// <summary>The database has a table no model knows about.</summary>
    UnexpectedTable,

    /// <summary>The database has a column no model knows about.</summary>
    UnexpectedColumn,

    /// <summary>Both have the column, but disagree about whether it accepts null.</summary>
    NullabilityMismatch
}

/// <summary>One disagreement between two schema sources.</summary>
public sealed record SchemaDifference(
    SchemaDifferenceKind Kind,
    string Table,
    string? Column,
    string Detail)
{
    /// <summary>The object this is about, as <c>Table</c> or <c>Table.Column</c>.</summary>
    public string ObjectName => Column is null ? Table : $"{Table}.{Column}";

    /// <summary>
    /// Whether a query the expected source issues will fail outright because of this.
    ///
    /// Only the missing kinds qualify. A column the database has and the model does
    /// not is invisible to EF and breaks nothing at run time — it is the drift that
    /// breaks the next <c>--idempotent</c> script, not the next request. A
    /// nullability disagreement can break materialization, but it can also be a
    /// benign artifact of how a shared column was declared, so it is reported and
    /// deliberately not treated as blocking. Widening the readiness gate is a
    /// release-blocking decision that belongs in its own change, not a side effect
    /// of making the report more thorough.
    /// </summary>
    public bool PreventsQueries =>
        Kind is SchemaDifferenceKind.MissingTable or SchemaDifferenceKind.MissingColumn;
}

/// <summary>How the migration chain and the recorded history disagree.</summary>
public enum MigrationHistoryDifferenceKind
{
    /// <summary>
    /// The database records a migration this build does not contain. The database
    /// is ahead of the code, or the migration was renamed.
    /// </summary>
    AppliedButNotInChain,

    /// <summary>
    /// The chain contains a migration the database has never recorded. Either it
    /// genuinely has not run, or its objects were created by hand and the history
    /// row was never written — which is what makes a generated idempotent script
    /// fail with SQL 2705 on a column that already exists.
    /// </summary>
    InChainButNotApplied
}

/// <summary>One disagreement between the migration chain and <c>__EFMigrationsHistory</c>.</summary>
public sealed record MigrationHistoryDifference(
    MigrationHistoryDifferenceKind Kind,
    string MigrationId);

/// <summary>
/// The full three-way picture: what a model expects, what the database has, and
/// what the history table claims was done to it.
/// </summary>
/// <param name="AppliedMigrations">
/// Every id in <c>__EFMigrationsHistory</c>, reported as data rather than folded
/// into <paramref name="HistoryDifferences"/>. Only a caller that owns the chain
/// can say whether an applied id is expected, and the API does not: all 79
/// migrations belong to <c>SatiContext</c> in the desktop project, while
/// <c>ApiDbContext</c> is a second model over the same tables with no chain of
/// its own. So the API reports what the database claims and leaves the verdict to
/// the desktop, which can compare it against the real chain.
/// </param>
public sealed record SchemaDriftReport(
    string ExpectedSource,
    string ActualSource,
    IReadOnlyList<SchemaDifference> Differences,
    IReadOnlyList<MigrationHistoryDifference> HistoryDifferences,
    IReadOnlyList<string> AppliedMigrations)
{
    /// <summary>Differences that will make a query fail, in report order.</summary>
    public IReadOnlyList<SchemaDifference> Blocking =>
        Differences.Where(difference => difference.PreventsQueries).ToList();

    /// <summary>True when nothing disagrees in either direction.</summary>
    public bool IsClean => Differences.Count == 0 && HistoryDifferences.Count == 0;
}

/// <summary>
/// Sole owner of the rule for how two descriptions of a database schema differ.
///
/// This exists because the answer had one consumer and is about to have three:
/// the API's readiness check, the reconciliation that has to classify each
/// discrepancy before it can be fixed, and the migrator that must verify its own
/// work afterwards. A second hand-written comparison would let those three
/// disagree about whether a database is sound, which is the failure this is meant
/// to prevent.
///
/// It takes plain data rather than EF types on purpose. <c>Sati.Contracts</c>
/// carries no package references, and both the desktop and the API must be able
/// to ask this question. Extracting a snapshot from an EF model, or from
/// <c>INFORMATION_SCHEMA</c>, is provider-specific work that belongs to the
/// caller; deciding what the difference *means* belongs here.
///
/// Store-type comparison is deliberately absent. EF reports <c>nvarchar(max)</c>
/// where <c>INFORMATION_SCHEMA</c> reports <c>nvarchar</c> with length -1, and
/// <c>decimal(18,2)</c> as three separate columns. Normalizing those well enough
/// to avoid false positives is real work, and a drift report that cries wolf is
/// worse than one with a documented gap. Tracked in AGENDA.md rather than
/// half-implemented here.
/// </summary>
public static class SchemaComparison
{
    /// <summary>
    /// Compares what <paramref name="expected"/> believes against what
    /// <paramref name="actual"/> actually has, in both directions.
    /// </summary>
    public static IReadOnlyList<SchemaDifference> Compare(
        SchemaSnapshot expected,
        SchemaSnapshot actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var expectedTables = Index(expected);
        var actualTables = Index(actual);
        var differences = new List<SchemaDifference>();

        foreach (var (tableName, expectedColumns) in expectedTables)
        {
            if (!actualTables.TryGetValue(tableName, out var actualColumns))
            {
                differences.Add(new SchemaDifference(
                    SchemaDifferenceKind.MissingTable, tableName, null,
                    $"{expected.Source} expects table {tableName}; {actual.Source} does not have it."));
                continue;
            }

            foreach (var (columnName, expectedColumn) in expectedColumns)
            {
                if (!actualColumns.TryGetValue(columnName, out var actualColumn))
                {
                    differences.Add(new SchemaDifference(
                        SchemaDifferenceKind.MissingColumn, tableName, columnName,
                        $"{expected.Source} expects {tableName}.{columnName}; {actual.Source} does not have it."));
                    continue;
                }

                if (expectedColumn.IsNullable != actualColumn.IsNullable)
                {
                    differences.Add(new SchemaDifference(
                        SchemaDifferenceKind.NullabilityMismatch, tableName, columnName,
                        $"{expected.Source} says {tableName}.{columnName} is " +
                        $"{Nullability(expectedColumn.IsNullable)}; {actual.Source} says " +
                        $"{Nullability(actualColumn.IsNullable)}."));
                }
            }
        }

        // A partial source omits most of the database by design. Reporting each
        // omission as drift would drown the findings that matter.
        if (!expected.DescribesEveryTable)
            return Ordered(differences);

        foreach (var (tableName, actualColumns) in actualTables)
        {
            if (!expectedTables.TryGetValue(tableName, out var expectedColumns))
            {
                differences.Add(new SchemaDifference(
                    SchemaDifferenceKind.UnexpectedTable, tableName, null,
                    $"{actual.Source} has table {tableName}; {expected.Source} does not describe it."));
                continue;
            }

            foreach (var columnName in actualColumns.Keys)
            {
                if (!expectedColumns.ContainsKey(columnName))
                {
                    differences.Add(new SchemaDifference(
                        SchemaDifferenceKind.UnexpectedColumn, tableName, columnName,
                        $"{actual.Source} has {tableName}.{columnName}; " +
                        $"{expected.Source} does not describe it."));
                }
            }
        }

        return Ordered(differences);
    }

    /// <summary>
    /// Compares the migration ids this build contains against the ids the database
    /// records as applied.
    /// </summary>
    public static IReadOnlyList<MigrationHistoryDifference> CompareHistory(
        IEnumerable<string> chainMigrationIds,
        IEnumerable<string> appliedMigrationIds)
    {
        ArgumentNullException.ThrowIfNull(chainMigrationIds);
        ArgumentNullException.ThrowIfNull(appliedMigrationIds);

        var chain = new HashSet<string>(chainMigrationIds, StringComparer.Ordinal);
        var applied = new HashSet<string>(appliedMigrationIds, StringComparer.Ordinal);

        return applied.Except(chain, StringComparer.Ordinal)
            .Select(id => new MigrationHistoryDifference(
                MigrationHistoryDifferenceKind.AppliedButNotInChain, id))
            .Concat(chain.Except(applied, StringComparer.Ordinal)
                .Select(id => new MigrationHistoryDifference(
                    MigrationHistoryDifferenceKind.InChainButNotApplied, id)))
            .OrderBy(difference => difference.MigrationId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Builds the whole report in one call. Pass the migration ids this build
    /// contains as <paramref name="chainMigrationIds"/>; pass an empty sequence
    /// when the caller does not own the chain, and the history verdict is left
    /// empty rather than reporting every applied migration as unrecognized.
    /// </summary>
    public static SchemaDriftReport Report(
        SchemaSnapshot expected,
        SchemaSnapshot actual,
        IEnumerable<string> chainMigrationIds,
        IEnumerable<string> appliedMigrationIds)
    {
        var applied = appliedMigrationIds as IReadOnlyList<string> ?? appliedMigrationIds.ToList();
        var chain = chainMigrationIds as IReadOnlyList<string> ?? chainMigrationIds.ToList();
        return new SchemaDriftReport(
            expected.Source,
            actual.Source,
            Compare(expected, actual),
            chain.Count == 0 ? [] : CompareHistory(chain, applied),
            applied);
    }

    private static Dictionary<string, Dictionary<string, SchemaColumn>> Index(SchemaSnapshot snapshot)
    {
        var tables = new Dictionary<string, Dictionary<string, SchemaColumn>>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in snapshot.Tables)
        {
            // A source that lists the same table twice is a bug in the reader, not
            // a schema difference. Keep the first and let the columns merge, so the
            // report still renders instead of throwing on a duplicate key.
            if (!tables.TryGetValue(table.Name, out var columns))
            {
                columns = new Dictionary<string, SchemaColumn>(StringComparer.OrdinalIgnoreCase);
                tables[table.Name] = columns;
            }

            foreach (var column in table.Columns)
                columns[column.Name] = column;
        }

        return tables;
    }

    private static string Nullability(bool isNullable) => isNullable ? "nullable" : "not null";

    private static IReadOnlyList<SchemaDifference> Ordered(List<SchemaDifference> differences) =>
        differences
            .OrderBy(difference => difference.Kind)
            .ThenBy(difference => difference.ObjectName, StringComparer.Ordinal)
            .ToList();
}
