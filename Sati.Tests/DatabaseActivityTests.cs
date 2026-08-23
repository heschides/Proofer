using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Services;
using Sati.ViewModels;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Xunit;

namespace Sati.Tests;

public sealed class DatabaseActivityTests
{
    [Fact]
    public void OverlappingOperationsRemainBusyUntilTheFinalLeaseEnds()
    {
        var tracker = new DatabaseActivityTracker();
        var states = new List<int>();
        tracker.ActivityChanged += (_, change) => states.Add(change.ActiveCount);

        using var first = tracker.Begin();
        using var second = tracker.Begin();

        Assert.True(tracker.IsBusy);
        Assert.Equal(2, tracker.ActiveCount);

        first.Dispose();
        Assert.True(tracker.IsBusy);
        Assert.Equal(1, tracker.ActiveCount);

        second.Dispose();
        second.Dispose();

        Assert.False(tracker.IsBusy);
        Assert.Equal(0, tracker.ActiveCount);
        Assert.Equal([1, 2, 1, 0], states);
    }

    [Fact]
    public async Task PatienceStateAppearsOnlyAfterTheConfiguredContinuousDelay()
    {
        var tracker = new DatabaseActivityTracker();
        var delayStarted = NewSignal();
        var releaseDelay = NewSignal();
        Task ControlledDelay(TimeSpan _, CancellationToken cancellationToken)
        {
            delayStarted.TrySetResult();
            return releaseDelay.Task.WaitAsync(cancellationToken);
        }

        using var display = new DatabaseActivityViewModel(
            tracker,
            TimeSpan.FromSeconds(8),
            ControlledDelay,
            synchronizationContext: null);
        using var activity = tracker.Begin();

        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(display.IsBusy);
        Assert.False(display.IsPatienceVisible);

        releaseDelay.TrySetResult();
        await EventuallyAsync(() => display.IsPatienceVisible);

        activity.Dispose();
        Assert.False(display.IsBusy);
        Assert.False(display.IsPatienceVisible);
        Assert.Equal(TimeSpan.FromSeconds(8), DatabaseActivityViewModel.DefaultPatienceDelay);
    }

    [Fact]
    public async Task CompletingBeforeTheDelayPreventsALatePatiencePopup()
    {
        var tracker = new DatabaseActivityTracker();
        var releaseDelay = NewSignal();
        Task ControlledDelay(TimeSpan _, CancellationToken cancellationToken) =>
            releaseDelay.Task.WaitAsync(cancellationToken);

        using var display = new DatabaseActivityViewModel(
            tracker,
            TimeSpan.FromSeconds(8),
            ControlledDelay,
            synchronizationContext: null);

        var activity = tracker.Begin();
        activity.Dispose();
        releaseDelay.TrySetResult();
        await Task.Delay(25);

        Assert.False(display.IsBusy);
        Assert.False(display.IsPatienceVisible);
    }

    [Fact]
    public async Task SettingsPreviewUsesOneRealActivityLeaseWithoutADataCallOrReentry()
    {
        var tracker = new DatabaseActivityTracker();
        var delayStarted = NewSignal();
        var releaseDelay = NewSignal();
        TimeSpan? requestedDuration = null;
        Task ControlledDelay(TimeSpan duration, CancellationToken cancellationToken)
        {
            requestedDuration = duration;
            delayStarted.TrySetResult();
            return releaseDelay.Task.WaitAsync(cancellationToken);
        }

        var preview = new DatabaseActivityPreview(
            tracker,
            DatabaseActivityPreview.DefaultDuration,
            ControlledDelay);

        var firstRun = preview.TryRunAsync();
        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(preview.IsRunning);
        Assert.True(tracker.IsBusy);
        Assert.Equal(1, tracker.ActiveCount);
        Assert.Equal(TimeSpan.FromSeconds(12), requestedDuration);
        Assert.False(await preview.TryRunAsync());
        Assert.Equal(1, tracker.ActiveCount);

        releaseDelay.TrySetResult();
        Assert.True(await firstRun);
        Assert.False(preview.IsRunning);
        Assert.False(tracker.IsBusy);
    }

    [Fact]
    public async Task CancelingSettingsPreviewAlwaysReleasesTheActivityLease()
    {
        var tracker = new DatabaseActivityTracker();
        var delayStarted = NewSignal();
        Task ControlledDelay(TimeSpan _, CancellationToken cancellationToken)
        {
            delayStarted.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        var preview = new DatabaseActivityPreview(
            tracker,
            DatabaseActivityPreview.DefaultDuration,
            ControlledDelay);
        using var cancellation = new CancellationTokenSource();
        var run = preview.TryRunAsync(cancellation.Token);
        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.False(preview.IsRunning);
        Assert.False(tracker.IsBusy);
    }

    [Fact]
    public async Task CloudPipelineTracksSuccessAndReleasesAfterFailure()
    {
        var tracker = new DatabaseActivityTracker();
        var inner = new ControlledHttpHandler();
        var activityHandler = new DatabaseActivityHandler(tracker) { InnerHandler = inner };
        using var client = new HttpClient(activityHandler);

        var request = client.GetAsync("https://example.invalid/people");
        await inner.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(tracker.IsBusy);

        inner.Complete(new HttpResponseMessage(HttpStatusCode.OK));
        using var response = await request;
        Assert.False(tracker.IsBusy);

        var throwing = new ThrowingHttpHandler();
        using var failingClient = new HttpClient(
            new DatabaseActivityHandler(tracker) { InnerHandler = throwing });
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            failingClient.GetAsync("https://example.invalid/failure"));
        Assert.False(tracker.IsBusy);
    }

    [Fact]
    public void LocalCommandInterceptorReferenceCountsCommandsAndFailureCleanup()
    {
        var tracker = new DatabaseActivityTracker();
        var interceptor = new DatabaseActivityCommandInterceptor(tracker);
        var readerCommand = Guid.NewGuid();
        var saveCommand = Guid.NewGuid();

        interceptor.TrackStarted(readerCommand);
        interceptor.TrackStarted(saveCommand);
        Assert.Equal(2, tracker.ActiveCount);

        interceptor.TrackCompleted(saveCommand);
        Assert.True(tracker.IsBusy);

        interceptor.TrackCompleted(readerCommand);
        interceptor.TrackCompleted(readerCommand);
        Assert.False(tracker.IsBusy);
    }

    [Fact]
    public async Task LocalEfQueryReleasesItsReaderLeaseAfterMaterialization()
    {
        var tracker = new DatabaseActivityTracker();
        var interceptor = new DatabaseActivityCommandInterceptor(tracker);
        var sawActiveCommand = false;
        tracker.ActivityChanged += (_, change) => sawActiveCommand |= change.IsBusy;

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new SatiContext(options);

        await db.Database.EnsureCreatedAsync();
        _ = await db.People.AsNoTracking().ToListAsync();

        Assert.True(sawActiveCommand);
        Assert.False(tracker.IsBusy);
        Assert.Equal(0, tracker.ActiveCount);
    }

    [Fact]
    public void ShellAndPatienceWindowUseTheColorLeafFastAnimationAndAccessibleText()
    {
        var root = FindRepositoryRoot();
        var shell = File.ReadAllText(Path.Combine(root, "Views", "ShellWindow.xaml"));
        var patience = File.ReadAllText(Path.Combine(root, "Views", "DatabasePatienceWindow.xaml"));
        var settings = File.ReadAllText(Path.Combine(root, "Views", "SettingsWindow.xaml"));
        var shellCode = File.ReadAllText(Path.Combine(root, "Views", "ShellWindow.xaml.cs"));
        var app = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));

        Assert.Contains("sati-watercolor-leaf.png", shell, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"60\"", shell, StringComparison.Ordinal);
        Assert.Contains("Duration=\"0:0:0.42\"", shell, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Retrieving information from Sati\"", shell, StringComparison.Ordinal);
        Assert.Contains("Thank you for your patience", patience, StringComparison.Ordinal);
        Assert.Contains("close automatically", patience, StringComparison.Ordinal);
        Assert.Contains("ShowActivated=\"False\"", patience, StringComparison.Ordinal);
        Assert.Contains("Content=\"Test loading indicator\"", settings, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding PreviewLoadingIndicatorCommand}\"", settings, StringComparison.Ordinal);
        Assert.Contains("does not query the database or access client records", settings, StringComparison.Ordinal);
        Assert.Contains("Duration=\"0:0:0.42\"", settings, StringComparison.Ordinal);
        Assert.Contains("activeOwnedWindow", shellCode, StringComparison.Ordinal);
        Assert.Contains("Func<DatabasePatienceWindow>", app, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<DatabaseActivityPreview>", app, StringComparison.Ordinal);
        Assert.Contains("AddHttpMessageHandler<DatabaseActivityHandler>", app, StringComparison.Ordinal);
        Assert.Contains("DatabaseActivityCommandInterceptor", app, StringComparison.Ordinal);
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var testsDirectory = Directory.GetParent(sourceFilePath);
        var repositoryRoot = testsDirectory?.Parent;
        Assert.NotNull(repositoryRoot);
        Assert.True(File.Exists(Path.Combine(repositoryRoot.FullName, "Sati.slnx")));
        return repositoryRoot.FullName;
    }

    private sealed class ControlledHttpHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> _response =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = NewSignal();

        public void Complete(HttpResponseMessage response) => _response.TrySetResult(response);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return await _response.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Synthetic failure."));
    }
}
