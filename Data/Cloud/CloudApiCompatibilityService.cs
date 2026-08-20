using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

/// <summary>
/// Asks the hosted API which route surface it serves and compares it with the one
/// this build expects.
///
/// Both sides derive their fingerprint from <see cref="ApiSurface"/>, which is
/// generated from the API's own endpoint table and held to it by a test. So a
/// disagreement means precisely one thing — the two builds do not serve and call the
/// same routes — and it cannot be produced by a forgotten version bump.
/// </summary>
public sealed class CloudApiCompatibilityService(CloudApiClient client) : IApiCompatibilityService
{
    public async Task<ApiCompatibility> CheckAsync(CancellationToken cancellationToken = default)
    {
        ApiVersionDto? server;
        try
        {
            // Outside /api/v1 on purpose: this route has to be answerable by a server
            // whose versioned surface is exactly what is in question.
            server = await client.GetAsync<ApiVersionDto>("/health/version", cancellationToken);
        }
        catch (Exception)
        {
            // A server that cannot be reached is a network problem, and every other
            // screen will report it more precisely than a version check can. Claiming
            // a mismatch here would raise a banner about the wrong thing.
            return ApiCompatibility.Agreed;
        }

        if (server is null || string.IsNullOrWhiteSpace(server.ContractRevision))
        {
            // A server old enough to predate the field itself. That is a real
            // disagreement and worth saying so, since it is at least one deployment
            // behind by definition.
            return new ApiCompatibility(
                true,
                server?.ReleaseVersion,
                "The server does not report a contract revision, so it predates this client. " +
                "Publish the API before relying on recently added features.");
        }

        if (string.Equals(server.ContractRevision, ApiSurface.Revision, StringComparison.Ordinal))
            return ApiCompatibility.Agreed;

        return new ApiCompatibility(
            true,
            server.ReleaseVersion,
            $"This client expects a different set of API routes than release {server.ReleaseVersion} " +
            "serves. Features that use a route the server does not have will report the record as " +
            "missing rather than naming the real cause. Publish the API to clear this.");
    }
}
