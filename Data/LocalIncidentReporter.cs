using System.Reflection;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Services;

namespace Sati.Data;

public sealed class LocalIncidentReporter(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IIncidentReporter
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 32)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public async Task ReportAsync(
        Exception exception,
        string operation,
        string reference,
        string severity = IncidentSeverities.Error,
        CancellationToken cancellationToken = default)
    {
        var actor = sessionService.CurrentUser;
        if (actor is null)
            return;

        try
        {
            var fingerprint = AppErrorLog.CreateFingerprint(exception);
            var safeOperation = AppErrorLog.SafeArea(operation);
            var release = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
            var occurredAt = DateTime.UtcNow;
            var gate = Gates[(int)((uint)HashCode.Combine(
                actor.AgencyId,
                actor.Role == UserRole.PlatformOperator ? IncidentScopes.Platform : IncidentScopes.Agency,
                safeOperation,
                fingerprint) % Gates.Length)];
            await gate.WaitAsync(cancellationToken);
            try
            {
                await using var context = contextFactory.CreateDbContext();
                await using var transaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                var incident = await context.IncidentGroups.SingleOrDefaultAsync(candidate =>
                    candidate.AgencyId == actor.AgencyId &&
                    candidate.Scope == (actor.Role == UserRole.PlatformOperator ? IncidentScopes.Platform : IncidentScopes.Agency) &&
                    candidate.Source == "Desktop" &&
                    candidate.Operation == safeOperation &&
                    candidate.ExceptionFingerprint == fingerprint,
                    cancellationToken);
                if (incident is null)
                {
                    context.IncidentGroups.Add(new IncidentGroup
                    {
                        AgencyId = actor.AgencyId,
                        Scope = actor.Role == UserRole.PlatformOperator ? IncidentScopes.Platform : IncidentScopes.Agency,
                        Source = "Desktop",
                        Severity = severity,
                        Operation = safeOperation,
                        FirstRelease = release,
                        LastRelease = release,
                        ExceptionFingerprint = fingerprint,
                        FirstSeenUtc = occurredAt,
                        LastSeenUtc = occurredAt,
                        LastReference = reference,
                        LastActorRole = actor.Role.ToString()
                    });
                }
                else
                {
                    incident.Severity = MoreSevere(incident.Severity, severity);
                    incident.FirstSeenUtc = occurredAt < incident.FirstSeenUtc ? occurredAt : incident.FirstSeenUtc;
                    incident.LastRelease = release;
                    incident.LastSeenUtc = occurredAt > incident.LastSeenUtc ? occurredAt : incident.LastSeenUtc;
                    incident.LastReference = reference;
                    incident.LastActorRole = actor.Role.ToString();
                    incident.OccurrenceCount++;
                    if (incident.Status == "Resolved")
                        incident.Status = "Reopened";
                }
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
        catch
        {
            // Error reporting must never replace or amplify the original failure.
        }
    }

    private static string MoreSevere(string current, string reported)
    {
        static int Rank(string value) => value switch
        {
            IncidentSeverities.Critical => 3,
            IncidentSeverities.Error => 2,
            _ => 1
        };
        return Rank(reported) > Rank(current) ? reported : current;
    }
}
