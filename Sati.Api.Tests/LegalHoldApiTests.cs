using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// Placing and releasing a legal hold — the fail-closed gate rule-3 deletion checks before
/// removing a record. See HANDOFF_CLIENT_DELETION_POLICY.md, A3.
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class LegalHoldApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task AnAdminCanPlaceAHold()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var person = await CreateConsumerAsync(admin);

        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/legal-holds",
            new PlaceLegalHoldRequest(
                person.Id, "MaineCare program integrity review", "PI-2026-014",
                "MaineCare Program Integrity", DateTime.UtcNow));
        var hold = await response.Content.ReadFromJsonAsync<LegalHoldDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(person.Id, hold!.PersonId);
        Assert.False(hold.IsReleased);
    }

    [Fact]
    public async Task ACaseManagerCannotPlaceAHold()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var person = await CreateConsumerAsync(admin);

        var response = await caseManager.PostAsJsonAsync(
            "/api/v1/admin/legal-holds",
            new PlaceLegalHoldRequest(person.Id, "Attempted forgery", null, null, DateTime.UtcNow));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnAdminCannotPlaceAHoldOnAConsumerInAnotherAgency()
    {
        using var agencyOneAdmin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var agencyTwoCaseManager = await factory.CreateAuthenticatedClientAsync("case-manager-two");
        var foreignPerson = await CreateConsumerAsync(agencyTwoCaseManager);

        var response = await agencyOneAdmin.PostAsJsonAsync(
            "/api/v1/admin/legal-holds",
            new PlaceLegalHoldRequest(foreignPerson.Id, "Wrong agency", null, null, DateTime.UtcNow));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnAdminCanReleaseAHoldTheyPlaced()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var person = await CreateConsumerAsync(admin);
        var placeResponse = await admin.PostAsJsonAsync(
            "/api/v1/admin/legal-holds",
            new PlaceLegalHoldRequest(person.Id, "Under review", null, null, DateTime.UtcNow));
        var placed = await placeResponse.Content.ReadFromJsonAsync<LegalHoldDto>();

        var releaseResponse = await admin.PostAsJsonAsync(
            $"/api/v1/admin/legal-holds/{placed!.Id}/release",
            new ReleaseLegalHoldRequest("Review concluded."));
        var released = await releaseResponse.Content.ReadFromJsonAsync<LegalHoldDto>();

        Assert.Equal(HttpStatusCode.OK, releaseResponse.StatusCode);
        Assert.True(released!.IsReleased);
        Assert.Equal("Review concluded.", released.ReleaseNote);
    }

    [Fact]
    public async Task AnAlreadyReleasedHoldCannotBeReleasedAgain()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var person = await CreateConsumerAsync(admin);
        var placeResponse = await admin.PostAsJsonAsync(
            "/api/v1/admin/legal-holds",
            new PlaceLegalHoldRequest(person.Id, "Under review", null, null, DateTime.UtcNow));
        var placed = await placeResponse.Content.ReadFromJsonAsync<LegalHoldDto>();
        await admin.PostAsJsonAsync(
            $"/api/v1/admin/legal-holds/{placed!.Id}/release", new ReleaseLegalHoldRequest(null));

        var secondRelease = await admin.PostAsJsonAsync(
            $"/api/v1/admin/legal-holds/{placed.Id}/release", new ReleaseLegalHoldRequest(null));

        Assert.Equal(HttpStatusCode.Conflict, secondRelease.StatusCode);
    }

    [Fact]
    public async Task GetLegalHoldsListsHoldsForThatConsumer()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var person = await CreateConsumerAsync(admin);
        await admin.PostAsJsonAsync(
            "/api/v1/admin/legal-holds",
            new PlaceLegalHoldRequest(person.Id, "Under review", null, null, DateTime.UtcNow));

        var holds = await admin.GetFromJsonAsync<List<LegalHoldDto>>(
            $"/api/v1/admin/legal-holds?personId={person.Id}");

        Assert.Single(holds!);
        Assert.Equal(person.Id, holds![0].PersonId);
    }

    // ---- Helpers ----

    private static async Task<PersonDto> CreateConsumerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/people",
            ValidRequest() with { LastName = Guid.NewGuid().ToString("N")[..10] });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PersonDto>())!;
    }

    private static SavePersonRequest ValidRequest() => new(
        "Hold", "Test", new DateTime(1990, 4, 3), "Unknown", null,
        "A consumer created for legal-hold tests.", "None",
        null, null, null, null, false, false, null, null, null, null, null, null, null, null,
        null, false, false, false, false, false, false, 1, false, false, false,
        [], 0, true, false, null, null, false, false, "legal-hold-test@example.test");
}
