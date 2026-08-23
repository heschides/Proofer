using Sati.Data.Cloud;
using Sati.Services;
using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class SessionKeepAliveTests
{
    private static readonly DateTimeOffset SignIn = new(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly DateTimeOffset Expiry = SignIn + TokenLifetime;

    /// <summary>Idleness allowed between renewals: the lifetime less the margin.</summary>
    private static readonly TimeSpan IdleGrace = TokenLifetime - CloudApiClient.RenewalMargin;

    /// <summary>
    /// The reason a fixed poll cannot stand in for token-driven scheduling. Renewal is
    /// possible only between minute 25 and minute 30, so a twenty-minute interval waits
    /// at minute 20 and next wakes at minute 40, holding a token that died at thirty.
    /// </summary>
    [Fact]
    public void TwentyMinutePollStepsOverTheRenewalWindowEntirely()
    {
        var atTwenty = SessionKeepAlive.Decide(
            Expiry, sessionEnded: false, SignIn.AddMinutes(20), SignIn.AddMinutes(20), IdleGrace);
        Assert.Equal(KeepAliveStep.WaitForRenewalWindow, atTwenty.Step);

        var atForty = SessionKeepAlive.Decide(
            Expiry, sessionEnded: false, SignIn.AddMinutes(40), SignIn.AddMinutes(40), IdleGrace);
        Assert.Equal(KeepAliveStep.NoSession, atForty.Step);
    }

    [Fact]
    public void SchedulingWaitsExactlyUntilTheRenewalMarginOpens()
    {
        var decision = SessionKeepAlive.Decide(
            Expiry, sessionEnded: false, SignIn, SignIn, IdleGrace);

        Assert.Equal(KeepAliveStep.WaitForRenewalWindow, decision.Step);
        Assert.Equal(TokenLifetime - CloudApiClient.RenewalMargin, decision.Delay);
    }

    [Fact]
    public void APresentUserInsideTheMarginRenews()
    {
        var now = Expiry - TimeSpan.FromMinutes(4);
        var decision = SessionKeepAlive.Decide(
            Expiry, sessionEnded: false, now, now - TimeSpan.FromMinutes(2), IdleGrace);

        Assert.Equal(KeepAliveStep.Renew, decision.Step);
    }

    /// <summary>
    /// The gate that keeps an unattended workstation from holding a session for the
    /// twelve hours the server would otherwise allow.
    /// </summary>
    [Fact]
    public void AnIdleWorkstationIsNotRenewedAndLapses()
    {
        var now = Expiry - TimeSpan.FromMinutes(4);
        var decision = SessionKeepAlive.Decide(
            Expiry, sessionEnded: false, now, SignIn, IdleGrace);

        Assert.Equal(KeepAliveStep.WaitForUser, decision.Step);
    }

    [Fact]
    public void ActivityDuringTheMarginStillSavesTheSession()
    {
        // Away all morning, back with one minute of runway left.
        var now = Expiry - TimeSpan.FromMinutes(1);
        var decision = SessionKeepAlive.Decide(
            Expiry, sessionEnded: false, now, now, IdleGrace);

        Assert.Equal(KeepAliveStep.Renew, decision.Step);
    }

    [Fact]
    public void AnEndedSessionIsNotRenewedButIsStillWatchedForANewSignIn()
    {
        var now = Expiry - TimeSpan.FromMinutes(1);
        var decision = SessionKeepAlive.Decide(
            Expiry, sessionEnded: true, now, now, IdleGrace);

        Assert.Equal(KeepAliveStep.NoSession, decision.Step);
        Assert.Equal(SessionKeepAlive.RecheckInterval, decision.Delay);
    }

    /// <summary>
    /// End to end against a fake transport: an app that makes no requests of its own
    /// still holds its session, which is the whole point of the loop.
    /// </summary>
    [Fact]
    public async Task AnIdleAppWithAPresentUserRenewsWithoutAnyOtherTraffic()
    {
        var renewals = 0;
        var handler = new RenewalHandler(() =>
        {
            renewals++;
            return new SessionRenewalResponse($"renewed-{renewals}", DateTimeOffset.UtcNow.AddMinutes(30));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo.invalid") };
        var api = new CloudApiClient(http);
        api.SetAccessToken("first-token", DateTimeOffset.UtcNow.AddMinutes(1));

        var released = new TaskCompletionSource();
        using var keepAlive = new SessionKeepAlive(
            api,
            () => DateTimeOffset.UtcNow,
            (_, _) =>
            {
                released.TrySetResult();
                return Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
            },
            IdleGrace);

        keepAlive.Start();
        await released.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, renewals);
        Assert.Equal("renewed-1", handler.LastIssuedToken);
    }

    private sealed class RenewalHandler(Func<SessionRenewalResponse> next) : HttpMessageHandler
    {
        public string? LastIssuedToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var renewal = next();
            LastIssuedToken = renewal.AccessToken;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(renewal)
            });
        }
    }
}
