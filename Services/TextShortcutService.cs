using Sati.Data;
using System.IO;
using System.Text.Json;

namespace Sati.Services;

public sealed class TextShortcutSaveException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Stores personal text snippets for one Sati user and environment in the current
/// Windows profile. These are client-side typing preferences, not agency records.
/// </summary>
public sealed class TextShortcutService
{
    public const int ShortcutCount = 10;
    public const int MaximumTextLength = 200;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly DataEnvironmentInfo _environment;
    private readonly string _preferencePath;
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private string[] _activeTexts = EmptyTexts();
    private int? _activeUserId;

    public TextShortcutService(DataEnvironmentInfo environment)
        : this(
            environment,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sati",
                "text-shortcuts.json"))
    {
    }

    internal TextShortcutService(DataEnvironmentInfo environment, string preferencePath)
    {
        _environment = environment;
        _preferencePath = preferencePath;
    }

    public string? LastLoadWarning { get; private set; }

    public async Task LoadForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));

        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            LastLoadWarning = null;
            string[] texts;
            try
            {
                var document = await ReadDocumentAsync(cancellationToken);
                texts = document.Profiles.TryGetValue(ProfileKey(userId), out var saved)
                    ? NormalizeSavedTexts(saved)
                    : EmptyTexts();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                texts = EmptyTexts();
                LastLoadWarning =
                    "Your personal text shortcuts could not be loaded. Existing shortcut text was not changed.";
            }

            _activeUserId = userId;
            Volatile.Write(ref _activeTexts, texts);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public IReadOnlyList<string> GetActiveTexts() => [.. Volatile.Read(ref _activeTexts)];

    public string? GetTextForDigit(int digit)
    {
        if (digit is < 0 or > 9 || _activeUserId is null)
            return null;

        var index = digit == 0 ? 9 : digit - 1;
        var value = Volatile.Read(ref _activeTexts)[index];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public async Task SaveForUserAsync(
        int userId,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));

        var normalized = ValidateTexts(texts);
        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            ShortcutDocument document;
            try
            {
                document = await ReadDocumentAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                throw new TextShortcutSaveException(
                    "The shortcut file could not be read, so Sati left it unchanged.",
                    exception);
            }

            document.Profiles[ProfileKey(userId)] = [.. normalized];
            try
            {
                await WriteDocumentAsync(document, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new TextShortcutSaveException(
                    "The shortcuts could not be saved to this Windows account.",
                    exception);
            }

            _activeUserId = userId;
            Volatile.Write(ref _activeTexts, normalized);
            LastLoadWarning = null;
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private async Task<ShortcutDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_preferencePath))
            return new ShortcutDocument();

        await using var stream = File.OpenRead(_preferencePath);
        var document = await JsonSerializer.DeserializeAsync<ShortcutDocument>(stream, JsonOptions, cancellationToken)
            ?? new ShortcutDocument();
        document.Profiles ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        return document;
    }

    private async Task WriteDocumentAsync(
        ShortcutDocument document,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_preferencePath)
            ?? throw new IOException("The shortcut folder is unavailable.");
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

    private static string[] ValidateTexts(IReadOnlyList<string> texts)
    {
        if (texts.Count != ShortcutCount)
            throw new ArgumentException($"Exactly {ShortcutCount} shortcut values are required.", nameof(texts));

        var normalized = texts.Select(value => value ?? string.Empty).ToArray();
        if (normalized.Any(value => value.Length > MaximumTextLength))
        {
            throw new ArgumentException(
                $"Shortcut text cannot exceed {MaximumTextLength} characters.",
                nameof(texts));
        }

        return normalized;
    }

    private static string[] NormalizeSavedTexts(IReadOnlyList<string>? texts)
    {
        var normalized = EmptyTexts();
        if (texts is null)
            return normalized;

        for (var index = 0; index < Math.Min(texts.Count, ShortcutCount); index++)
        {
            var value = texts[index] ?? string.Empty;
            normalized[index] = value.Length <= MaximumTextLength
                ? value
                : value[..MaximumTextLength];
        }

        return normalized;
    }

    private static string[] EmptyTexts() =>
        Enumerable.Repeat(string.Empty, ShortcutCount).ToArray();

    private sealed class ShortcutDocument
    {
        public Dictionary<string, List<string>> Profiles { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
