using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Services;

namespace Sati.ViewModels;

public sealed partial class DatabaseActivityViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan DefaultPatienceDelay = TimeSpan.FromSeconds(8);

    private readonly IDatabaseActivityTracker _tracker;
    private readonly TimeSpan _patienceDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SynchronizationContext? _synchronizationContext;
    private CancellationTokenSource? _patienceCancellation;
    private int _generation;
    private bool _disposed;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isPatienceVisible;

    public DatabaseActivityViewModel(IDatabaseActivityTracker tracker)
        : this(tracker, DefaultPatienceDelay, Task.Delay, SynchronizationContext.Current)
    {
    }

    internal DatabaseActivityViewModel(
        IDatabaseActivityTracker tracker,
        TimeSpan patienceDelay,
        Func<TimeSpan, CancellationToken, Task> delay,
        SynchronizationContext? synchronizationContext)
    {
        _tracker = tracker;
        _patienceDelay = patienceDelay;
        _delay = delay;
        _synchronizationContext = synchronizationContext;
        _tracker.ActivityChanged += OnActivityChanged;
        RefreshFromTracker();
    }

    private void OnActivityChanged(object? sender, DatabaseActivityChangedEventArgs e) =>
        RunOnCapturedContext(RefreshFromTracker);

    private void RefreshFromTracker()
    {
        if (_disposed)
            return;

        var busy = _tracker.IsBusy;
        if (busy == IsBusy)
            return;

        _generation++;
        _patienceCancellation?.Cancel();
        _patienceCancellation?.Dispose();
        _patienceCancellation = null;

        IsBusy = busy;
        if (!busy)
        {
            IsPatienceVisible = false;
            return;
        }

        IsPatienceVisible = false;
        var generation = _generation;
        _patienceCancellation = new CancellationTokenSource();
        _ = ShowPatienceAfterDelayAsync(generation, _patienceCancellation.Token);
    }

    private async Task ShowPatienceAfterDelayAsync(int generation, CancellationToken cancellationToken)
    {
        // Await WhenAny first so a canceled delay is observed by state rather than by throwing
        // TaskCanceledException. Short successful calls cancel this timer routinely; treating
        // that expected control flow as an exception floods debugger output and can hide the
        // real network exception that ended the data call.
        var completedDelay = await Task.WhenAny(_delay(_patienceDelay, cancellationToken));
        if (completedDelay.IsCanceled)
            return;

        // Preserve genuine delay faults. Only cancellation is an expected outcome here.
        await completedDelay;

        RunOnCapturedContext(() =>
        {
            if (!_disposed && generation == _generation && _tracker.IsBusy)
                IsPatienceVisible = true;
        });
    }

    private void RunOnCapturedContext(Action action)
    {
        if (_synchronizationContext is null ||
            ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            action();
            return;
        }

        _synchronizationContext.Post(_ => action(), null);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _tracker.ActivityChanged -= OnActivityChanged;
        _patienceCancellation?.Cancel();
        _patienceCancellation?.Dispose();
    }
}
