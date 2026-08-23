namespace Sati.Services;

public interface IDatabaseActivityTracker
{
    bool IsBusy { get; }
    int ActiveCount { get; }
    event EventHandler<DatabaseActivityChangedEventArgs>? ActivityChanged;
    IDisposable Begin();
}

public sealed class DatabaseActivityChangedEventArgs(int activeCount) : EventArgs
{
    public int ActiveCount { get; } = activeCount;
    public bool IsBusy => ActiveCount > 0;
}

/// <summary>
/// Counts overlapping database/API operations without retaining query text, route bodies, or PHI.
/// Every lease must be disposed; duplicate disposal is harmless.
/// </summary>
public sealed class DatabaseActivityTracker : IDatabaseActivityTracker
{
    private int _activeCount;

    public bool IsBusy => ActiveCount > 0;
    public int ActiveCount => Math.Max(0, Volatile.Read(ref _activeCount));
    public event EventHandler<DatabaseActivityChangedEventArgs>? ActivityChanged;

    public IDisposable Begin()
    {
        var count = Interlocked.Increment(ref _activeCount);
        ActivityChanged?.Invoke(this, new DatabaseActivityChangedEventArgs(count));
        return new Lease(this);
    }

    private void End()
    {
        var count = Interlocked.Decrement(ref _activeCount);
        if (count < 0)
        {
            Interlocked.Exchange(ref _activeCount, 0);
            count = 0;
        }

        ActivityChanged?.Invoke(this, new DatabaseActivityChangedEventArgs(count));
    }

    private sealed class Lease(DatabaseActivityTracker owner) : IDisposable
    {
        private DatabaseActivityTracker? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.End();
    }
}
