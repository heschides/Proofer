using System.Net;
using System.Net.Http;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Cloud;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The startup check that names a behind-server before its symptoms do.
///
/// Written after 2026-08-19, when the hosted API was missing five routes the client
/// had begun calling and every one of them surfaced to a case manager as "the record
/// was not found or is outside your caseload". The release number could not have
/// caught it — both sides read 1.2.17, because a release is numbered when it is cut
/// and not when a route is added. So the comparison is over the route surface, and
/// these tests pin the two properties that matter: it fires when the surfaces differ,
/// and it stays quiet otherwise, including when there is no server at all.
/// </summary>
public sealed class ApiCompatibilityTests
{
    [Fact]
    public async Task AMatchingSurfaceRaisesNothing()
    {
        var service = ServiceFor(Version(ApiSurface.Revision));

        var result = await service.CheckAsync();

        Assert.False(result.Disagrees);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task ADifferentSurfaceIsReported()
    {
        var service = ServiceFor(Version("0000DEADBEEF"));

        var result = await service.CheckAsync();

        Assert.True(result.Disagrees);
        Assert.Contains("Publish the API", result.Detail);
    }

    /// <summary>
    /// The exact case that started this. Equal release numbers must not be mistaken
    /// for agreement — that reading is what let five missing routes look healthy.
    /// </summary>
    [Fact]
    public async Task AnIdenticalReleaseNumberDoesNotImplyAgreement()
    {
        var sameRelease = $$"""
            {"product":"Sati.Api","releaseVersion":"1.2.17","contractRevision":"0000DEADBEEF"}
            """;
        var service = ServiceFor(sameRelease);

        var result = await service.CheckAsync();

        Assert.True(result.Disagrees);
        Assert.Equal("1.2.17", result.ServerRelease);
    }

    /// <summary>
    /// A server predating the field itself is at least one deployment behind, and
    /// saying so is more useful than treating a missing value as agreement.
    /// </summary>
    [Fact]
    public async Task AServerWithNoContractRevisionIsReported()
    {
        var service = ServiceFor("""{"product":"Sati.Api","releaseVersion":"1.2.17"}""");

        var result = await service.CheckAsync();

        Assert.True(result.Disagrees);
        Assert.Contains("predates this client", result.Detail);
    }

    /// <summary>
    /// An unreachable server is a network problem that every other screen reports
    /// better. Raising a compatibility banner would point at the wrong thing, and
    /// throwing would turn a warning into a failed sign-in.
    /// </summary>
    [Fact]
    public async Task AnUnreachableServerRaisesNothingAndDoesNotThrow()
    {
        var service = ServiceFor(null, HttpStatusCode.ServiceUnavailable);

        var result = await service.CheckAsync();

        Assert.False(result.Disagrees);
    }

    /// <summary>Local Production has no server to disagree with.</summary>
    [Fact]
    public async Task TheLocalPathAlwaysAgrees()
    {
        var result = await new LocalApiCompatibilityService().CheckAsync();

        Assert.False(result.Disagrees);
        Assert.Null(result.Detail);
    }

    /// <summary>
    /// The check asks the unversioned health route on purpose: a server whose
    /// versioned surface is exactly what is in question still has to be able to answer.
    /// </summary>
    [Fact]
    public async Task TheCheckAsksTheUnversionedHealthRoute()
    {
        var handler = new StubHandler(Version(ApiSurface.Revision), HttpStatusCode.OK);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.invalid") };
        var api = new CloudApiClient(client);
        api.SetAccessToken("test-token");

        await new CloudApiCompatibilityService(api).CheckAsync();

        Assert.Equal("https://api.invalid/health/version", handler.LastUri?.ToString());
    }

    private static string Version(string revision) => $$"""
        {"product":"Sati.Api","releaseVersion":"1.2.17","contractRevision":"{{revision}}"}
        """;

    private static CloudApiCompatibilityService ServiceFor(
        string? body,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var client = new HttpClient(new StubHandler(body, status))
        {
            BaseAddress = new Uri("https://api.invalid")
        };
        var api = new CloudApiClient(client);
        api.SetAccessToken("test-token");
        return new CloudApiCompatibilityService(api);
    }

    private sealed class StubHandler(string? body, HttpStatusCode status) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            var response = new HttpResponseMessage(status);
            if (body is not null)
                response.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        }
    }
}
