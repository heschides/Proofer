using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// <c>POST /api/v1/people/credible-matches</c> — the dedupe check behind bulk import.
///
/// <para>
/// It answers "has this Credible id already been imported into this agency?" and deliberately
/// very little else. The interesting tests are the two boundaries: the agency edge, which must
/// hold absolutely, and the caseload edge, which governs whether the caller is told <i>whose</i>
/// consumer it is.
/// </para>
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class CredibleMatchApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task AnIdTheAgencyHoldsIsReported()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var id = await CreateConsumerWithCredibleIdAsync(client);

        var result = await LookupAsync(client, [id, "no-such-id"]);

        var match = Assert.Single(result.CredibleClientMatches);
        Assert.Equal(id, match.CredibleClientId);
    }

    [Fact]
    public async Task AnIdNobodyHoldsIsNotReported()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        var result = await LookupAsync(client, ["definitely-not-imported"]);

        Assert.Empty(result.CredibleClientMatches);
    }

    // Tenant isolation. Agency two's consumer must be invisible to agency one even though the
    // caller is asking about an id they legitimately hold in their own Credible instance —
    // Credible ids collide across agencies because they are separate installations.
    [Fact]
    public async Task AnIdHeldByAnotherAgencyIsNotReported()
    {
        using var agencyOne = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var agencyTwo = await factory.CreateAuthenticatedClientAsync("case-manager-two");
        var id = await CreateConsumerWithCredibleIdAsync(agencyTwo);

        var result = await LookupAsync(agencyOne, [id]);

        Assert.Empty(result.CredibleClientMatches);
    }

    // The caller's own consumer, so the owner is theirs to know about.
    [Fact]
    public async Task TheOwnerIsNamedWhenTheCallerCouldAlreadySeeThatCaseload()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var id = await CreateConsumerWithCredibleIdAsync(client);

        var match = Assert.Single((await LookupAsync(client, [id])).CredibleClientMatches);

        Assert.Equal("case-manager-one", match.OwnerDisplayName);
    }

    // A supervisor onboarding a team needs to know a consumer is already on one of their case
    // managers' caseloads — that is the duplicate a caseload-scoped check would miss entirely.
    [Fact]
    public async Task ASupervisorIsToldWhichOfTheirCaseManagersHoldsIt()
    {
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var id = await CreateConsumerWithCredibleIdAsync(caseManager);

        var match = Assert.Single((await LookupAsync(supervisor, [id])).CredibleClientMatches);

        Assert.Equal("case-manager-one", match.OwnerDisplayName);
    }

    // The disclosure boundary. An ordinary case manager learns the id is taken — which is all
    // dedupe needs — without learning whose consumer it is. Naming the owner here would turn a
    // dedupe check into a way to enumerate other people's caseloads.
    [Fact]
    public async Task ACaseManagerIsNotToldWhoHoldsAConsumerOutsideTheirCaseload()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        // supervisee-of-demoted-one: a real case manager in the same agency who neither owns
        // this consumer nor supervises anyone. Deliberately not stale-badge-user, whose badge
        // another test invalidates — that actor 401s and would prove nothing about disclosure.
        using var other = await factory.CreateAuthenticatedClientAsync("supervisee-of-demoted-one");
        var id = await CreateConsumerWithCredibleIdAsync(owner);

        var match = Assert.Single((await LookupAsync(other, [id])).CredibleClientMatches);

        Assert.Equal(id, match.CredibleClientId);
        Assert.Null(match.OwnerDisplayName);
    }

    [Fact]
    public async Task AnEmptyRequestAsksTheDatabaseNothing()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        var result = await LookupAsync(client, []);

        Assert.Empty(result.CredibleClientMatches);
        Assert.Empty(result.MaineCareIdMatches);
        Assert.Empty(result.NameBirthDateMatches);
    }

    // A runaway guard on a caller-supplied list. Bulk import chunks its lookups well below this.
    [Fact]
    public async Task AnOversizedRequestIsRefused()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var ids = Enumerable.Range(0, 501).Select(index => $"id-{index}").ToList();

        var response = await client.PostAsJsonAsync(
            "/api/v1/people/credible-matches", new CredibleClientLookupRequest(ids));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ACallerWithoutCaseManagementIsRefused()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("billing-only-one");

        var response = await client.PostAsJsonAsync(
            "/api/v1/people/credible-matches",
            new CredibleClientLookupRequest(["anything"]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // A consumer hand-entered before Credible import existed has no Credible id on file at all —
    // the id-only tier cannot see them. MaineCare id is the second tier for exactly this case.
    [Fact]
    public async Task AConsumerWithNoCredibleIdIsStillFoundByMaineCareId()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var maineCareId = $"mc-{Guid.NewGuid():N}"[..15];
        await CreateConsumerWithoutCredibleIdAsync(client, maineCareId: maineCareId);

        var result = await LookupAsync(client, [], maineCareIds: [maineCareId]);

        var match = Assert.Single(result.MaineCareIdMatches);
        Assert.Equal(maineCareId, match.MaineCareId);
        Assert.Empty(result.CredibleClientMatches);
    }

    // Third tier: neither a Credible id nor a MaineCare id on file, only a name and birth date.
    [Fact]
    public async Task AConsumerWithOnlyANameAndBirthDateIsStillFound()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var lastName = Guid.NewGuid().ToString("N")[..10];
        await CreateConsumerWithoutCredibleIdAsync(client, lastName: lastName);

        var result = await LookupAsync(client, [],
            nameBirthDates: [new PersonNameBirthDate(lastName, "Credible", new DateTime(1990, 4, 3))]);

        var match = Assert.Single(result.NameBirthDateMatches);
        Assert.Equal(lastName, match.NameBirthDate.LastName);
    }

    // Tenant isolation must hold on every tier, not only the Credible-id one.
    [Fact]
    public async Task AMaineCareIdHeldByAnotherAgencyIsNotReported()
    {
        using var agencyOne = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var agencyTwo = await factory.CreateAuthenticatedClientAsync("case-manager-two");
        var maineCareId = $"mc-{Guid.NewGuid():N}"[..15];
        await CreateConsumerWithoutCredibleIdAsync(agencyTwo, maineCareId: maineCareId);

        var result = await LookupAsync(agencyOne, [], maineCareIds: [maineCareId]);

        Assert.Empty(result.MaineCareIdMatches);
    }

    // ---- Helpers ----

    private static async Task<CredibleMatchLookupResult> LookupAsync(
        HttpClient client,
        IReadOnlyList<string> ids,
        IReadOnlyList<string>? maineCareIds = null,
        IReadOnlyList<PersonNameBirthDate>? nameBirthDates = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/people/credible-matches",
            new CredibleClientLookupRequest(ids, maineCareIds, nameBirthDates));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CredibleMatchLookupResult>())!;
    }

    private static async Task<string> CreateConsumerWithCredibleIdAsync(HttpClient client)
    {
        var id = $"cred-{Guid.NewGuid():N}"[..20];
        var response = await client.PostAsJsonAsync(
            "/api/v1/people",
            ValidRequest() with
            {
                LastName = Guid.NewGuid().ToString("N")[..10],
                CredibleClientId = id
            });
        response.EnsureSuccessStatusCode();
        return id;
    }

    private static async Task CreateConsumerWithoutCredibleIdAsync(
        HttpClient client, string? maineCareId = null, string? lastName = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/people",
            ValidRequest() with
            {
                LastName = lastName ?? Guid.NewGuid().ToString("N")[..10],
                MaineCareId = maineCareId,
                CredibleClientId = null
            });
        response.EnsureSuccessStatusCode();
    }

    private static SavePersonRequest ValidRequest() => new(
        "Credible", "Match", new DateTime(1990, 4, 3), "Unknown", null,
        "A consumer created for Credible dedupe tests.", "None",
        null, null, null, null, false, false, null, null, null, null, null, null, null, null,
        null, false, false, false, false, false, false, 1, false, false, false,
        [], 0, true, false, null, null, false, false, "credible@example.test");
}
