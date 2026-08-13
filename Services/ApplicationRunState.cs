using System.IO;
using System.Reflection;
using System.Text.Json;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;

namespace Sati.Services;

internal sealed record ApplicationRunMarker(
    int AgencyId,
    string Scope,
    string Role,
    string Release,
    DateTime StartedAtUtc);

internal sealed class UnexpectedApplicationTerminationException : Exception
{
    public UnexpectedApplicationTerminationException()
        : base("The preceding Sati session did not record a graceful exit.")
    {
    }
}

public sealed class ApplicationRunState
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private string? _activeMarkerPath;

    public ApplicationRunState() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Satilogica", "Sati", "RunState"))
    {
    }

    internal ApplicationRunState(string directory) => _directory = directory;

    public async Task StartSessionAsync(
        User user,
        IIncidentReporter reporter,
        CancellationToken cancellationToken = default)
    {
        MarkGracefulExit();
        Directory.CreateDirectory(_directory);
        var scope = user.Role == UserRole.PlatformOperator
            ? IncidentScopes.Platform
            : IncidentScopes.Agency;
        var path = Path.Combine(_directory, $"agency-{user.AgencyId}-{scope.ToLowerInvariant()}.json");
        var previousRunWasUnclean = TryReadMarker(path) is not null;
        var marker = new ApplicationRunMarker(
            user.AgencyId,
            scope,
            user.Role.ToString(),
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown",
            DateTime.UtcNow);
        WriteMarker(path, marker);
        _activeMarkerPath = path;

        if (!previousRunWasUnclean)
            return;

        var exception = new UnexpectedApplicationTerminationException();
        var reference = AppErrorLog.Record(exception, "application.previous-session-unclean");
        await reporter.ReportAsync(
            exception,
            "application.previous-session-unclean",
            reference,
            IncidentSeverities.Critical,
            cancellationToken);
    }

    public void MarkGracefulExit()
    {
        if (_activeMarkerPath is null)
            return;

        try
        {
            if (File.Exists(_activeMarkerPath))
                File.Delete(_activeMarkerPath);
        }
        finally
        {
            _activeMarkerPath = null;
        }
    }

    private ApplicationRunMarker? TryReadMarker(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ApplicationRunMarker>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void WriteMarker(string path, ApplicationRunMarker marker)
    {
        var temporary = path + $".pending-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(marker, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
