using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

/// <summary>
/// Turns an EF model and a live database into the plain
/// <see cref="SchemaSnapshot"/> values <see cref="SchemaComparison"/> compares.
/// </summary>
/// <remarks>
/// <para>
/// The comparison rule lives in <c>Sati.Contracts</c> so the desktop and the API
/// cannot answer differently. Reading a snapshot is provider-specific plumbing and
/// stays here, because <c>Sati.Contracts</c> carries no package references and
/// must not acquire EF Core.
/// </para>
/// <para>
/// Provider handling is matched on the provider name rather than
/// <c>IsSqlite()</c>, which lives in the Sqlite package this project deliberately
/// does not reference. The SQLite path exists so the integration tests exercise
/// this code rather than leaving it to run for the first time against Azure SQL.
/// </para>
/// </remarks>
internal static class SchemaSnapshotReader
{
    /// <summary>
    /// The tables and columns an EF model expects, as the model itself declares
    /// them. Entities sharing a table (owned types, TPH hierarchies) contribute
    /// their columns to one entry rather than producing duplicate tables.
    /// </summary>
    public static SchemaSnapshot FromModel(IModel model, string source, bool describesEveryTable)
    {
        ArgumentNullException.ThrowIfNull(model);

        var tables = new Dictionary<string, Dictionary<string, SchemaColumn>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (string.IsNullOrEmpty(tableName))
                continue;

            if (!tables.TryGetValue(tableName, out var columns))
            {
                columns = new Dictionary<string, SchemaColumn>(StringComparer.OrdinalIgnoreCase);
                tables[tableName] = columns;
            }

            foreach (var property in entity.GetProperties())
            {
                var columnName = property.GetColumnName();
                if (string.IsNullOrEmpty(columnName))
                    continue;

                // A column shared by two entities in a hierarchy is nullable if
                // either side allows null; take the permissive reading so a shared
                // column is not reported as a mismatch against its own database.
                var isNullable = property.IsNullable;
                if (columns.TryGetValue(columnName, out var existing))
                    isNullable = existing.IsNullable || isNullable;

                columns[columnName] = new SchemaColumn(columnName, isNullable);
            }
        }

        return new SchemaSnapshot(
            source,
            tables.Select(entry => new SchemaTable(entry.Key, entry.Value.Values.ToList())).ToList(),
            describesEveryTable);
    }

    /// <summary>The tables and columns the database actually has.</summary>
    public static async Task<SchemaSnapshot> ReadDatabaseAsync(
        DbContext db,
        string source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var sql = IsSqlite(db)
            ? """
              SELECT m.name AS TableName, p.name AS ColumnName, p."notnull" AS ColumnNotNull
              FROM sqlite_master AS m
              JOIN pragma_table_info(m.name) AS p
              WHERE m.type = 'table'
              """
            : """
              SELECT TABLE_NAME AS TableName,
                     COLUMN_NAME AS ColumnName,
                     CASE WHEN IS_NULLABLE = 'NO' THEN 1 ELSE 0 END AS ColumnNotNull
              FROM INFORMATION_SCHEMA.COLUMNS
              """;

        var tables = new Dictionary<string, List<SchemaColumn>>(StringComparer.OrdinalIgnoreCase);

        await ExecuteReaderAsync(db, sql, async reader =>
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var tableName = reader.GetString(0);
                var columnName = reader.GetString(1);
                var notNull = Convert.ToInt64(reader.GetValue(2)) != 0;

                if (!tables.TryGetValue(tableName, out var columns))
                {
                    columns = [];
                    tables[tableName] = columns;
                }

                columns.Add(new SchemaColumn(columnName, !notNull));
            }
        }, cancellationToken);

        // The history table is bookkeeping, not part of any model. Leaving it in
        // would report it as an unexpected table on every authoritative comparison.
        tables.Remove("__EFMigrationsHistory");

        return new SchemaSnapshot(
            source,
            tables.Select(entry => new SchemaTable(entry.Key, entry.Value)).ToList(),
            DescribesEveryTable: true);
    }

    /// <summary>
    /// The migration ids the database records as applied, or an empty list when
    /// the history table does not exist — which is the normal state for the
    /// SQLite-backed tests and for a database created outside the chain.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ReadAppliedMigrationsAsync(
        DbContext db,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var exists = IsSqlite(db)
            ? "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'"
            : "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '__EFMigrationsHistory'";

        var present = false;
        await ExecuteReaderAsync(db, exists, async reader =>
        {
            if (await reader.ReadAsync(cancellationToken))
                present = Convert.ToInt64(reader.GetValue(0)) > 0;
        }, cancellationToken);

        if (!present)
            return [];

        var applied = new List<string>();
        await ExecuteReaderAsync(db, "SELECT MigrationId FROM __EFMigrationsHistory", async reader =>
        {
            while (await reader.ReadAsync(cancellationToken))
                applied.Add(reader.GetString(0));
        }, cancellationToken);

        applied.Sort(StringComparer.Ordinal);
        return applied;
    }

    private static bool IsSqlite(DbContext db) =>
        db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task ExecuteReaderAsync(
        DbContext db,
        string sql,
        Func<DbDataReader, Task> read,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await read(reader);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
