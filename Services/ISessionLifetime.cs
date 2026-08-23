namespace Sati.Services;

/// <summary>
/// Tells the shell that the signed-in session is over.
///
/// This exists so the window can offer a sign-in prompt without referencing the
/// cloud transport, and so the same shell code runs unchanged against a local
/// Production session — which holds no token, has nothing to expire, and is served
/// by <see cref="NeverEndingSessionLifetime"/>.
/// </summary>
public interface ISessionLifetime
{
    /// <summary>
    /// Raised once per ended session, possibly from a background thread. Handlers
    /// that touch UI must marshal to the dispatcher themselves.
    /// </summary>
    event EventHandler? SessionEnded;
}

/// <summary>
/// The local Production implementation. An EF session against a database the client
/// already has access to is bounded by the process, not by a credential, so there is
/// no expiry to announce and this never raises.
/// </summary>
public sealed class NeverEndingSessionLifetime : ISessionLifetime
{
    public event EventHandler? SessionEnded
    {
        add { }
        remove { }
    }
}
