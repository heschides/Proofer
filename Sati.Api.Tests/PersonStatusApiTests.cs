using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// <c>PUT /api/v1/people/{id}/status</c> — archiving and restoring a consumer.
///
/// <para>
/// Non-destructive: this changes caseload visibility and compliance-work generation, never data.
/// <see cref="PersonStatusRules"/> owns who may set which status; the interesting boundary is
/// that only an Admin may set <c>Ghost</c>, because that status makes the same claim the rule-3
/// deletion attestation makes. See HANDOFF_CLIENT_DELETION_POLICY.md.
/// </para>
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class PersonStatusApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task ACaseManagerCanMarkTheirOwnConsumerNoLongerServed()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var created = await CreateConsumerAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/people/{created.Id}/status",
            new SetPersonStatusRequest(PersonStatusRules.NoLongerServed, "Moved away.", created.Revision));
        var result = await response.Content.ReadFromJsonAsync<PersonStatusDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PersonStatusRules.NoLongerServed, result!.Status);
        Assert.Equal("Moved away.", result.StatusNote);
    }

    [Fact]
    public async Task ACaseManagerCannotMarkAConsumerGhost()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var created = await CreateConsumerAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/people/{created.Id}/status",
            new SetPersonStatusRequest(PersonStatusRules.Ghost, null, created.Revision));
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty("status", out _));
    }

    [Fact]
    public async Task AnAdminCanMarkAConsumerGhost()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var created = await CreateConsumerAsync(admin);

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/people/{created.Id}/status",
            new SetPersonStatusRequest(PersonStatusRules.Ghost, "Not a real person.", created.Revision));
        var result = await response.Content.ReadFromJsonAsync<PersonStatusDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PersonStatusRules.Ghost, result!.Status);
    }

    // Same-agency, different caseload — the boundary a strict UserId-equality check draws.
    [Fact]
    public async Task ACaseManagerCannotChangeStatusOutsideTheirOwnCaseload()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var other = await factory.CreateAuthenticatedClientAsync("supervisee-of-demoted-one");
        var created = await CreateConsumerAsync(owner);

        var response = await other.PutAsJsonAsync(
            $"/api/v1/people/{created.Id}/status",
            new SetPersonStatusRequest(PersonStatusRules.NoLongerServed, null, created.Revision));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnArchivedConsumerIsAbsentFromTheCaseload()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var kept = await CreateConsumerAsync(client);
        var archived = await CreateConsumerAsync(client);

        await client.PutAsJsonAsync(
            $"/api/v1/people/{archived.Id}/status",
            new SetPersonStatusRequest(PersonStatusRules.Deceased, null, archived.Revision));

        var caseload = await client.GetFromJsonAsync<List<PersonDto>>(
            $"/api/v1/caseload?userId={kept.UserId}");

        Assert.DoesNotContain(caseload!, person => person.Id == archived.Id);
        Assert.Contains(caseload!, person => person.Id == kept.Id);
    }

    [Fact]
    public async Task AStaleRevisionIsRefused()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var created = await CreateConsumerAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/people/{created.Id}/status",
            new SetPersonStatusRequest(PersonStatusRules.NoLongerServed, null, created.Revision + 1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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
        "Status", "Test", new DateTime(1990, 4, 3), "Unknown", null,
        "A consumer created for status tests.", "None",
        null, null, null, null, false, false, null, null, null, null, null, null, null, null,
        null, false, false, false, false, false, false, 1, false, false, false,
        [], 0, true, false, null, null, false, false, "status-test@example.test");
}
