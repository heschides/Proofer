namespace Sati.Data;

public interface IIncidentReporter
{
    Task ReportAsync(
        Exception exception,
        string operation,
        string reference,
        string severity = "Error",
        CancellationToken cancellationToken = default);
}
