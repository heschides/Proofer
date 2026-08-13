using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;

namespace Sati.Api.Infrastructure;

internal sealed record IncidentAggregation(
    int AgencyId,
    string Scope,
    string Source,
    string Severity,
    string Operation,
    string Release,
    string Fingerprint,
    DateTime OccurredAtUtc,
    string Reference,
    string ActorRole);

internal sealed class IncidentAggregator(IDbContextFactory<ApiDbContext> contextFactory)
{
    // Bounded lock striping prevents an attacker from growing an unbounded dictionary.
    // The database transaction remains the cross-process authority; these gates merely
    // avoid needless unique-key races within one API process.
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public async Task<ServerIncidentGroup> UpsertAsync(
        IncidentAggregation report,
        CancellationToken cancellationToken = default)
    {
        var gate = Gates[(int)((uint)HashCode.Combine(
            report.AgencyId,
            report.Scope,
            report.Source,
            report.Operation,
            report.Fingerprint) % Gates.Length)];
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var query = db.Database.IsSqlServer()
                ? db.IncidentGroups.FromSqlInterpolated($"""
                    SELECT * FROM [IncidentGroups] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [AgencyId] = {report.AgencyId}
                      AND [Scope] = {report.Scope}
                      AND [Source] = {report.Source}
                      AND [Operation] = {report.Operation}
                      AND [ExceptionFingerprint] = {report.Fingerprint}
                    """)
                : db.IncidentGroups.Where(candidate =>
                    candidate.AgencyId == report.AgencyId &&
                    candidate.Scope == report.Scope &&
                    candidate.Source == report.Source &&
                    candidate.Operation == report.Operation &&
                    candidate.ExceptionFingerprint == report.Fingerprint);
            var incident = await query.SingleOrDefaultAsync(cancellationToken);
            if (incident is null)
            {
                incident = new ServerIncidentGroup
                {
                    AgencyId = report.AgencyId,
                    Scope = report.Scope,
                    Source = report.Source,
                    Severity = report.Severity,
                    Operation = report.Operation,
                    FirstRelease = report.Release,
                    LastRelease = report.Release,
                    ExceptionFingerprint = report.Fingerprint,
                    Status = "Open",
                    OccurrenceCount = 1,
                    FirstSeenUtc = report.OccurredAtUtc,
                    LastSeenUtc = report.OccurredAtUtc,
                    LastReference = report.Reference,
                    LastActorRole = report.ActorRole
                };
                db.IncidentGroups.Add(incident);
            }
            else
            {
                // Durable clients may retry an accepted envelope when the local
                // acknowledgement could not be removed. The support reference is
                // the idempotency key for that individual occurrence.
                if (incident.LastReference == report.Reference)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return incident;
                }

                incident.Severity = MoreSevere(incident.Severity, report.Severity);
                incident.FirstSeenUtc = report.OccurredAtUtc < incident.FirstSeenUtc
                    ? report.OccurredAtUtc
                    : incident.FirstSeenUtc;
                incident.LastRelease = report.Release;
                incident.LastSeenUtc = report.OccurredAtUtc > incident.LastSeenUtc
                    ? report.OccurredAtUtc
                    : incident.LastSeenUtc;
                incident.LastReference = report.Reference;
                incident.LastActorRole = report.ActorRole;
                incident.OccurrenceCount++;
                if (incident.Status == "Resolved")
                    incident.Status = "Reopened";
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return incident;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string MoreSevere(string current, string reported)
    {
        static int Rank(string value) => value switch
        {
            "Critical" => 3,
            "Error" => 2,
            _ => 1
        };
        return Rank(reported) > Rank(current) ? reported : current;
    }
}
