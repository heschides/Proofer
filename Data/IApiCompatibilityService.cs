namespace Sati.Data;

/// <summary>
/// What a startup compatibility check concluded.
///
/// <paramref name="Disagrees"/> is deliberately not called "IsBehind": the client
/// cannot tell direction from a fingerprint, only that the two surfaces differ. In
/// practice the client is the newer of the two, because the desktop is rebuilt from
/// source while the hosted API is published separately — but saying "behind" would
/// be asserting something the check never established.
/// </summary>
/// <param name="Disagrees">The server serves a different route surface than this build expects.</param>
/// <param name="ServerRelease">The release the server reports, for the message. Null if unreachable.</param>
/// <param name="Detail">One sentence a case manager can act on, or null when everything agrees.</param>
public sealed record ApiCompatibility(bool Disagrees, string? ServerRelease, string? Detail)
{
    /// <summary>Nothing to report: the surfaces match, or there is no server to disagree with.</summary>
    public static ApiCompatibility Agreed { get; } = new(false, null, null);
}

/// <summary>
/// Compares the API surface this client was built against with the one the server
/// actually serves, once, at sign-in.
///
/// Exists because the alternative is discovering the mismatch one feature at a time,
/// in whichever screen touches a new route first, as an error that describes
/// something else entirely. On 2026-08-19 five missing routes surfaced to a case
/// manager as "the record was not found or is outside your caseload" — a caseload
/// problem that did not exist, on a record that was fine.
///
/// Never throws and never blocks sign-in. A server that cannot be reached is a
/// network problem the rest of the application will report far better than a version
/// check can, and refusing to start over a failed comparison would turn a warning
/// into an outage.
/// </summary>
public interface IApiCompatibilityService
{
    Task<ApiCompatibility> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Local Production has no server to disagree with — the client and the schema are
/// the same deployment — so the check is a constant.
/// </summary>
public sealed class LocalApiCompatibilityService : IApiCompatibilityService
{
    public Task<ApiCompatibility> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ApiCompatibility.Agreed);
}
