using System.Reflection;
using Sati.Contracts.V1;
using Sati.Services;

namespace Sati.Data.Cloud;

internal sealed class CloudIncidentReporter(CloudApiClient api, IncidentOutbox outbox) : IIncidentReporter
{
    private static readonly SemaphoreSlim FlushGate = new(1, 1);

    public async Task ReportAsync(
        Exception exception,
        string operation,
        string reference,
        string severity = IncidentSeverities.Error,
        CancellationToken cancellationToken = default)
    {
        try
        {
            outbox.Enqueue(new IncidentReportRequest(
                reference,
                "Desktop",
                severity,
                AppErrorLog.SafeArea(operation),
                Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown",
                AppErrorLog.CreateFingerprint(exception),
                DateTime.UtcNow));

            await FlushAsync(cancellationToken);
        }
        catch
        {
            // The sanitized envelope remains in the durable outbox for the next
            // authenticated session. Error reporting must not replace the failure.
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await FlushGate.WaitAsync(cancellationToken);
        try
        {
            foreach (var envelope in outbox.ReadPending())
            {
                try
                {
                    await api.PostAsync<IncidentReportRequest, IncidentGroupDto>(
                        "/api/v1/incidents",
                        envelope.Report,
                        cancellationToken);
                    outbox.Complete(envelope);
                }
                catch (CloudApiException exception) when ((int)exception.StatusCode is >= 400 and < 500 &&
                                                          exception.StatusCode != System.Net.HttpStatusCode.Unauthorized &&
                                                          exception.StatusCode != System.Net.HttpStatusCode.Forbidden &&
                                                          exception.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
                {
                    // A permanently invalid envelope must not block every later
                    // valid report. Preserve it for support review, then continue.
                    outbox.Reject(envelope);
                }
                catch
                {
                    // Preserve FIFO order. A later envelope should not leapfrog an
                    // earlier one while authentication or the network is unavailable.
                    break;
                }
            }
        }
        finally
        {
            FlushGate.Release();
        }
    }
}
