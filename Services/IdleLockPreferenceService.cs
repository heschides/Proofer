using Sati.Data;
using System.IO;
using System.Text.Json;

namespace Sati.Services;

public sealed class IdleLockPreferenceSaveException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Stores how long Sati waits before covering the screen, per Sati user and
/// environment in the current Windows profile. Local presentation state, not
/// agency data, so it needs no migration and never leaves the machine.
///
/// It deliberately mirrors <see cref="EasyEyesPreferenceService"/> rather than
/// sharing its file, so a malformed idle preference cannot cost a user their
/// Easy Eyes setting. Consolidating the two stores is tracked in AGENDA.md.
/// </summary>
public sealed class IdleLockPreferenceService
{
    /// <summary>Ten minutes, the value asked for when the feature was specified.</summary>
    public const int DefaultMinutes = 10;

    /// <summary>Zero disables the overlay entirely.</summary>
    public const int DisabledMinutes = 0;

    public const int MaximumMinutes = 240;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly DataEnvironmentInfo _environment;
    private readonly string _preferencePath;
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    public IdleLockPreferenceService(DataEnvironmentInfo environment)
        : this(
            environment,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sati",
                "idle-lock-preferences.json"))
    {
    }

    internal IdleLockPreferenceService(DataEnvironmentInfo environment, string preferencePath)
    {
        _environment = environment;
        _preferencePath = preferencePath;
    }

    public int TimeoutMinutes { get; private set; } = DefaultMinutes;
    public string? LastLoadWarning { get; private set; }
    public event EventHandler<int>? PreferenceChanged;

    /// <summary>
    /// Clamps to a supported range. An out-of-range or corrupt stored value falls
    /// back to the default rather than leaving the screen uncovered forever or
    /// covering it after a second.
    /// </summary>
    public static int Normalize(int minutes) => minutes switch
    {
        <= DisabledMinutes => DisabledMinutes,
        > MaximumMinutes => MaximumMinutes,
        _ => minutes
    };

    public async Task<int> LoadForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        int minutes;

        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            LastLoadWarning = null;
            try
            {
                var document = await ReadDocumentAsync(cancellationToken);
                minutes = document.Profiles.TryGetValue(ProfileKey(userId), out var profile)
                    ? Normalize(profile.TimeoutMinutes)
                    : DefaultMinutes;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                LastLoadWarning =
                    "Your inactivity screen preference could not be loaded. Sati will use ten minutes.";
                minutes = DefaultMinutes;
            }
        }
        finally
        {
            _fileGate.Release();
        }

        SetCurrentValue(minutes);
        return minutes;
    }

    public async Task SetTimeoutAsync(
        int userId,
        int minutes,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        var normalized = Normalize(minutes);

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
                throw new IdleLockPreferenceSaveException(
                    "The inactivity screen preference file could not be read, so Sati left it unchanged.",
                    exception);
            }

            document.Profiles[ProfileKey(userId)] = new PreferenceProfile(normalized);

            try
            {
                await WriteDocumentAsync(document, cancellationToken);
                LastLoadWarning = null;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw new IdleLockPreferenceSaveException(
                    "The inactivity screen preference could not be saved to this Windows account.",
                    exception);
            }
        }
        finally
        {
            _fileGate.Release();
        }

        SetCurrentValue(normalized);
    }

    private void SetCurrentValue(int minutes)
    {
        if (TimeoutMinutes == minutes)
            return;

        TimeoutMinutes = minutes;
        PreferenceChanged?.Invoke(this, minutes);
    }

    private async Task<PreferenceDocument> ReadDocumentAsync(CancellationToken cancellationToken)
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
            ?? throw new IOException("The inactivity preference folder is unavailable.");
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
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
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

    private sealed record PreferenceProfile(int TimeoutMinutes = DefaultMinutes);
}
