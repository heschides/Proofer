using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Xunit;

namespace Sati.Tests;

public sealed class CloudConnectivityTests
{
    [Fact]
    public async Task NameResolutionFailuresRetryBecauseNoRequestWasSent()
    {
        var handler = new SequenceHandler(call =>
        {
            if (call < 3)
                throw NameResolutionFailure();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new TestResponse("saved"))
            };
        });
        using var http = NewHttpClient(handler);
        var delays = new List<TimeSpan>();
        var api = new CloudApiClient(http, (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });
        api.SetAccessToken("test-token");

        var response = await api.GetAsync<TestResponse>("/probe");

        Assert.Equal("saved", response.Value);
        Assert.Equal(3, handler.Calls);
        Assert.Equal([TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1)], delays);
    }

    [Fact]
    public async Task ExhaustedNameResolutionRetriesExplainThatNothingWasSent()
    {
        var handler = new SequenceHandler(_ => throw NameResolutionFailure());
        using var http = NewHttpClient(handler);
        var api = new CloudApiClient(http, (_, _) => Task.CompletedTask);
        api.SetAccessToken("test-token");

        var error = await Assert.ThrowsAsync<CloudConnectivityException>(
            () => api.GetAsync<TestResponse>("/probe"));

        Assert.Equal(3, handler.Calls);
        Assert.True(error.RequestWasDefinitelyNotSent);
        Assert.Contains("after three attempts", error.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(error.InnerException);
    }

    [Fact]
    public async Task AmbiguousTimeoutIsClassifiedButNeverRetried()
    {
        var handler = new SequenceHandler(_ => throw new TaskCanceledException(
            "Synthetic HTTP timeout.", new TimeoutException("Synthetic timeout.")));
        using var http = NewHttpClient(handler);
        var api = new CloudApiClient(http, (_, _) => Task.CompletedTask);
        api.SetAccessToken("test-token");

        var error = await Assert.ThrowsAsync<CloudConnectivityException>(
            () => api.GetAsync<TestResponse>("/probe"));

        Assert.Equal(1, handler.Calls);
        Assert.False(error.RequestWasDefinitelyNotSent);
        Assert.Contains("did not repeat", error.Message, StringComparison.Ordinal);
        Assert.IsType<TaskCanceledException>(error.InnerException);
    }

    [Fact]
    public async Task CallerCancellationIsNotMisreportedAsAConnectivityFailure()
    {
        var handler = new CancelingHandler();
        using var http = NewHttpClient(handler);
        var api = new CloudApiClient(http, (_, _) => Task.CompletedTask);
        api.SetAccessToken("test-token");
        using var cancellation = new CancellationTokenSource();

        var request = api.GetAsync<TestResponse>("/probe", cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.IsNotType<CloudConnectivityException>(error);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ScratchpadSaveKeepsContentOutOfTheSafeConnectivityMessage()
    {
        var handler = new SequenceHandler(_ => throw NameResolutionFailure());
        using var http = NewHttpClient(handler);
        var api = new CloudApiClient(http, (_, _) => Task.CompletedTask);
        api.SetAccessToken("test-token");
        var service = new CloudScratchpadService(api);
        var scratchpad = new Scratchpad
        {
            Id = 17,
            UserId = 9,
            Date = DateTime.Today,
            Content = "private draft marker",
            Revision = 4
        };

        var error = await Assert.ThrowsAsync<ScratchpadSaveException>(
            () => service.SaveAsync(scratchpad));

        Assert.Equal(3, handler.Calls);
        Assert.IsType<CloudConnectivityException>(error.InnerException);
        Assert.Contains("request was not sent", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(scratchpad.Content, error.Message, StringComparison.Ordinal);
        Assert.Equal("private draft marker", scratchpad.Content);
        Assert.Equal(4, scratchpad.Revision);
    }

    private static HttpClient NewHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://example.invalid/")
    };

    private static HttpRequestException NameResolutionFailure() =>
        new("No such host is known.", new SocketException((int)SocketError.HostNotFound));

    private sealed record TestResponse(string Value);

    private sealed class SequenceHandler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(response(Calls));
        }
    }

    private sealed class CancelingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }
    }
}
