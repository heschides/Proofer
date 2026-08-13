using System.Security.Cryptography;
using System.Text;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal sealed class ApiIncidentRecorder(IncidentAggregator aggregator)
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
            var operation = SafeOperation(context.Request.Method, context.GetEndpoint()?.DisplayName);
            var fingerprint = Fingerprint(exception);
            var release = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            var actorRole = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "Unknown";
            var now = DateTime.UtcNow;
            await aggregator.UpsertAsync(new IncidentAggregation(
                agencyId,
                actorRole == "PlatformOperator" ? IncidentScopes.Platform : IncidentScopes.Agency,
                "Api",
                IncidentSeverities.Error,
                operation,
                release,
                fingerprint,
                now,
                context.TraceIdentifier,
                actorRole), cancellationToken);
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
