using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sati.Api.Data;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

/// <summary>
/// Reports objects the API's model expects but the database does not have.
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
/// objects. It is a detector, not a fix — it deliberately does not alter
/// anything. Applying a schema change to a hosted database stays a decision
/// somebody makes on purpose.
/// </para>
/// <para>
/// The comparison itself belongs to <see cref="SchemaComparison"/> in
/// <c>Sati.Contracts</c>, shared with the drift report and, later, the migrator's
/// verify step. This check gates readiness on
/// <see cref="SchemaDifference.PreventsQueries"/> only, which is the same set of
/// failures it reported before that rule was extracted: a database column no
/// model knows about breaks the next idempotent script, not the next request, and
/// must not take the API out of service.
/// </para>
/// <para>
/// The detail goes in the health-check description, which the default
/// <c>MapHealthChecks</c> response writer does not emit; the anonymous
/// <c>/health/ready</c> endpoint still returns only the status word. The full
/// three-way report is available to an authenticated Admin at
/// <c>GET /api/v1/admin/schema-drift</c>.
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
            var blocking = SchemaComparison
                .Compare(
                    SchemaSnapshotReader.FromModel(db.Model, "The API model", describesEveryTable: false),
                    await SchemaSnapshotReader.ReadDatabaseAsync(db, "the database", cancellationToken))
                .Where(difference => difference.PreventsQueries)
                .ToList();

            if (blocking.Count == 0)
                return HealthCheckResult.Healthy("Database schema matches the API model.");

            var detail = string.Join(
                ", ",
                blocking.Select(difference => difference.Kind == SchemaDifferenceKind.MissingTable
                    ? $"{difference.Table} (entire table)"
                    : difference.ObjectName));

            logger.LogError(
                "Database schema is behind the API model. Missing: {MissingColumns}. " +
                "Requests touching these will fail with a provider error until the " +
                "pending migrations are applied.", detail);
            return HealthCheckResult.Unhealthy(
                $"Database is missing {blocking.Count} object(s) the model expects: {detail}");
        }
        catch (Exception ex)
        {
            // A drift probe that cannot read metadata says nothing about drift.
            // Report it rather than letting a connection fault read as "healthy".
            logger.LogError(ex, "The schema drift check could not read database metadata.");
            return HealthCheckResult.Unhealthy("Could not read database metadata.", ex);
        }
    }
}
