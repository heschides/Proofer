namespace Sati.Services;

/// <summary>
/// Exercises the real application-wide loading indicator without issuing a database or API call.
/// The preview holds one ordinary activity lease for long enough to show both the immediate leaf
/// animation and the delayed patience window.
/// </summary>
public sealed class DatabaseActivityPreview
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(12);

    private readonly IDatabaseActivityTracker _tracker;
    private readonly TimeSpan _duration;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private int _isRunning;

    public DatabaseActivityPreview(IDatabaseActivityTracker tracker)
        : this(tracker, DefaultDuration, Task.Delay)
    {
    }

    internal DatabaseActivityPreview(
        IDatabaseActivityTracker tracker,
        TimeSpan duration,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _tracker = tracker;
        _duration = duration;
        _delay = delay;
    }

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    public async Task<bool> TryRunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            return false;

        try
        {
            using var activity = _tracker.Begin();
            await _delay(_duration, cancellationToken);
            return true;
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }
}
