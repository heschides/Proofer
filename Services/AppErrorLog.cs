using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Markup;

namespace Sati.Services;

internal static class AppErrorLog
{
    private static readonly object Sync = new();

    public static string Record(
        Exception exception,
        string area,
        string? directoryOverride = null)
    {
        var reference = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        try
        {
            var directory = directoryOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SatiLogica",
                "Sati",
                "Logs");
            Directory.CreateDirectory(directory);
            var entry = BuildEntry(exception, area, reference);
            var path = Path.Combine(directory, $"sati-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            lock (Sync)
                File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch (Exception loggingException)
        {
            Debug.WriteLine(
                $"Sati error {reference} could not be written. Logger failure type: {loggingException.GetType().FullName}");
        }

        return reference;
    }

    private static object BuildEntry(Exception exception, string area, string reference) => new
    {
        timestampUtc = DateTime.UtcNow,
        reference,
        area = SafeArea(area),
        applicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
        exceptionType = exception.GetType().FullName,
        hResult = $"0x{exception.HResult:X8}",
        target = exception.TargetSite?.DeclaringType?.FullName,
        stackTrace = exception.StackTrace,
        innerExceptionType = exception.InnerException?.GetType().FullName,
        innerTarget = exception.InnerException?.TargetSite?.DeclaringType?.FullName,
        innerHResult = exception.InnerException is null
            ? null
            : $"0x{exception.InnerException.HResult:X8}",
        xamlLineNumber = exception is XamlParseException xaml ? (int?)xaml.LineNumber : null,
        xamlLinePosition = exception is XamlParseException xamlPosition ? (int?)xamlPosition.LinePosition : null
    };

    internal static string CreateFingerprint(Exception exception)
    {
        var shape = string.Join('|',
            exception.GetType().FullName,
            exception.HResult.ToString("X8"),
            exception.TargetSite?.DeclaringType?.FullName,
            exception.TargetSite?.Name,
            exception.InnerException?.GetType().FullName,
            exception.InnerException?.HResult.ToString("X8"),
            exception.InnerException?.TargetSite?.DeclaringType?.FullName,
            exception.InnerException?.TargetSite?.Name);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shape)));
    }

    internal static string SafeArea(string area)
    {
        var safe = new string((area ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            .Take(80)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }
}
