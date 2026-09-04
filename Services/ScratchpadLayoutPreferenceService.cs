using Sati.Data;
using System.IO;
using System.Text.Json;

namespace Sati.Services;

public sealed class ScratchpadLayoutPreferenceSaveException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Stores whether the Scratchpad (Today's Work / Tomorrow's Agenda) renders in the
/// main content area instead of the collapsible side panel, per Sati user and
/// environment in the current Windows profile. Local presentation state, not agency
/// data.
///
/// Deliberately its own file rather than folded into another preference store, for
/// the same reason those stores are already separate from each other: a malformed
/// preference file should cost a user only that one setting, not every personal
/// preference at once.
/// </summary>
public sealed class ScratchpadLayoutPreferenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly DataEnvironmentInfo _environment;
    private readonly string _preferencePath;
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    public ScratchpadLayoutPreferenceService(DataEnvironmentInfo environment)
        : this(
            environment,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sati",
                "scratchpad-layout-preferences.json"))
    {
    }

    internal ScratchpadLayoutPreferenceService(
        DataEnvironmentInfo environment,
        string preferencePath)
    {
        _environment = environment;
        _preferencePath = preferencePath;
    }

    public bool IsCentered { get; private set; }
    public string? LastLoadWarning { get; private set; }
    public event EventHandler<bool>? PreferenceChanged;

    public async Task<bool> LoadForUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        bool isCentered;

        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            LastLoadWarning = null;
            try
            {
                var document = await ReadDocumentAsync(cancellationToken);
                isCentered = document.Profiles.TryGetValue(ProfileKey(userId), out var profile)
                    && profile.IsCentered;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                LastLoadWarning =
                    "Your Scratchpad layout preference could not be loaded. Sati will use the standard layout.";
                isCentered = false;
            }
        }
        finally
        {
            _fileGate.Release();
        }

        SetCurrentValue(isCentered);
        return isCentered;
    }

    public async Task SetCenteredAsync(
        int userId,
        bool isCentered,
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
                throw new ScratchpadLayoutPreferenceSaveException(
                    "The Scratchpad layout preference file could not be read, so Sati left it unchanged.",
                    exception);
            }

            document.Profiles[ProfileKey(userId)] = new PreferenceProfile(isCentered);

            try
            {
                await WriteDocumentAsync(document, cancellationToken);
                LastLoadWarning = null;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw new ScratchpadLayoutPreferenceSaveException(
                    "The Scratchpad layout preference could not be saved to this Windows account.",
                    exception);
            }
        }
        finally
        {
            _fileGate.Release();
        }

        SetCurrentValue(isCentered);
    }

    private void SetCurrentValue(bool isCentered)
    {
        if (IsCentered == isCentered)
            return;

        IsCentered = isCentered;
        PreferenceChanged?.Invoke(this, isCentered);
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
            ?? throw new IOException("The Scratchpad layout preference folder is unavailable.");
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

    private sealed record PreferenceProfile(bool IsCentered = false);
}
