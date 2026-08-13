using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal sealed class ApiIncidentRecorder(IDbContextFactory<ApiDbContext> contextFactory)
{
    public async Task RecordAsync(
        Exception exception,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !int.TryParse(context.User.FindFirst("agency_id")?.Value, out var agencyId))
            return;

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var operation = SafeOperation(context.Request.Method, context.GetEndpoint()?.DisplayName);
            var fingerprint = Fingerprint(exception);
            var release = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            var actorRole = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "Unknown";
            var now = DateTime.UtcNow;
            var incident = await db.IncidentGroups.SingleOrDefaultAsync(candidate =>
                candidate.AgencyId == agencyId &&
                candidate.Source == "Api" &&
                candidate.Operation == operation &&
                candidate.ExceptionFingerprint == fingerprint,
                cancellationToken);
            if (incident is null)
            {
                db.IncidentGroups.Add(new ServerIncidentGroup
                {
                    AgencyId = agencyId,
                    Source = "Api",
                    Severity = IncidentSeverities.Error,
                    Operation = operation,
                    FirstRelease = release,
                    LastRelease = release,
                    ExceptionFingerprint = fingerprint,
                    FirstSeenUtc = now,
                    LastSeenUtc = now,
                    LastReference = context.TraceIdentifier,
                    LastActorRole = actorRole
                });
            }
            else
            {
                incident.LastRelease = release;
                incident.LastSeenUtc = now;
                incident.LastReference = context.TraceIdentifier;
                incident.LastActorRole = actorRole;
                incident.OccurrenceCount++;
                if (incident.Status == "Resolved")
                    incident.Status = "Reopened";
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The original API failure remains authoritative if incident persistence fails.
        }
    }

    private static string Fingerprint(Exception exception)
    {
        var shape = string.Join('|',
            exception.GetType().FullName,
            exception.HResult.ToString("X8"),
            exception.TargetSite?.DeclaringType?.FullName,
            exception.TargetSite?.Name,
            exception.InnerException?.GetType().FullName,
            exception.InnerException?.HResult.ToString("X8"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shape)));
    }

    private static string SafeOperation(string method, string? endpointName)
    {
        var value = $"{method}.{endpointName ?? "unmatched"}";
        var safe = new string(value
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            .Take(80)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "api.unknown" : safe;
    }
}
