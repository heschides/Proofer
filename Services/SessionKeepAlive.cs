using Sati.Data.Cloud;

namespace Sati.Services;

/// <summary>What the keep-alive should do at the moment it wakes up.</summary>
internal enum KeepAliveStep
{
    /// <summary>Sleep until the token enters its renewal margin.</summary>
    WaitForRenewalWindow,

    /// <summary>Inside the margin with a present user: replace the token now.</summary>
    Renew,

    /// <summary>Inside the margin with nobody here: look again shortly, or let it lapse.</summary>
    WaitForUser,

    /// <summary>No session to keep alive. Look again in case one is signed in later.</summary>
    NoSession
}

internal readonly record struct KeepAliveDecision(KeepAliveStep Step, TimeSpan Delay);

/// <summary>
/// Renews an active Demo session on the token's own schedule.
///
/// Renewal authenticates with the token it replaces, so it only works inside the
/// <see cref="CloudApiClient.RenewalMargin"/> before expiry. Left to ordinary traffic,
/// that window is entered only if the user happens to make a request during it — an
/// app sitting idle makes no calls at all, so a quiet half hour ends the session even
/// though the person is still at their desk. Waking at <c>expiry - margin</c> removes
/// the coincidence. Note that a fixed poll cannot: an interval that steps over the
/// margin arrives after expiry and renews nothing.
///
/// Renewal is gated on the user actually being here. Without that gate an unattended
/// workstation would hold a signed-in session for the full twelve hours the server
/// allows; with it, the session lapses after <see cref="_idleGrace"/> of no input and
/// the shell asks for credentials again. An active session still ends at the server's
/// twelve-hour cap regardless, because renewal preserves the original sign-in time.
/// </summary>
public sealed class SessionKeepAlive : IDisposable
{
    /// <summary>How soon to look again when there is nothing to do yet.</summary>
    internal static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Idleness allowed between renewals, used when no token has been seen so no
    /// lifetime could be observed.
    ///
    /// This is the gap between renewals, not the idle timeout the user experiences.
    /// A session skipped at the renewal point still runs to expiry, so the effective
    /// allowance is this plus <see cref="CloudApiClient.RenewalMargin"/> — the token
    /// lifetime. Measuring against the token lifetime instead would double it: at the
    /// first renewal only <c>lifetime - margin</c> can possibly have elapsed, so the
    /// gate could never close and the session would always survive one extra cycle.
    /// </summary>
    internal static readonly TimeSpan DefaultIdleGrace =
        TimeSpan.FromMinutes(30) - CloudApiClient.RenewalMargin;

    private readonly CloudApiClient _api;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan? _configuredIdleGrace;

    private long _lastActivityUtcTicks;
    private TimeSpan _idleGrace = DefaultIdleGrace;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public SessionKeepAlive(CloudApiClient api)
        : this(api, () => DateTimeOffset.UtcNow, Task.Delay, null)
    {
    }

    internal SessionKeepAlive(
        CloudApiClient api,
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delay,
        TimeSpan? idleGrace)
    {
        _api = api;
        _utcNow = utcNow;
        _delay = delay;
        _configuredIdleGrace = idleGrace;
        _lastActivityUtcTicks = utcNow().UtcTicks;
    }

    internal DateTimeOffset LastActivityUtc =>
        new(Interlocked.Read(ref _lastActivityUtcTicks), TimeSpan.Zero);

    internal TimeSpan IdleGrace => _idleGrace;

    /// <summary>
    /// Records that the user is still here. Called for raw input, so it must stay a
    /// single write — anything heavier would run on every mouse move.
    /// </summary>
    public void NoteUserActivity() =>
        Interlocked.Exchange(ref _lastActivityUtcTicks, _utcNow().UtcTicks);

    /// <summary>
    /// Begins keeping the current session alive. Safe to call again after a new
    /// sign-in; the loop already survives a lapse and picks up the new token.
    /// </summary>
    public void Start()
    {
        if (_loop is not null)
            return;

        // Derived from the observed token lifetime, so changing the server's
        // TokenMinutes moves this with it rather than leaving a constant here to rot.
        _idleGrace = _configuredIdleGrace
            ?? (_api.AccessTokenExpiresAtUtc is DateTimeOffset expiresAt
                ? expiresAt - _utcNow() - CloudApiClient.RenewalMargin
                : DefaultIdleGrace);
        if (_idleGrace <= TimeSpan.Zero)
            _idleGrace = DefaultIdleGrace;

        NoteUserActivity();
        _cancellation = new CancellationTokenSource();
        _loop = RunAsync(_cancellation.Token);
    }

    internal static KeepAliveDecision Decide(
        DateTimeOffset? expiresAtUtc,
        bool sessionEnded,
        DateTimeOffset nowUtc,
        DateTimeOffset lastActivityUtc,
        TimeSpan idleGrace)
    {
        // A lapsed or absent session is not an error here. Someone may sign in again,
        // and the loop should be waiting when they do rather than already finished.
        if (sessionEnded || expiresAtUtc is not DateTimeOffset expiresAt || nowUtc >= expiresAt)
            return new KeepAliveDecision(KeepAliveStep.NoSession, RecheckInterval);

        var renewAt = expiresAt - CloudApiClient.RenewalMargin;
        if (nowUtc < renewAt)
            return new KeepAliveDecision(KeepAliveStep.WaitForRenewalWindow, renewAt - nowUtc);

        if (nowUtc - lastActivityUtc >= idleGrace)
            return new KeepAliveDecision(KeepAliveStep.WaitForUser, RecheckInterval);

        return new KeepAliveDecision(KeepAliveStep.Renew, TimeSpan.Zero);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var decision = Decide(
                    _api.AccessTokenExpiresAtUtc,
                    _api.HasSessionEnded,
                    _utcNow(),
                    LastActivityUtc,
                    _idleGrace);

                if (decision.Step == KeepAliveStep.Renew)
                {
                    if (!await TryRenewAsync(cancellationToken))
                        await Wait(RecheckInterval, cancellationToken);
                    continue;
                }

                await Wait(decision.Delay, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown or a new sign-in replacing the loop. Nothing to report.
        }
    }

    private async Task<bool> TryRenewAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _api.EnsureSessionRenewedAsync(cancellationToken);
            return true;
        }
        catch (CloudSessionEndedException)
        {
            // The server refused it. The shell hears this through ISessionLifetime;
            // the loop keeps waiting in case the user signs in again.
            return true;
        }
        catch (CloudConnectivityException)
        {
            // The margin leaves several minutes of runway, so a brief outage still
            // has time to clear before the token actually expires.
            return false;
        }
    }

    private Task Wait(TimeSpan delay, CancellationToken cancellationToken) =>
        _delay(delay > TimeSpan.Zero ? delay : RecheckInterval, cancellationToken);

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _loop = null;
    }
}
