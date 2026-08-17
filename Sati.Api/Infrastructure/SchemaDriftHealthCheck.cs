using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sati.Api.Data;

namespace Sati.Api.Infrastructure;

/// <summary>
/// Reports columns the API's model expects but the database does not have.
/// </summary>
/// <remarks>
/// <para>
/// Nothing advances the hosted database. The desktop runs
/// <c>Database.Migrate()</c>, but only when it is connected straight to SQL —
/// in Demo it goes through this API over HTTP and never touches Azure SQL. This
/// API does not migrate, and the publish script has no database step. So a
/// release that adds a column ships code the database cannot satisfy, and the
/// gap surfaces as a 500 from whichever feature happens to touch the new column
/// first: a provider directory that will not load, an incident channel that
/// cannot record the very failure it exists to report.
/// </para>
/// <para>
/// This turns that into one legible readiness failure naming the missing
/// columns. It is a detector, not a fix — it deliberately does not alter
/// anything. Applying a schema change to a hosted database stays a decision
/// somebody makes on purpose.
/// </para>
/// <para>
/// The detail goes in the health-check description, which the default
/// <c>MapHealthChecks</c> response writer does not emit; the anonymous
/// <c>/health/ready</c> endpoint still returns only the status word.
/// </para>
/// </remarks>
internal sealed class SchemaDriftHealthCheck(
    IDbContextFactory<ApiDbContext> contextFactory,
    ILogger<SchemaDriftHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var actual = await ReadDatabaseColumnsAsync(db, cancellationToken);
            var missing = new List<string>();

            foreach (var entity in db.Model.GetEntityTypes())
            {
                var table = entity.GetTableName();
                if (string.IsNullOrEmpty(table))
                    continue;

                if (!actual.TryGetValue(table, out var columns))
                {
                    missing.Add($"{table} (entire table)");
                    continue;
                }

                foreach (var property in entity.GetProperties())
                {
                    var column = property.GetColumnName();
                    if (!string.IsNullOrEmpty(column) && !columns.Contains(column))
                        missing.Add($"{table}.{column}");
                }
            }

            if (missing.Count == 0)
                return HealthCheckResult.Healthy("Database schema matches the API model.");

            var detail = string.Join(", ", missing.OrderBy(name => name, StringComparer.Ordinal));
            logger.LogError(
                "Database schema is behind the API model. Missing: {MissingColumns}. " +
                "Requests touching these will fail with a provider error until the " +
                "pending migrations are applied.", detail);
            return HealthCheckResult.Unhealthy(
                $"Database is missing {missing.Count} object(s) the model expects: {detail}");
        }
        catch (Exception ex)
        {
            // A drift probe that cannot read metadata says nothing about drift.
            // Report it rather than letting a connection fault read as "healthy".
            logger.LogError(ex, "The schema drift check could not read database metadata.");
            return HealthCheckResult.Unhealthy("Could not read database metadata.", ex);
        }
    }

    /// <summary>
    /// Table and column names as the database actually has them. Provider-aware
    /// so the check is exercised by the SQLite-backed integration tests rather
    /// than only ever running in production.
    /// </summary>
    private static async Task<Dictionary<string, HashSet<string>>> ReadDatabaseColumnsAsync(
        ApiDbContext db,
        CancellationToken cancellationToken)
    {
        // Matched on the provider name rather than IsSqlite(), which lives in the
        // Sqlite package this project deliberately does not reference.
        var isSqlite = db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
        var sql = isSqlite
            ? """
              SELECT m.name AS TableName, p.name AS ColumnName
              FROM sqlite_master AS m
              JOIN pragma_table_info(m.name) AS p
              WHERE m.type = 'table'
              """
            : """
              SELECT TABLE_NAME AS TableName, COLUMN_NAME AS ColumnName
              FROM INFORMATION_SCHEMA.COLUMNS
              """;

        var columns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = reader.GetString(0);
                if (!columns.TryGetValue(table, out var names))
                {
                    names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    columns[table] = names;
                }
                names.Add(reader.GetString(1));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return columns;
    }
}
