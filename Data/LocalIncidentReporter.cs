using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Services;

namespace Sati.Data;

public sealed class LocalIncidentReporter(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IIncidentReporter
{
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
            await using var context = contextFactory.CreateDbContext();
            var fingerprint = AppErrorLog.CreateFingerprint(exception);
            var safeOperation = AppErrorLog.SafeArea(operation);
            var release = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
            var occurredAt = DateTime.UtcNow;
            var incident = await context.IncidentGroups.SingleOrDefaultAsync(candidate =>
                candidate.AgencyId == actor.AgencyId &&
                candidate.Source == "Desktop" &&
                candidate.Operation == safeOperation &&
                candidate.ExceptionFingerprint == fingerprint,
                cancellationToken);
            if (incident is null)
            {
                context.IncidentGroups.Add(new IncidentGroup
                {
                    AgencyId = actor.AgencyId,
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
                incident.LastRelease = release;
                incident.LastSeenUtc = occurredAt;
                incident.LastReference = reference;
                incident.LastActorRole = actor.Role.ToString();
                incident.OccurrenceCount++;
                if (incident.Status == "Resolved")
                    incident.Status = "Reopened";
            }
            await context.SaveChangesAsync(cancellationToken);
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
