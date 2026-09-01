using Sati.Data;
using System.IO;
using System.Text.Json;

namespace Sati.Services;

public sealed record DailyAgendaPreference(
    bool ShowAtSignIn = true,
    DateOnly? LastShownDate = null);

public sealed class DailyAgendaPreferenceSaveException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Stores the login-agenda display preference and last-shown date per Sati user
/// and environment in the current Windows profile. This is local presentation
/// state, not an agency setting and not a clinical record.
/// </summary>
public sealed class DailyAgendaPreferenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly DataEnvironmentInfo _environment;
    private readonly string _preferencePath;
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    public DailyAgendaPreferenceService(DataEnvironmentInfo environment)
        : this(
            environment,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sati",
                "daily-agenda-preferences.json"))
    {
    }

    internal DailyAgendaPreferenceService(
        DataEnvironmentInfo environment,
        string preferencePath)
    {
        _environment = environment;
        _preferencePath = preferencePath;
    }

    public string? LastLoadWarning { get; private set; }

    public async Task<DailyAgendaPreference> LoadForUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            LastLoadWarning = null;
            try
            {
                var document = await ReadDocumentAsync(cancellationToken);
                return document.Profiles.TryGetValue(ProfileKey(userId), out var profile)
                    ? new DailyAgendaPreference(profile.ShowAtSignIn, profile.LastShownDate)
                    : new DailyAgendaPreference();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                LastLoadWarning =
                    "Your daily agenda preference could not be loaded. Sati will use the default for this sign-in.";
                return new DailyAgendaPreference();
            }
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public Task SetShowAtSignInAsync(
        int userId,
        bool showAtSignIn,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            userId,
            profile => profile with { ShowAtSignIn = showAtSignIn },
            cancellationToken);

    public Task MarkShownAsync(
        int userId,
        DateOnly localDate,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            userId,
            profile => profile with { LastShownDate = localDate },
            cancellationToken);

    private async Task UpdateAsync(
        int userId,
        Func<PreferenceProfile, PreferenceProfile> update,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            PreferenceDocument document;
            try
            {
                document = await ReadDocumentAsync(cancellationToken);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                throw new DailyAgendaPreferenceSaveException(
                    "The daily agenda preference file could not be read, so Sati left it unchanged.",
                    exception);
            }

            var key = ProfileKey(userId);
            var current = document.Profiles.TryGetValue(key, out var saved)
                ? saved
                : new PreferenceProfile();
            document.Profiles[key] = update(current);

            try
            {
                await WriteDocumentAsync(document, cancellationToken);
                LastLoadWarning = null;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw new DailyAgendaPreferenceSaveException(
                    "The daily agenda preference could not be saved to this Windows account.",
                    exception);
            }
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private async Task<PreferenceDocument> ReadDocumentAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_preferencePath))
            return new PreferenceDocument();

        await using var stream = File.OpenRead(_preferencePath);
        var document = await JsonSerializer.DeserializeAsync<PreferenceDocument>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? new PreferenceDocument();
        document.Profiles ??=
            new Dictionary<string, PreferenceProfile>(StringComparer.OrdinalIgnoreCase);
        return document;
    }

    private async Task WriteDocumentAsync(
        PreferenceDocument document,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_preferencePath)
            ?? throw new IOException("The daily agenda preference folder is unavailable.");
        Directory.CreateDirectory(directory);

        var temporary = _preferencePath + $".pending-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    JsonOptions,
                    cancellationToken);
            }

            File.Move(temporary, _preferencePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private string ProfileKey(int userId) => $"{_environment.Environment}:{userId}";

    private static void ValidateUserId(int userId)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));
    }

    private sealed class PreferenceDocument
    {
        public Dictionary<string, PreferenceProfile> Profiles { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record PreferenceProfile(
        bool ShowAtSignIn = true,
        DateOnly? LastShownDate = null);
}
