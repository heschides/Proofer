using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Sati.Data;

/// <summary>How a pending migration relates to the schema already in the database.</summary>
public enum MigrationEffectState
{
    /// <summary>None of its effects are present. The ordinary case: it simply has not run.</summary>
    NotApplied,

    /// <summary>
    /// Every effect this migration declares is already present, but the database holds
    /// no record that it ran. Applying it will fail — this is the SQL 2705 case.
    /// </summary>
    AlreadyPresent,

    /// <summary>
    /// Some effects are present and some are not. The most dangerous state, because
    /// neither applying nor recording the migration is correct, and which half is
    /// missing decides what should happen. Always needs a person.
    /// </summary>
    PartiallyPresent,

    /// <summary>
    /// The migration contains steps this analyzer cannot inspect and no structural
    /// step settled the question. Nothing is claimed.
    /// </summary>
    Indeterminate
}

/// <param name="MigrationId">The pending migration.</param>
/// <param name="State">What the schema says about it.</param>
/// <param name="PresentEffects">Declared effects found in the database.</param>
/// <param name="MissingEffects">Declared effects not found.</param>
/// <param name="UnverifiableSteps">
/// Steps that cannot be checked by inspection — raw SQL and data changes. A migration
/// whose structural effects are all present may still have an unrun backfill, so these
/// are reported rather than assumed either way.
/// </param>
public sealed record MigrationEffectFinding(
    string MigrationId,
    MigrationEffectState State,
    IReadOnlyList<string> PresentEffects,
    IReadOnlyList<string> MissingEffects,
    IReadOnlyList<string> UnverifiableSteps);

/// <summary>
/// Answers, for each pending migration, whether the database already contains what it
/// declares.
/// </summary>
/// <remarks>
/// <para>
/// Sati 1.2.32 refused to start against SatiProduction with SQL 2705, "Column name
/// 'AgencyId' in table 'Settings' is specified more than once". EF was applying a
/// migration whose columns already existed. The database had the effects and no record
/// of them, and nothing in the startup path could tell that apart from an ordinary
/// pending migration until it failed halfway through trying.
/// </para>
/// <para>
/// This closes that gap by comparing what a migration declares against what the schema
/// has, before anything is applied. It reports; it does not repair. Deciding which side
/// of a disagreement is right needs judgement, and <see cref="LocalDatabaseUpdater"/>
/// deliberately does not exercise judgement over a database full of consumer records.
/// What it can now do is stop with a diagnosis rather than a provider error.
/// </para>
/// <para>
/// Conservative by construction. An operation type it does not recognise is counted as
/// unverifiable rather than assumed satisfied, so an unfamiliar migration reports
/// <see cref="MigrationEffectState.Indeterminate"/> instead of a confident wrong answer.
/// </para>
/// </remarks>
public static class MigrationEffectAnalyzer
{
    public static async Task<IReadOnlyList<MigrationEffectFinding>> AnalyzeAsync(
        DbContext context,
        IEnumerable<string> pendingMigrationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pendingMigrationIds);

        var pending = pendingMigrationIds as IReadOnlyList<string> ?? [.. pendingMigrationIds];
        if (pending.Count == 0)
            return [];

        var schema = await LiveSchema.ReadAsync(context, cancellationToken);
        var assembly = context.GetService<IMigrationsAssembly>();
        var activeProvider = context.Database.ProviderName
            ?? throw new InvalidOperationException("The context has no database provider.");

        var findings = new List<MigrationEffectFinding>(pending.Count);
        foreach (var migrationId in pending)
        {
            if (!assembly.Migrations.TryGetValue(migrationId, out var typeInfo))
            {
                findings.Add(new MigrationEffectFinding(
                    migrationId, MigrationEffectState.Indeterminate, [], [],
                    ["The migration is pending but not present in this build."]));
                continue;
            }

            var migration = assembly.CreateMigration(typeInfo, activeProvider);
            findings.Add(Classify(migrationId, migration.UpOperations, schema));
        }

        return findings;
    }

    internal static MigrationEffectFinding Classify(
        string migrationId,
        IReadOnlyList<MigrationOperation> operations,
        LiveSchema schema)
    {
        List<string> present = [];
        List<string> missing = [];
        List<string> unverifiable = [];

        foreach (var operation in operations)
        {
            switch (operation)
            {
                case CreateTableOperation create:
                    Record($"table {create.Name}", schema.HasTable(create.Name));
                    break;

                case DropTableOperation drop:
                    Record($"table {drop.Name} removed", !schema.HasTable(drop.Name));
                    break;

                case AddColumnOperation add:
                    Record($"{add.Table}.{add.Name}", schema.HasColumn(add.Table, add.Name));
                    break;

                case DropColumnOperation drop:
                    Record($"{drop.Table}.{drop.Name} removed", !schema.HasColumn(drop.Table, drop.Name));
                    break;

                // An alter is satisfied only when the column exists, its nullability
                // matches the target, AND a declared bound has actually been applied.
                // The columns this chain alters are made NOT NULL after a backfill, so
                // nullability is the signal that the backfill ran.
                //
                // Nullability alone is not enough. AddUniqueFormPersonTypeDueDateIndex
                // narrows Forms.Type from nvarchar(max) to nvarchar(40) so it can be
                // indexed, and that column is NOT NULL both before and after. Judged on
                // nullability the alter read as already applied while the index beside
                // it read as missing, so a completely un-migrated database reported
                // PartiallyPresent and startup refused - correctly, given what the
                // analyzer could see, but on a false premise. An unbounded column where
                // the migration declares a bound is proof the alter has NOT run.
                //
                // Only unbounded-versus-bounded is treated as evidence. A live column
                // merely wider than the migration declared is left satisfied, because
                // that is benign drift and narrowing the verdict there would stop
                // startup over something that does not affect correctness.
                case AlterColumnOperation alter:
                    var boundApplied =
                        alter.MaxLength is not int declaredLength
                        || schema.ColumnMaxLength(alter.Table, alter.Name) is not int liveLength
                        || liveLength >= declaredLength;
                    Record(
                        $"{alter.Table}.{alter.Name} is {(alter.IsNullable ? "nullable" : "not null")}"
                        + (alter.MaxLength is int bound ? $" and bounded to {bound}" : string.Empty),
                        schema.HasColumn(alter.Table, alter.Name)
                        && schema.ColumnIsNullable(alter.Table, alter.Name) == alter.IsNullable
                        && boundApplied);
                    break;

                case CreateIndexOperation index:
                    Record(
                        $"index on {index.Table} ({string.Join(", ", index.Columns)})",
                        schema.HasIndex(index.Table, index.Columns));
                    break;

                case DropIndexOperation drop:
                    unverifiable.Add($"index removal on {drop.Table} cannot be checked by column");
                    break;

                case AddForeignKeyOperation foreignKey:
                    Record(
                        $"{foreignKey.Table} ({string.Join(", ", foreignKey.Columns)}) references {foreignKey.PrincipalTable}",
                        schema.HasForeignKey(foreignKey.Table, foreignKey.Columns, foreignKey.PrincipalTable));
                    break;

                case AddPrimaryKeyOperation primaryKey:
                    Record(
                        $"primary key on {primaryKey.Table} ({string.Join(", ", primaryKey.Columns)})",
                        schema.HasPrimaryKey(primaryKey.Table, primaryKey.Columns));
                    break;

                // Raw SQL is usually a data backfill beside a structural change. It cannot
                // be inspected, so it is reported and left out of the verdict rather than
                // guessed at in either direction.
                case SqlOperation:
                    unverifiable.Add("a raw SQL step");
                    break;

                case InsertDataOperation or UpdateDataOperation or DeleteDataOperation:
                    unverifiable.Add("a data change");
                    break;

                default:
                    unverifiable.Add($"an unrecognised step ({operation.GetType().Name})");
                    break;
            }
        }

        var state = (present.Count, missing.Count) switch
        {
            (0, 0) => MigrationEffectState.Indeterminate,
            (> 0, 0) => MigrationEffectState.AlreadyPresent,
            (0, > 0) => MigrationEffectState.NotApplied,
            _ => MigrationEffectState.PartiallyPresent
        };

        return new MigrationEffectFinding(migrationId, state, present, missing, unverifiable);

        void Record(string description, bool satisfied)
        {
            if (satisfied) present.Add(description);
            else missing.Add(description);
        }
    }

    /// <summary>
    /// The tables, columns, indexes, primary keys, and foreign keys the database actually
    /// has, read once so a migration set is classified against one consistent picture.
    /// </summary>
    internal sealed class LiveSchema
    {
        // Test seam. The reader below needs SQL Server catalog views, so the
        // classification rules would otherwise have no automated coverage at all.
        internal static LiveSchema ForTests(
            Dictionary<string, Dictionary<string, (bool Nullable, int? MaxChars)>> columns,
            IEnumerable<(string Table, string[] Columns)>? indexes = null)
        {
            var schema = new LiveSchema();
            foreach (var table in columns)
                schema._columns[table.Key] = table.Value;
            foreach (var index in indexes ?? [])
                schema._indexes.Add(Key(index.Table, index.Columns));
            return schema;
        }

        // Value is (IsNullable, MaxCharacters). MaxCharacters is null for a column with
        // no character length - a non-string type - and -1 for an unbounded one, which
        // is what INFORMATION_SCHEMA reports for nvarchar(max).
        private readonly Dictionary<string, Dictionary<string, (bool Nullable, int? MaxChars)>> _columns =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _indexes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _primaryKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _foreignKeys = new(StringComparer.OrdinalIgnoreCase);

        public static async Task<LiveSchema> ReadAsync(DbContext context, CancellationToken cancellationToken)
        {
            var schema = new LiveSchema();
            var connection = context.Database.GetDbConnection();
            await context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await ReadAsync(connection,
                    """
                    SELECT TABLE_NAME, COLUMN_NAME, CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END,
                           CHARACTER_MAXIMUM_LENGTH
                    FROM INFORMATION_SCHEMA.COLUMNS
                    """,
                    reader =>
                    {
                        var table = reader.GetString(0);
                        if (!schema._columns.TryGetValue(table, out var columns))
                        {
                            columns = new Dictionary<string, (bool, int?)>(StringComparer.OrdinalIgnoreCase);
                            schema._columns[table] = columns;
                        }
                        columns[reader.GetString(1)] = (
                            Convert.ToInt32(reader.GetValue(2)) == 1,
                            reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetValue(3)));
                    }, cancellationToken);

                // Index and key membership is keyed by table plus its ordered key columns,
                // so a renamed constraint still matches. Names are not identity here.
                await ReadAsync(connection,
                    """
                    SELECT t.name, i.name, i.is_primary_key,
                           STUFF((SELECT ',' + c.name
                                  FROM sys.index_columns ic
                                  JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
                                  ORDER BY ic.key_ordinal
                                  FOR XML PATH('')), 1, 1, '')
                    FROM sys.indexes i
                    JOIN sys.tables t ON t.object_id = i.object_id
                    WHERE i.type IN (1, 2) AND i.is_disabled = 0
                    """,
                    reader =>
                    {
                        var key = Key(reader.GetString(0), (reader.IsDBNull(3) ? "" : reader.GetString(3)).Split(','));
                        schema._indexes.Add(key);
                        if (!reader.IsDBNull(2) && reader.GetBoolean(2))
                            schema._primaryKeys.Add(key);
                    }, cancellationToken);

                await ReadAsync(connection,
                    """
                    SELECT pt.name, rt.name,
                           STUFF((SELECT ',' + pc.name
                                  FROM sys.foreign_key_columns fc
                                  JOIN sys.columns pc ON pc.object_id = fc.parent_object_id AND pc.column_id = fc.parent_column_id
                                  WHERE fc.constraint_object_id = fk.object_id
                                  ORDER BY fc.constraint_column_id
                                  FOR XML PATH('')), 1, 1, '')
                    FROM sys.foreign_keys fk
                    JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
                    JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
                    """,
                    reader => schema._foreignKeys.Add(
                        Key(reader.GetString(0), (reader.IsDBNull(2) ? "" : reader.GetString(2)).Split(','))
                        + "->" + reader.GetString(1)),
                    cancellationToken);
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }

            return schema;
        }

        public bool HasTable(string table) => _columns.ContainsKey(table);

        public bool HasColumn(string table, string column) =>
            _columns.TryGetValue(table, out var columns) && columns.ContainsKey(column);

        public bool ColumnIsNullable(string table, string column) =>
            _columns.TryGetValue(table, out var columns)
            && columns.TryGetValue(column, out var info)
            && info.Nullable;

        /// <summary>
        /// The column's character length, or null when it has none. An unbounded
        /// column - nvarchar(max) - reports -1, which is deliberately NOT normalised
        /// to null: "unbounded" is a real answer that disproves a declared bound,
        /// while "not a string column" is an absence of evidence.
        /// </summary>
        public int? ColumnMaxLength(string table, string column) =>
            _columns.TryGetValue(table, out var columns)
            && columns.TryGetValue(column, out var info)
                ? info.MaxChars
                : null;

        public bool HasIndex(string table, string[] columns) => _indexes.Contains(Key(table, columns));

        public bool HasPrimaryKey(string table, string[] columns) => _primaryKeys.Contains(Key(table, columns));

        public bool HasForeignKey(string table, string[] columns, string principalTable) =>
            _foreignKeys.Contains(Key(table, columns) + "->" + principalTable);

        private static string Key(string table, IEnumerable<string> columns) =>
            table + "(" + string.Join(",", columns.Select(column => column.Trim())) + ")";

        private static async Task ReadAsync(
            System.Data.Common.DbConnection connection,
            string sql,
            Action<System.Data.Common.DbDataReader> read,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                read(reader);
        }
    }
}
