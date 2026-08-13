using System.IO;
using System.Text.Json;
using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

internal sealed record PendingIncidentEnvelope(string Path, IncidentReportRequest Report);

internal sealed class IncidentOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _pendingDirectory;
    private readonly string _rejectedDirectory;

    public IncidentOutbox() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Satilogica", "Sati", "IncidentOutbox"))
    {
    }

    internal IncidentOutbox(string rootDirectory)
    {
        _pendingDirectory = Path.Combine(rootDirectory, "Pending");
        _rejectedDirectory = Path.Combine(rootDirectory, "Rejected");
    }

    public void Enqueue(IncidentReportRequest report)
    {
        Directory.CreateDirectory(_pendingDirectory);
        var safeReference = new string(report.Reference
            .Where(char.IsLetterOrDigit)
            .Take(40)
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeReference))
            safeReference = Guid.NewGuid().ToString("N");

        if (Directory.EnumerateFiles(_pendingDirectory, $"*-{safeReference}.json").Any())
            return;

        var name = $"{report.OccurredAtUtc.Ticks:D19}-{safeReference}.json";
        var destination = Path.Combine(_pendingDirectory, name);
        var temporary = destination + $".pending-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(report, JsonOptions));
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public IReadOnlyList<PendingIncidentEnvelope> ReadPending()
    {
        if (!Directory.Exists(_pendingDirectory))
            return [];

        var pending = new List<PendingIncidentEnvelope>();
        foreach (var path in Directory.EnumerateFiles(_pendingDirectory, "*.json")
                     .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var report = JsonSerializer.Deserialize<IncidentReportRequest>(
                    File.ReadAllText(path), JsonOptions)
                    ?? throw new JsonException("The incident envelope was empty.");
                pending.Add(new PendingIncidentEnvelope(path, report));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                Quarantine(path);
            }
        }

        return pending;
    }

    public void Complete(PendingIncidentEnvelope envelope)
    {
        if (File.Exists(envelope.Path))
            File.Delete(envelope.Path);
    }

    public void Reject(PendingIncidentEnvelope envelope) => Quarantine(envelope.Path);

    private void Quarantine(string path)
    {
        try
        {
            Directory.CreateDirectory(_rejectedDirectory);
            var destination = Path.Combine(
                _rejectedDirectory,
                $"{Path.GetFileNameWithoutExtension(path)}-invalid-{Guid.NewGuid():N}.json");
            File.Move(path, destination);
        }
        catch
        {
            // Keep an unreadable envelope in place if it cannot be quarantined.
        }
    }
}
