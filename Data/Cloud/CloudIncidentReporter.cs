using System.Reflection;
using Sati.Contracts.V1;
using Sati.Services;

namespace Sati.Data.Cloud;

public sealed class CloudIncidentReporter(CloudApiClient api) : IIncidentReporter
{
    public async Task ReportAsync(
        Exception exception,
        string operation,
        string reference,
        string severity = IncidentSeverities.Error,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await api.PostAsync<IncidentReportRequest, IncidentGroupDto>(
                "/api/v1/incidents",
                new IncidentReportRequest(
                    reference,
                    "Desktop",
                    severity,
                    AppErrorLog.SafeArea(operation),
                    Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown",
                    AppErrorLog.CreateFingerprint(exception),
                    DateTime.UtcNow),
                cancellationToken);
        }
        catch
        {
            // Keep the local diagnostic record even when the network is unavailable.
        }
    }
}
