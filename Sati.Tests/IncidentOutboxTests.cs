using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Sati.Data.Cloud;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class IncidentOutboxTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SatiIncidentOutboxTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnvelopeSurvivesUntilAcknowledgedAndDuplicateReferenceIsNotQueuedTwice()
    {
        var outbox = new IncidentOutbox(_root);
        var report = Report("REF_OUTBOX01");

        outbox.Enqueue(report);
        outbox.Enqueue(report);

        var pending = Assert.Single(outbox.ReadPending());
        Assert.Equal(report, pending.Report);

        outbox.Complete(pending);
        Assert.Empty(outbox.ReadPending());
    }

    [Fact]
    public async Task ReporterRetriesTheDurableEnvelopeAfterServiceRecovery()
    {
        var handler = new RecoveringHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo.invalid/") };
        var api = new CloudApiClient(http);
        api.SetAccessToken("test-token");
        var outbox = new IncidentOutbox(_root);
        var reporter = new CloudIncidentReporter(api, outbox);

        await reporter.ReportAsync(new InvalidOperationException("local only"),
            "calendar.day.open", "REF_OUTBOX02");

        Assert.Single(outbox.ReadPending());

        handler.IsAvailable = true;
        await reporter.FlushAsync();

        Assert.Empty(outbox.ReadPending());
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task PermanentlyInvalidEnvelopeDoesNotBlockTheNextValidReport()
    {
        var handler = new RejectFirstHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo.invalid/") };
        var api = new CloudApiClient(http);
        api.SetAccessToken("test-token");
        var outbox = new IncidentOutbox(_root);
        outbox.Enqueue(Report("REF_INVALID01"));
        outbox.Enqueue(Report("REF_VALID02"));

        await new CloudIncidentReporter(api, outbox).FlushAsync();

        Assert.Empty(outbox.ReadPending());
        Assert.Equal(2, handler.RequestCount);
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(_root, "Rejected"), "*.json"));
    }

    [Fact]
    public async Task NextMatchingSessionReportsAnUncleanPreviousTerminationOnce()
    {
        var runStateRoot = Path.Combine(_root, "run-state");
        var user = User.Create(
            91, "case-manager", "Case Manager", string.Empty, string.Empty,
            UserRole.CaseManager, null, 7);
        var firstReporter = new CapturingReporter();
        var firstRun = new ApplicationRunState(runStateRoot);
        await firstRun.StartSessionAsync(user, firstReporter);
        Assert.Empty(firstReporter.Operations);

        var recoveryReporter = new CapturingReporter();
        var recoveryRun = new ApplicationRunState(runStateRoot);
        await recoveryRun.StartSessionAsync(user, recoveryReporter);

        Assert.Equal(["application.previous-session-unclean"], recoveryReporter.Operations);

        recoveryRun.MarkGracefulExit();
        var cleanReporter = new CapturingReporter();
        await new ApplicationRunState(runStateRoot).StartSessionAsync(user, cleanReporter);
        Assert.Empty(cleanReporter.Operations);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static IncidentReportRequest Report(string reference) => new(
        reference,
        "Desktop",
        IncidentSeverities.Error,
        "calendar.day.open",
        "1.2.5",
        "ABCDEF0123456789ABCDEF0123456789",
        DateTime.UtcNow);

    private sealed class RecoveringHandler : HttpMessageHandler
    {
        public bool IsAvailable { get; set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (!IsAvailable)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var now = DateTime.UtcNow;
            var dto = new IncidentGroupDto(
                1, 1, IncidentScopes.Agency, "Desktop", IncidentSeverities.Error,
                "calendar.day.open", "1.2.5", "1.2.5",
                "ABCDEF0123456789ABCDEF0123456789", "Open", 1,
                now, now, "REF_OUTBOX02", "CaseManager");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(dto)
            });
        }
    }

    private sealed class RejectFirstHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));

            var now = DateTime.UtcNow;
            var dto = new IncidentGroupDto(
                1, 1, IncidentScopes.Agency, "Desktop", IncidentSeverities.Error,
                "calendar.day.open", "1.2.5", "1.2.5",
                "ABCDEF0123456789ABCDEF0123456789", "Open", 1,
                now, now, "REF_VALID02", "CaseManager");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(dto)
            });
        }
    }

    private sealed class CapturingReporter : IIncidentReporter
    {
        public List<string> Operations { get; } = [];

        public Task ReportAsync(
            Exception exception,
            string operation,
            string reference,
            string severity = "Error",
            CancellationToken cancellationToken = default)
        {
            Operations.Add(operation);
            return Task.CompletedTask;
        }
    }
}
