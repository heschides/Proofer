using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.Contracts.V1;
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
    public async Task NearExpirySessionRenewsBeforeTheOriginalRequest()
    {
        var handler = new SequenceHandler(call => call == 1
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SessionRenewalResponse(
                    "renewed-token",
                    DateTimeOffset.UtcNow.AddMinutes(30)))
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new TestResponse("saved"))
            });
        using var http = NewHttpClient(handler);
        var api = new CloudApiClient(http, (_, _) => Task.CompletedTask);
        api.SetAccessToken("expiring-token", DateTimeOffset.UtcNow.AddMinutes(1));

        var response = await api.GetAsync<TestResponse>("/probe");

        Assert.Equal("saved", response.Value);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(["expiring-token", "renewed-token"], handler.AuthorizationTokens);
    }

    /// <summary>
    /// An idle desktop reaches the renewal window with a token the server will no
    /// longer accept, so the renewal itself is rejected. The old client surfaced a
    /// bare 401 and re-attempted renewal on every later call, which turned one dead
    /// session into a stream of rejected requests and screens that loaded empty.
    /// </summary>
    [Fact]
    public async Task RefusedRenewalEndsTheSessionInsteadOfRetryingOnEveryCall()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = NewHttpClient(handler);
        var api = new CloudApiClient(http, (_, _) => Task.CompletedTask);
        api.SetAccessToken("expiring-token", DateTimeOffset.UtcNow.AddMinutes(1));
        var endedNotifications = 0;
        api.SessionEnded += (_, _) => endedNotifications++;

        await Assert.ThrowsAsync<CloudSessionEndedException>(
            () => api.GetAsync<TestResponse>("/probe"));
        await Assert.ThrowsAsync<CloudSessionEndedException>(
            () => api.GetAsync<TestResponse>("/probe"));
        await Assert.ThrowsAsync<CloudSessionEndedException>(
            () => api.GetAsync<TestResponse>("/other"));

        // The renewal attempt, and nothing after it. The original request never went
        // out either: sending it would only have collected a second rejection.
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, endedNotifications);
    }

    [Fact]
    public async Task SigningInAgainReopensAClientWhoseSessionHadEnded()
    {
        var handler = new SequenceHandler(call => call == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new TestResponse("saved"))
            });
        using var http = NewHttpClient(handler);
        var api = new CloudApiClient(http, (_, _) => Task.CompletedTask);
        api.SetAccessToken("expiring-token", DateTimeOffset.UtcNow.AddMinutes(1));

        await Assert.ThrowsAsync<CloudSessionEndedException>(
            () => api.GetAsync<TestResponse>("/probe"));
        Assert.True(api.HasSessionEnded);

        api.SetAccessToken("fresh-token", DateTimeOffset.UtcNow.AddMinutes(30));

        Assert.False(api.HasSessionEnded);
        Assert.Equal("saved", (await api.GetAsync<TestResponse>("/probe")).Value);
        Assert.Equal(["expiring-token", "fresh-token"], handler.AuthorizationTokens);
    }

    /// <summary>
    /// The switch-user directory must not present an ended session as an empty
    /// account list; the dialog has no other way to explain why it is blank.
    /// </summary>
    [Fact]
    public async Task SwitchableUserDirectoryReportsAnEndedSessionRatherThanNoUsers()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = NewHttpClient(handler);
        var api = new CloudApiClient(http, (_, _) => Task.CompletedTask);
        api.SetAccessToken("expiring-token", DateTimeOffset.UtcNow.AddMinutes(1));
        var service = new CloudUserService(api);

        await Assert.ThrowsAsync<SessionExpiredException>(() => service.GetAllAsync());
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

    [Fact]
    public async Task UnauthorizedScratchpadSaveBecomesANonRetryingSessionExpiry()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = NewHttpClient(handler);
        var api = new CloudApiClient(http, (_, _) => Task.CompletedTask);
        api.SetAccessToken("expired-token");
        var service = new CloudScratchpadService(api);
        var scratchpad = new Scratchpad
        {
            Id = 17,
            UserId = 9,
            Date = DateTime.Today,
            Content = "private unsaved agenda",
            Revision = 4
        };

        var error = await Assert.ThrowsAsync<ScratchpadSessionExpiredException>(
            () => service.SaveAsync(scratchpad));

        Assert.Equal(1, handler.Calls);
        Assert.IsType<CloudApiException>(error.InnerException);
        Assert.DoesNotContain(scratchpad.Content, error.Message, StringComparison.Ordinal);
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
        public List<string?> AuthorizationTokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            AuthorizationTokens.Add(request.Headers.Authorization?.Parameter);
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
