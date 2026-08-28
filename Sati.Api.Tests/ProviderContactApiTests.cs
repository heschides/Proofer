using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class ProviderContactApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task CaseManagerCanCreateCorrectReadAndRemoveANamedContact()
    {
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var provider = await CreateProviderAsync(caseManager, "Contact CRUD");
        try
        {
            var create = await caseManager.PostAsJsonAsync(
                $"/api/v1/providers/{provider.Id}/contacts",
                new SaveProviderContactRequest(
                    "  Jamie Referral  ", "  Referrals  ", " 207-555-0100 ", " 42 ",
                    " jamie@example.test ", false, 2));
            var contact = await create.Content.ReadFromJsonAsync<ProviderContactDto>();

            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            Assert.Equal("Jamie Referral", contact?.Name);
            Assert.Equal("Referrals", contact?.Role);
            Assert.Equal("jamie@example.test", contact?.Email);

            var update = await caseManager.PutAsJsonAsync(
                $"/api/v1/providers/{provider.Id}/contacts/{contact!.Id}",
                new SaveProviderContactRequest(
                    "Jamie Referral", "Lead referrals", "207-555-0100", "42",
                    "jamie@example.test", true, 1));
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);

            var contacts = await caseManager.GetFromJsonAsync<List<ProviderContactDto>>(
                $"/api/v1/providers/{provider.Id}/contacts");
            var stored = Assert.Single(contacts!);
            Assert.Equal("Lead referrals", stored.Role);
            Assert.True(stored.IsPrimary);

            var removal = await caseManager.DeleteAsync(
                $"/api/v1/providers/{provider.Id}/contacts/{stored.Id}");
            Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
            Assert.Empty((await caseManager.GetFromJsonAsync<List<ProviderContactDto>>(
                $"/api/v1/providers/{provider.Id}/contacts"))!);
        }
        finally
        {
            await admin.DeleteAsync($"/api/v1/providers/{provider.Id}");
        }
    }

    [Fact]
    public async Task PromotingAContactDemotesTheOldPrimary()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var provider = await CreateProviderAsync(client, "Primary Contact");
        try
        {
            await client.PostAsJsonAsync(
                $"/api/v1/providers/{provider.Id}/contacts",
                Contact("First", isPrimary: true));
            await client.PostAsJsonAsync(
                $"/api/v1/providers/{provider.Id}/contacts",
                Contact("Second", isPrimary: true));

            var contacts = await client.GetFromJsonAsync<List<ProviderContactDto>>(
                $"/api/v1/providers/{provider.Id}/contacts");
            Assert.True(contacts!.Single(item => item.Name == "Second").IsPrimary);
            Assert.False(contacts.Single(item => item.Name == "First").IsPrimary);
        }
        finally
        {
            await admin.DeleteAsync($"/api/v1/providers/{provider.Id}");
        }
    }

    [Fact]
    public async Task InvalidContactReturnsTheSpecificFieldAndWritesNothing()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var provider = await CreateProviderAsync(client, "Contact Validation");
        try
        {
            var response = await client.PostAsJsonAsync(
                $"/api/v1/providers/{provider.Id}/contacts",
                Contact("Referral") with { Email = "not-an-email" });
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True(problem.GetProperty("errors").TryGetProperty("email", out _));
            Assert.Empty((await client.GetFromJsonAsync<List<ProviderContactDto>>(
                $"/api/v1/providers/{provider.Id}/contacts"))!);
        }
        finally
        {
            await admin.DeleteAsync($"/api/v1/providers/{provider.Id}");
        }
    }

    [Fact]
    public async Task AnotherAgencyCannotReadOrWriteProviderContacts()
    {
        using var agencyOne = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var agencyTwo = await factory.CreateAuthenticatedClientAsync("admin-two");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var provider = await CreateProviderAsync(agencyOne, "Tenant Contact");
        try
        {
            var read = await agencyTwo.GetAsync($"/api/v1/providers/{provider.Id}/contacts");
            var write = await agencyTwo.PostAsJsonAsync(
                $"/api/v1/providers/{provider.Id}/contacts", Contact("Foreign"));

            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        }
        finally
        {
            await admin.DeleteAsync($"/api/v1/providers/{provider.Id}");
        }
    }

    [Fact]
    public async Task ContactIdMustBelongToTheProviderNamedInTheRoute()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var first = await CreateProviderAsync(client, "Scoped Contact One");
        var second = await CreateProviderAsync(client, "Scoped Contact Two");
        try
        {
            var created = await client.PostAsJsonAsync(
                $"/api/v1/providers/{first.Id}/contacts", Contact("Keep me"));
            var contact = await created.Content.ReadFromJsonAsync<ProviderContactDto>();

            var removal = await client.DeleteAsync(
                $"/api/v1/providers/{second.Id}/contacts/{contact!.Id}");

            Assert.Equal(HttpStatusCode.NotFound, removal.StatusCode);
            Assert.Single((await client.GetFromJsonAsync<List<ProviderContactDto>>(
                $"/api/v1/providers/{first.Id}/contacts"))!);
        }
        finally
        {
            await admin.DeleteAsync($"/api/v1/providers/{first.Id}");
            await admin.DeleteAsync($"/api/v1/providers/{second.Id}");
        }
    }

    private static SaveProviderContactRequest Contact(string name, bool isPrimary = false) =>
        new(name, null, null, null, null, isPrimary, 0);

    private static async Task<ProviderDto> CreateProviderAsync(HttpClient client, string prefix)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/providers",
            new SaveProviderRequest(
                "Other", $"{prefix} {Guid.NewGuid():N}", null, null, null, null,
                null, null, 0, false, null, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProviderDto>())!;
    }
}
