using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class AgencyReleaseApiTests
{
    private const int OwnPerson = 101;
    private const int OtherAgencyPerson = 201;
    private readonly SatiApiFactory _factory;

    public AgencyReleaseApiTests(SatiApiFactory factory) => _factory = factory;

    private static string Route(int personId) => $"/api/v1/people/{personId}/agency-release.pdf";

    [Fact]
    public async Task Anonymous_caller_cannot_generate_a_release()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(Route(OwnPerson), ValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Another_agencys_consumer_is_not_reachable()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.PostAsJsonAsync(Route(OtherAgencyPerson), ValidRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_release_is_rejected_before_generation()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var request = ValidRequest() with { ContactName = "" };

        var response = await client.PostAsJsonAsync(Route(OwnPerson), request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Own_release_returns_a_non_cacheable_pdf_and_is_audited()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var before = await _factory.GetAuditEventsAsync("agency-release.generated");

        var response = await client.PostAsJsonAsync(Route(OwnPerson), ValidRequest());

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        var after = await _factory.GetAuditEventsAsync("agency-release.generated");
        Assert.Equal(before.Count + 1, after.Count);
        Assert.Equal(OwnPerson.ToString(), after[^1].ResourceId);
    }

    private static AgencyReleaseRequest ValidRequest() => new(
        true,
        "Community support",
        "Community Provider",
        "Service provider",
        "1 Center Street",
        "Augusta",
        "ME",
        null,
        "207-555-0100",
        null,
        [AgencyReleaseInformation.IntakeAssessment],
        null,
        new DateOnly(2026, 8, 19),
        new DateOnly(2026, 11, 17),
        nameof(AgencyReleaseScope.OneTime),
        false,
        false,
        false,
        false);
}
