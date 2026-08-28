using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// The authoritative path. The desktop repeats these rules, but the API is where they
/// have to hold — particularly that a parent belonging to another agency is refused, and
/// that an entry with affiliated entries beneath it cannot be deleted out from under them.
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class ProviderAffiliationApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task AParentBelongingToAnotherAgencyIsRefused()
    {
        using var agencyOne = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var agencyTwo = await factory.CreateAuthenticatedClientAsync("admin-two");
        var network = await CreateAsync(agencyOne, Medical("Agency One Health", "Network"));
        try
        {
            // A real, correctly-tiered network. The only thing wrong with it is the tenant
            // it belongs to, and nothing in the request body says so.
            var response = await agencyTwo.PostAsJsonAsync(
                "/api/v1/providers", Medical("Dr. Reed", "Individual", network.Id));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("not in this agency's provider directory", body);
            Assert.DoesNotContain("Agency One Health", body);
            Assert.Empty(await ListAsync(agencyTwo, "Dr. Reed"));
        }
        finally
        {
            await DeleteAsync(agencyOne, network.Id);
        }
    }

    [Fact]
    public async Task AnExistingEntryCannotBeRepointedAtAnotherAgencysParent()
    {
        using var agencyOne = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var agencyTwo = await factory.CreateAuthenticatedClientAsync("admin-two");
        var foreign = await CreateAsync(agencyOne, Medical("Agency One Health", "Network"));
        var practice = await CreateAsync(agencyTwo, Medical("Agency Two Practice", "Practice"));
        try
        {
            var response = await agencyTwo.PutAsJsonAsync(
                $"/api/v1/providers/{practice.Id}",
                Medical("Agency Two Practice", "Practice", foreign.Id));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var stored = Assert.Single(await ListAsync(agencyTwo, "Agency Two Practice"));
            Assert.Null(stored.ParentProviderId);
        }
        finally
        {
            await DeleteAsync(agencyTwo, practice.Id);
            await DeleteAsync(agencyOne, foreign.Id);
        }
    }

    [Fact]
    public async Task AnAffiliationLoopIsRefused()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var upper = await CreateAsync(client, Medical("Loop Upper", "Network"));
        var lower = await CreateAsync(client, Medical("Loop Lower", "Network", upper.Id));
        try
        {
            var response = await client.PutAsJsonAsync(
                $"/api/v1/providers/{upper.Id}", Medical("Loop Upper", "Network", lower.Id));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("already sits beneath this entry", body);
            var stored = Assert.Single(await ListAsync(client, "Loop Upper"));
            Assert.Null(stored.ParentProviderId);
        }
        finally
        {
            await DeleteAsync(client, lower.Id);
            await DeleteAsync(client, upper.Id);
        }
    }

    [Fact]
    public async Task AnEntryCannotBeItsOwnParent()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var network = await CreateAsync(client, Medical("Self Parent Network", "Network"));
        try
        {
            var response = await client.PutAsJsonAsync(
                $"/api/v1/providers/{network.Id}",
                Medical("Self Parent Network", "Network", network.Id));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("cannot be affiliated with itself", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await DeleteAsync(client, network.Id);
        }
    }

    [Fact]
    public async Task AnIllegalTierIsRefused()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var practice = await CreateAsync(client, Medical("Tier Practice", "Practice"));
        try
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/providers", Medical("Tier Network", "Network", practice.Id));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "A network can only be affiliated with another network",
                await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await DeleteAsync(client, practice.Id);
        }
    }

    [Fact]
    public async Task DeletingAnEntryWithAffiliatedEntriesBeneathItIsRefused()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var network = await CreateAsync(client, Medical("Delete Guard Network", "Network"));
        var practice = await CreateAsync(client, Medical("Delete Guard Practice", "Practice", network.Id));
        try
        {
            var response = await client.DeleteAsync($"/api/v1/providers/{network.Id}");
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("provider_has_affiliated_entries", error?.Code);
            Assert.Contains("Delete Guard Practice", error?.Message);
            Assert.Single(await ListAsync(client, "Delete Guard Network"));
        }
        finally
        {
            await DeleteAsync(client, practice.Id);
            await DeleteAsync(client, network.Id);
        }
    }

    [Fact]
    public async Task ARefusedDeleteDoesNotClearTheDefaultPassthroughSetting()
    {
        // The affiliation guard runs before the settings default is cleared, so a rejected
        // delete has to leave the rest of the agency's configuration untouched.
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var network = await CreateAsync(client, Medical("Settings Guard Network", "Network"));
        var practice = await CreateAsync(client, Medical("Settings Guard Practice", "Practice", network.Id));
        try
        {
            var response = await client.DeleteAsync($"/api/v1/providers/{network.Id}");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Single(await ListAsync(client, "Settings Guard Network"));
            Assert.Single(await ListAsync(client, "Settings Guard Practice"));
        }
        finally
        {
            await DeleteAsync(client, practice.Id);
            await DeleteAsync(client, network.Id);
        }
    }

    [Fact]
    public async Task AMedicalProviderWithoutADesignationIsRefused()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");

        var response = await client.PostAsJsonAsync(
            "/api/v1/providers",
            new SaveProviderRequest("Healthcare", "Undesignated Clinic", null, null, null, null,
                null, null, 0, false, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "individual, a practice, or a network",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUnrecognisedDesignationIsRefused()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");

        var response = await client.PostAsJsonAsync(
            "/api/v1/providers", Medical("Nonsense Tier", "Hospital"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("designation is invalid", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ANonMedicalProviderMayNotCarryADesignationOrAParent()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var network = await CreateAsync(client, Medical("Waiver Guard Network", "Network"));
        try
        {
            var designation = await client.PostAsJsonAsync(
                "/api/v1/providers",
                new SaveProviderRequest("Waiver", "Spurwink", null, null, null, null, null, null,
                    0, false, null, null, null, null, null, "Practice"));
            var parent = await client.PostAsJsonAsync(
                "/api/v1/providers",
                new SaveProviderRequest("Waiver", "Spurwink", null, null, null, null, null, null,
                    0, false, null, null, null, null, null, null, network.Id));

            Assert.Equal(HttpStatusCode.BadRequest, designation.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, parent.StatusCode);
        }
        finally
        {
            await DeleteAsync(client, network.Id);
        }
    }

    [Fact]
    public async Task AClientThatOmitsTheAffiliationFieldsStillSaves()
    {
        // The fields were added as optional trailing parameters, so a client built before
        // this slice keeps working and its entries simply stand alone.
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var response = await client.PostAsJsonAsync(
            "/api/v1/providers",
            new SaveProviderRequest("Other", "Legacy Client Entry", null, null, null, null,
                null, null, 0, false, null, null, null));
        var created = await response.Content.ReadFromJsonAsync<ProviderDto>();
        try
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(created?.MedicalKind);
            Assert.Null(created?.ParentProviderId);
        }
        finally
        {
            if (created is not null)
                await DeleteAsync(client, created.Id);
        }
    }

    [Fact]
    public async Task AFourLevelHierarchyRoundTripsThroughTheDirectory()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var top = await CreateAsync(client, Medical("RoundTrip MaineHealth", "Network"));
        var group = await CreateAsync(client, Medical("RoundTrip Partners", "Network", top.Id));
        var practice = await CreateAsync(client, Medical("RoundTrip Coastal", "Practice", group.Id));
        var clinician = await CreateAsync(client, Medical("RoundTrip Dr Reed", "Individual", practice.Id));
        try
        {
            var directory = (await client.GetFromJsonAsync<List<ProviderDto>>("/api/v1/providers"))!
                .Where(provider => provider.Name.StartsWith("RoundTrip", StringComparison.Ordinal))
                .Select(provider => new ProviderAffiliationNode(
                    provider.Id, provider.Name, provider.ParentProviderId,
                    Enum.Parse<MedicalProviderKind>(provider.MedicalKind!)))
                .ToList();

            Assert.Equal(
                "RoundTrip Coastal · RoundTrip Partners · RoundTrip MaineHealth",
                ProviderAffiliation.DescribeAffiliation(clinician.Id, directory));
        }
        finally
        {
            await DeleteAsync(client, clinician.Id);
            await DeleteAsync(client, practice.Id);
            await DeleteAsync(client, group.Id);
            await DeleteAsync(client, top.Id);
        }
    }

    [Fact]
    public async Task ACaseManagerMayAddAndCorrectDirectoryEntries()
    {
        // The directory is agency-wide, and it is only useful if the person on the phone with a
        // new specialist can record them straight away.
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        ProviderDto? created = null;
        try
        {
            var response = await caseManager.PostAsJsonAsync(
                "/api/v1/providers", Medical("Case Manager Added Network", "Network"));
            created = await response.Content.ReadFromJsonAsync<ProviderDto>();
            var edit = await caseManager.PutAsJsonAsync(
                $"/api/v1/providers/{created!.Id}", Medical("Case Manager Renamed Network", "Network"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        }
        finally
        {
            if (created is not null)
            {
                using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
                await admin.DeleteAsync($"/api/v1/providers/{created.Id}");
            }
        }
    }

    [Fact]
    public async Task ACaseManagerCannotRemoveADirectoryEntry()
    {
        // Removing reaches other case managers' consumers and is not undoable by whoever did it.
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var network = await CreateAsync(admin, Medical("Delete Permission Network", "Network"));
        try
        {
            var response = await caseManager.DeleteAsync($"/api/v1/providers/{network.Id}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Single(await ListAsync(admin, "Delete Permission Network"));
        }
        finally
        {
            await DeleteAsync(admin, network.Id);
        }
    }

    [Fact]
    public async Task ACaseManagerCannotMergeDirectoryEntries()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var surviving = await CreateAsync(admin, Medical("Merge Permission Keep", "Network"));
        var merged = await CreateAsync(admin, Medical("Merge Permission Drop", "Network"));
        try
        {
            var response = await caseManager.PostAsJsonAsync(
                $"/api/v1/providers/{surviving.Id}/merge", new MergeProvidersRequest(merged.Id));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Single(await ListAsync(admin, "Merge Permission Drop"));
        }
        finally
        {
            await DeleteAsync(admin, merged.Id);
            await DeleteAsync(admin, surviving.Id);
        }
    }

    [Fact]
    public async Task AdminMergeMovesAffiliationsConsumerLinksAndContactsAndWritesAudit()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var survivor = await CreateAsync(admin, Medical("Merge Keep Network", "Network"));
        var merged = await CreateAsync(admin, Medical("Merge Remove Network", "Network"));
        var child = await CreateAsync(admin, Medical("Merge Child Practice", "Practice", merged.Id));
        ConsumerProviderDto? link = null;
        try
        {
            var linkResponse = await caseManager.PostAsJsonAsync(
                "/api/v1/people/101/providers",
                new SaveConsumerProviderRequest(
                    merged.Id, "Specialist", false, null, null, true, 0));
            link = await linkResponse.Content.ReadFromJsonAsync<ConsumerProviderDto>();
            Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);

            var contactResponse = await admin.PostAsJsonAsync(
                $"/api/v1/providers/{merged.Id}/contacts",
                new SaveProviderContactRequest(
                    "Referral coordinator", "Referrals", "207-555-0100", null,
                    "referrals@example.test", true, 0));
            Assert.Equal(HttpStatusCode.OK, contactResponse.StatusCode);

            var response = await admin.PostAsJsonAsync(
                $"/api/v1/providers/{survivor.Id}/merge",
                new MergeProvidersRequest(merged.Id));
            var result = await response.Content.ReadFromJsonAsync<MergeProvidersResultDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("1 affiliated entry", result?.Summary);
            Assert.Contains("1 consumer link", result?.Summary);
            Assert.Contains("1 contact", result?.Summary);
            var directory = await admin.GetFromJsonAsync<List<ProviderDto>>("/api/v1/providers");
            Assert.DoesNotContain(directory!, provider => provider.Id == merged.Id);
            Assert.Equal(survivor.Id,
                directory!.Single(provider => provider.Id == child.Id).ParentProviderId);
            var links = await caseManager.GetFromJsonAsync<List<ConsumerProviderDto>>(
                "/api/v1/people/101/providers");
            Assert.Equal(survivor.Id, links!.Single(item => item.Id == link!.Id).ProviderId);
            var contacts = await admin.GetFromJsonAsync<List<ProviderContactDto>>(
                $"/api/v1/providers/{survivor.Id}/contacts");
            Assert.Equal("Referral coordinator", Assert.Single(contacts!).Name);

            var audit = Assert.Single((await factory.GetAuditEventsAsync("provider.merged"))
                .Where(item => item.ResourceId == survivor.Id.ToString()));
            Assert.Contains($"\"mergedProviderId\":{merged.Id}", audit.MetadataJson);
            Assert.DoesNotContain("Merge Remove Network", audit.MetadataJson);
        }
        finally
        {
            if (link is not null)
                await caseManager.DeleteAsync($"/api/v1/people/101/providers/{link.Id}");
            await DeleteAsync(admin, child.Id);
            await DeleteAsync(admin, merged.Id);
            await DeleteAsync(admin, survivor.Id);
        }
    }

    [Fact]
    public async Task MergeThatWouldCreateALoopIsRefusedWithoutMovingAnything()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var parent = await CreateAsync(admin, Medical("Merge Loop Parent", "Network"));
        var child = await CreateAsync(admin, Medical("Merge Loop Child", "Network", parent.Id));
        try
        {
            var response = await admin.PostAsJsonAsync(
                $"/api/v1/providers/{child.Id}/merge",
                new MergeProvidersRequest(parent.Id));
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("provider_merge_loop", error?.Code);
            var directory = await admin.GetFromJsonAsync<List<ProviderDto>>("/api/v1/providers");
            Assert.Equal(parent.Id,
                directory!.Single(provider => provider.Id == child.Id).ParentProviderId);
            Assert.Contains(directory, provider => provider.Id == parent.Id);
        }
        finally
        {
            await DeleteAsync(admin, child.Id);
            await DeleteAsync(admin, parent.Id);
        }
    }

    [Fact]
    public async Task MergeRefusesWhenAConsumerHasCurrentLinksToBothEntries()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var survivor = await CreateAsync(admin, Medical("Merge Links Keep", "Individual"));
        var merged = await CreateAsync(admin, Medical("Merge Links Remove", "Individual"));
        var first = await AddConsumerLinkAsync(caseManager, survivor.Id);
        var second = await AddConsumerLinkAsync(caseManager, merged.Id);
        try
        {
            var response = await admin.PostAsJsonAsync(
                $"/api/v1/providers/{survivor.Id}/merge",
                new MergeProvidersRequest(merged.Id));
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("provider_merge_consumer_link_conflict", error?.Code);
            Assert.Contains("current links to both entries", error?.Message);
            Assert.DoesNotContain("River", error?.Message);
            Assert.Single(await ListAsync(admin, "Merge Links Remove"));
        }
        finally
        {
            await caseManager.DeleteAsync($"/api/v1/people/101/providers/{first.Id}");
            await caseManager.DeleteAsync($"/api/v1/people/101/providers/{second.Id}");
            await DeleteAsync(admin, merged.Id);
            await DeleteAsync(admin, survivor.Id);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SaveProviderRequest Medical(string name, string kind, int? parentId = null) =>
        new("Healthcare", name, null, null, null, null, null, null, 0, false, null, null, null,
            null, null, kind, parentId);

    private static async Task<ProviderDto> CreateAsync(HttpClient client, SaveProviderRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/providers", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProviderDto>())!;
    }

    private static async Task<List<ProviderDto>> ListAsync(HttpClient client, string name) =>
        (await client.GetFromJsonAsync<List<ProviderDto>>("/api/v1/providers"))!
            .Where(provider => provider.Name == name)
            .ToList();

    private static async Task<ConsumerProviderDto> AddConsumerLinkAsync(
        HttpClient client,
        int providerId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/people/101/providers",
            new SaveConsumerProviderRequest(providerId, null, false, null, null, false, 0));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConsumerProviderDto>())!;
    }

    private static async Task DeleteAsync(HttpClient client, int id) =>
        await client.DeleteAsync($"/api/v1/providers/{id}");
}
