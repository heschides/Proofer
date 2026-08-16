namespace Sati.Services;

/// <summary>
/// Gives overlapping read operations a monotonically increasing identity so only
/// the newest request is allowed to publish results into shared UI state.
/// </summary>
internal sealed class LatestRequestTracker
{
    private int _latest;

    public int Begin() => Interlocked.Increment(ref _latest);

    public bool IsCurrent(int request) => request == Volatile.Read(ref _latest);

    public void Invalidate() => Interlocked.Increment(ref _latest);
}
