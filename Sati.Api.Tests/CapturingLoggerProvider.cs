using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Sati.Api.Tests;

/// <summary>
/// Records everything the API logs so a test can assert on what is NOT in it.
///
/// Redaction is unusual among security properties in that nothing observable goes
/// wrong when it fails: the request succeeds, the response is correct, and a
/// consumer's Social Security number is sitting in a log file. The only way to hold
/// the line is to read the log and look.
///
/// Formatted messages, exception text, and scope values all land here, because a
/// number can leak through any of the three — an exception whose message quotes the
/// value it rejected is the usual way.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private static readonly ConcurrentQueue<string> Lines = new();

    public static void Clear() => Lines.Clear();

    /// <summary>Everything logged since the last <see cref="Clear"/>, as one string.</summary>
    public static string Captured() => string.Join("\n", Lines);

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName);

    public void Dispose() { }

    private sealed class CapturingLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            Lines.Enqueue($"[scope] {state}");
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Lines.Enqueue($"[{logLevel}] {category} {formatter(state, exception)}");
            if (exception is not null)
                Lines.Enqueue(exception.ToString());
        }
    }
}
