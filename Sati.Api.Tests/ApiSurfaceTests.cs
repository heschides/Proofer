using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// Keeps <see cref="ApiSurface"/> honest.
///
/// The manifest is only worth having if it cannot drift. Adding a route without
/// updating it would leave the client comparing against a stale fingerprint and
/// reporting agreement it never checked — the exact failure that made this whole
/// mechanism necessary, reproduced one level up.
///
/// So this fails the build rather than the deployment. If it goes red after you add
/// an endpoint, that is working: regenerate the list from the names it prints.
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class ApiSurfaceTests
{
    private readonly SatiApiFactory _factory;

    public ApiSurfaceTests(SatiApiFactory factory) => _factory = factory;

    [Fact]
    public void TheManifestMatchesTheRoutesTheApiActuallyServes()
    {
        var live = LiveRoutes();
        var declared = ApiSurface.Routes.OrderBy(x => x, StringComparer.Ordinal).ToList();

        var missing = live.Except(declared, StringComparer.Ordinal).ToList();
        var stale = declared.Except(live, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0 && stale.Count == 0,
            $"ApiSurface.Routes is out of date.\n" +
            $"Served but not declared ({missing.Count}):\n  {string.Join("\n  ", missing)}\n" +
            $"Declared but not served ({stale.Count}):\n  {string.Join("\n  ", stale)}");
    }

    /// <summary>
    /// The fingerprint has to be a function of the surface and nothing else, or two
    /// builds serving the same routes could disagree and cry wolf on every startup.
    /// </summary>
    [Fact]
    public void TheFingerprintDependsOnlyOnTheRouteSet()
    {
        var shuffled = ApiSurface.Routes.Reverse().ToList();

        Assert.Equal(ApiSurface.Revision, ApiSurface.Fingerprint(shuffled));
        Assert.Equal(ApiSurface.Revision, ApiSurface.Fingerprint(LiveRoutes()));
    }

    /// <summary>
    /// And it must actually change when the surface does, or a behind-server would
    /// still report agreement.
    /// </summary>
    [Fact]
    public void RemovingARouteChangesTheFingerprint()
    {
        var withoutOne = ApiSurface.Routes.Skip(1).ToList();

        Assert.NotEqual(ApiSurface.Revision, ApiSurface.Fingerprint(withoutOne));
    }

    /// <summary>
    /// The route list is a map of the attack surface and is deliberately not
    /// published. Only the fingerprint crosses the wire.
    /// </summary>
    [Fact]
    public async Task TheVersionEndpointPublishesTheFingerprintAndNotTheRoutes()
    {
        using var client = _factory.CreateAnonymousClient();

        var body = await (await client.GetAsync("/health/version")).Content.ReadAsStringAsync();

        Assert.Contains(ApiSurface.Revision, body);
        Assert.DoesNotContain("/api/v1/", body);
    }

    private List<string> LiveRoutes()
    {
        using var scope = _factory.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint =>
            {
                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"];
                return $"{string.Join(",", methods)} {endpoint.RoutePattern.RawText}";
            })
            .OrderBy(route => route, StringComparer.Ordinal)];
    }
}
