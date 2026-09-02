using Sati.Data;
using System.IO;
using System.Text.Json;

namespace Sati.Services;

public sealed class EasyEyesPreferenceSaveException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Stores the Easy Eyes display preference per Sati user and environment in the
/// current Windows profile. This is local presentation state, not agency data.
/// </summary>
public sealed class EasyEyesPreferenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly DataEnvironmentInfo _environment;
    private readonly string _preferencePath;
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    public EasyEyesPreferenceService(DataEnvironmentInfo environment)
        : this(
            environment,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sati",
                "easy-eyes-preferences.json"))
    {
    }

    internal EasyEyesPreferenceService(
        DataEnvironmentInfo environment,
        string preferencePath)
    {
        _environment = environment;
        _preferencePath = preferencePath;
    }

    public bool IsEnabled { get; private set; }
    public string? LastLoadWarning { get; private set; }
    public event EventHandler<bool>? PreferenceChanged;

    public async Task<bool> LoadForUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        bool enabled;

        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            LastLoadWarning = null;
            try
            {
                var document = await ReadDocumentAsync(cancellationToken);
                enabled = document.Profiles.TryGetValue(ProfileKey(userId), out var profile)
                    && profile.IsEnabled;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                LastLoadWarning =
                    "Your Easy Eyes preference could not be loaded. Sati will use the standard display.";
                enabled = false;
            }
        }
        finally
        {
            _fileGate.Release();
        }

        SetCurrentValue(enabled);
        return enabled;
    }

    public async Task SetEnabledAsync(
        int userId,
        bool enabled,
        CancellationToken cancellationToken = default)
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
                throw new EasyEyesPreferenceSaveException(
                    "The Easy Eyes preference file could not be read, so Sati left it unchanged.",
                    exception);
            }

            document.Profiles[ProfileKey(userId)] = new PreferenceProfile(enabled);

            try
            {
                await WriteDocumentAsync(document, cancellationToken);
                LastLoadWarning = null;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw new EasyEyesPreferenceSaveException(
                    "The Easy Eyes preference could not be saved to this Windows account.",
                    exception);
            }
        }
        finally
        {
            _fileGate.Release();
        }

        SetCurrentValue(enabled);
    }

    private void SetCurrentValue(bool enabled)
    {
        if (IsEnabled == enabled)
            return;

        IsEnabled = enabled;
        PreferenceChanged?.Invoke(this, enabled);
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
            ?? throw new IOException("The Easy Eyes preference folder is unavailable.");
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

    private sealed record PreferenceProfile(bool IsEnabled = false);
}
