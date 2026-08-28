using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// The authoritative path for a consumer's provider list. The tests that matter most are
/// the boundary ones: another caseload's consumer, another tenant's directory entry, and
/// the two rules that keep the list coherent.
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class ConsumerProviderApiTests(SatiApiFactory factory)
{
    private const int OwnedPersonId = 101;
    private const int OtherAgencyPersonId = 201;

    [Fact]
    public async Task ACaseManagerCannotReadAnotherAgencysConsumerProviderList()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.GetAsync($"/api/v1/people/{OtherAgencyPersonId}/providers");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ACaseManagerCannotAddAProviderToAnotherAgencysConsumer()
    {
        using var agencyOne = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var agencyTwo = await factory.CreateAuthenticatedClientAsync("case-manager-two");
        var clinician = await CreateProviderAsync("Cross Caseload Clinician");
        try
        {
            var response = await agencyOne.PostAsJsonAsync(
                $"/api/v1/people/{OtherAgencyPersonId}/providers", Link(clinician.Id));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await ListAsync(agencyTwo, OtherAgencyPersonId));
        }
        finally
        {
            await DeleteProviderAsync(clinician.Id);
        }
    }

    [Fact]
    public async Task ADirectoryEntryFromAnotherAgencyCannotBeLinkedToAConsumer()
    {
        // A real, correctly-tiered clinician. The only thing wrong with it is the tenant it
        // belongs to, and nothing in the request body says so.
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var adminTwo = await factory.CreateAuthenticatedClientAsync("admin-two");
        var foreign = await CreateProviderAsync("Agency Two Clinician", adminTwo);
        try
        {
            var response = await caseManager.PostAsJsonAsync(
                $"/api/v1/people/{OwnedPersonId}/providers", Link(foreign.Id));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("not in this agency's provider directory", body);
            Assert.DoesNotContain("Agency Two Clinician", body);
            Assert.Empty(await ListAsync(caseManager, OwnedPersonId));
        }
        finally
        {
            await DeleteProviderAsync(foreign.Id, adminTwo);
        }
    }

    [Fact]
    public async Task ASecondCurrentPrimaryCareProviderIsRefusedAndNamesTheFirst()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var first = await CreateProviderAsync("Primary Care Reed");
        var second = await CreateProviderAsync("Primary Care Okafor");
        var link = await AddAsync(client, OwnedPersonId, Link(first.Id, isPrimaryCare: true));
        try
        {
            var response = await client.PostAsJsonAsync(
                $"/api/v1/people/{OwnedPersonId}/providers", Link(second.Id, isPrimaryCare: true));
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("consumer_provider_primary_care", error?.Code);
            Assert.Contains("Primary Care Reed", error?.Message);
        }
        finally
        {
            await RemoveAsync(client, OwnedPersonId, link.Id);
            await DeleteProviderAsync(first.Id);
            await DeleteProviderAsync(second.Id);
        }
    }

    [Fact]
    public async Task TheSameProviderCannotAppearTwiceOnTheCurrentList()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var clinician = await CreateProviderAsync("Duplicate Guard Clinician");
        var link = await AddAsync(client, OwnedPersonId, Link(clinician.Id));
        try
        {
            var response = await client.PostAsJsonAsync(
                $"/api/v1/people/{OwnedPersonId}/providers", Link(clinician.Id));
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("consumer_provider_duplicate", error?.Code);
            Assert.Contains("Duplicate Guard Clinician", error?.Message);
        }
        finally
        {
            await RemoveAsync(client, OwnedPersonId, link.Id);
            await DeleteProviderAsync(clinician.Id);
        }
    }

    [Fact]
    public async Task EndingARelationshipKeepsTheRowAndFreesThePrimaryCareSlot()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var first = await CreateProviderAsync("Ending Reed");
        var second = await CreateProviderAsync("Ending Okafor");
        var link = await AddAsync(client, OwnedPersonId, Link(first.Id, isPrimaryCare: true));
        ConsumerProviderDto? replacement = null;
        try
        {
            var ended = await client.PutAsJsonAsync(
                $"/api/v1/people/{OwnedPersonId}/providers/{link.Id}",
                Link(first.Id, isPrimaryCare: true) with { EndDate = new DateTime(2026, 8, 1) });
            replacement = await AddAsync(
                client, OwnedPersonId, Link(second.Id, isPrimaryCare: true));

            Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
            var all = await ListAsync(client, OwnedPersonId);
            Assert.Equal(2, all.Count);
            // The ended row is still on the record rather than removed.
            Assert.Contains(all, row => row.Id == link.Id && row.EndDate is not null);
        }
        finally
        {
            if (replacement is not null)
                await RemoveAsync(client, OwnedPersonId, replacement.Id);
            await RemoveAsync(client, OwnedPersonId, link.Id);
            await DeleteProviderAsync(first.Id);
            await DeleteProviderAsync(second.Id);
        }
    }

    [Fact]
    public async Task AConsumerMayReturnToAProviderTheyPreviouslyLeft()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var clinician = await CreateProviderAsync("Returning Clinician");
        var first = await AddAsync(client, OwnedPersonId, Link(clinician.Id));
        ConsumerProviderDto? second = null;
        try
        {
            await client.PutAsJsonAsync(
                $"/api/v1/people/{OwnedPersonId}/providers/{first.Id}",
                Link(clinician.Id) with { EndDate = new DateTime(2026, 3, 1) });
            second = await AddAsync(client, OwnedPersonId, Link(clinician.Id));

            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(2, (await ListAsync(client, OwnedPersonId)).Count);
        }
        finally
        {
            if (second is not null)
                await RemoveAsync(client, OwnedPersonId, second.Id);
            await RemoveAsync(client, OwnedPersonId, first.Id);
            await DeleteProviderAsync(clinician.Id);
        }
    }

    [Fact]
    public async Task ALinkBelongingToAnotherConsumerIsNotReachableThroughYourOwn()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var clinician = await CreateProviderAsync("Scoped Link Clinician");
        var link = await AddAsync(client, OwnedPersonId, Link(clinician.Id));
        try
        {
            // Person 102 is on the same caseload, so this is not an ownership failure — it is
            // the row having to belong to the consumer named in the route.
            var response = await client.PutAsJsonAsync(
                $"/api/v1/people/102/providers/{link.Id}", Link(clinician.Id));
            var removal = await client.DeleteAsync($"/api/v1/people/102/providers/{link.Id}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, removal.StatusCode);
            Assert.Single(await ListAsync(client, OwnedPersonId));
        }
        finally
        {
            await RemoveAsync(client, OwnedPersonId, link.Id);
            await DeleteProviderAsync(clinician.Id);
        }
    }

    [Fact]
    public async Task AnInvalidRequestIsRejectedFieldByField()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/people/{OwnedPersonId}/providers",
            new SaveConsumerProviderRequest(0, new string('x', 200), false,
                new DateTime(2026, 5, 1), new DateTime(2026, 4, 1), false, -1));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("providerId", body);
        Assert.Contains("role", body);
        Assert.Contains("endDate", body);
        Assert.Contains("sortOrder", body);
    }

    [Fact]
    public async Task TheListRoundTripsWithThePrimaryCareProviderFirst()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var specialist = await CreateProviderAsync("RoundTrip Specialist");
        var primary = await CreateProviderAsync("RoundTrip Primary");
        var first = await AddAsync(client, OwnedPersonId, Link(specialist.Id, sortOrder: 0));
        var second = await AddAsync(
            client, OwnedPersonId, Link(primary.Id, isPrimaryCare: true, sortOrder: 9));
        try
        {
            var list = await ListAsync(client, OwnedPersonId);

            Assert.Equal(2, list.Count);
            Assert.Equal(primary.Id, list[0].ProviderId);
            Assert.True(list[0].IsPrimaryCare);
            Assert.Equal("Neurologist", list[1].Role);
        }
        finally
        {
            await RemoveAsync(client, OwnedPersonId, second.Id);
            await RemoveAsync(client, OwnedPersonId, first.Id);
            await DeleteProviderAsync(specialist.Id);
            await DeleteProviderAsync(primary.Id);
        }
    }

    [Fact]
    public async Task ADirectoryEntryAConsumerIsCurrentlySeeingCannotBeDeleted()
    {
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var clinician = await CreateProviderAsync("Referenced Clinician");
        var link = await AddAsync(caseManager, OwnedPersonId, Link(clinician.Id));
        try
        {
            // Deleting the clinician would leave the consumer's record pointing at nothing.
            // Refused explicitly rather than left to the foreign key, so the Admin gets a
            // sentence instead of a constraint violation.
            var response = await admin.DeleteAsync($"/api/v1/providers/{clinician.Id}");
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("provider_on_consumer_records", error?.Code);
            Assert.Contains("Referenced Clinician", error?.Message);
            // A count, never consumer names — the directory is not where who-sees-whom
            // is disclosed.
            Assert.Contains("1 consumer record", error?.Message);
            Assert.DoesNotContain("Person One", error?.Message);
            Assert.Single(await ListAsync(caseManager, OwnedPersonId));
        }
        finally
        {
            await RemoveAsync(caseManager, OwnedPersonId, link.Id);
            await DeleteProviderAsync(clinician.Id);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SaveConsumerProviderRequest Link(
        int providerId, bool isPrimaryCare = false, int sortOrder = 0) =>
        new(providerId, "Neurologist", isPrimaryCare, new DateTime(2026, 1, 1), null, true, sortOrder);

    private async Task<ProviderDto> CreateProviderAsync(string name, HttpClient? admin = null)
    {
        var owned = admin is null;
        var client = admin ?? await factory.CreateAuthenticatedClientAsync("admin-one");
        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/providers",
                new SaveProviderRequest("Healthcare", name, null, null, null, null, null, null,
                    0, false, null, null, null, null, null, "Individual"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ProviderDto>())!;
        }
        finally
        {
            if (owned) client.Dispose();
        }
    }

    private async Task DeleteProviderAsync(int providerId, HttpClient? admin = null)
    {
        var owned = admin is null;
        var client = admin ?? await factory.CreateAuthenticatedClientAsync("admin-one");
        try
        {
            await client.DeleteAsync($"/api/v1/providers/{providerId}");
        }
        finally
        {
            if (owned) client.Dispose();
        }
    }

    private static async Task<ConsumerProviderDto> AddAsync(
        HttpClient client, int personId, SaveConsumerProviderRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/people/{personId}/providers", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConsumerProviderDto>())!;
    }

    private static async Task<List<ConsumerProviderDto>> ListAsync(HttpClient client, int personId) =>
        (await client.GetFromJsonAsync<List<ConsumerProviderDto>>(
            $"/api/v1/people/{personId}/providers"))!;

    private static Task RemoveAsync(HttpClient client, int personId, int linkId) =>
        client.DeleteAsync($"/api/v1/people/{personId}/providers/{linkId}");
}
