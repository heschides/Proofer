namespace Sati.Data;

/// <summary>
/// The signed-in session is over and no further server call can succeed until the
/// user signs in again.
///
/// This exists so a view model can tell "your session ended" apart from "that
/// request failed" without referencing the cloud transport. A screen that cannot
/// load because the session is gone must say so: presenting an empty list instead
/// reads as "there is nothing here", which is a different and false statement.
///
/// Only the cloud services throw it. A local Production session holds no token and
/// therefore has no expiry to report.
/// </summary>
public sealed class SessionExpiredException(Exception innerException)
    : Exception(
        "Your Demo session has expired. Sign in again to continue.",
        innerException);
