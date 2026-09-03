using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class DocumentTemplateApiTests(SatiApiFactory factory)
{
    [Theory]
    [InlineData("GET", 1, "case-manager-one", HttpStatusCode.Forbidden)]
    [InlineData("POST", 1, "case-manager-one", HttpStatusCode.Forbidden)]
    [InlineData("GET", 2, "admin-one", HttpStatusCode.NotFound)]
    [InlineData("POST", 2, "admin-one", HttpStatusCode.NotFound)]
    public async Task TemplateRoutesEnforceAdministrationAndAgency(
        string method, int agencyId, string username, HttpStatusCode expected)
    {
        using var client = await factory.CreateAuthenticatedClientAsync(username);
        var route = $"/api/v1/agencies/{agencyId}/templates/PrivacyPractices";
        var response = method == "GET"
            ? await client.GetAsync(route)
            : await client.PostAsJsonAsync(route, new PublishDocumentTemplateRequest("# Test\n{{agency.name}}"));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task UnknownTokenIsRejectedBeforePublication()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var response = await client.PostAsJsonAsync("/api/v1/agencies/1/templates/PrivacyPractices",
            new PublishDocumentTemplateRequest("# Notice\n{{consumer.ssn}}"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PublishedTemplateCannotBeEditedInPlace()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var template = await db.DocumentTemplates.SingleAsync(t => t.Id == 1);
        template.Body = "Changed without publication";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ArtifactKeepsItsTemplateVersionAfterNewPublication()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await db.DocumentTemplates.Where(t => t.AgencyId == 1).ExecuteDeleteAsync();
        await factory.DeleteDocumentArtifactsAsync(101, AnnualDocumentKind.PrivacyPractices);
        try
        {
            var firstResponse = await admin.PostAsJsonAsync("/api/v1/agencies/1/templates/PrivacyPractices",
                new PublishDocumentTemplateRequest("# First notice\n{{agency.name}} for {{consumer.full_name}}"));
            firstResponse.EnsureSuccessStatusCode();
            var first = (await firstResponse.Content.ReadFromJsonAsync<DocumentTemplateDto>())!;
            (await owner.PostAsJsonAsync("/api/v1/people/101/documents/PrivacyPractices",
                new RenderAnnualDocumentRequest())).EnsureSuccessStatusCode();
            var artifact = await db.DocumentArtifacts.AsNoTracking().SingleAsync(a =>
                a.PersonId == 101 && a.Kind == "PrivacyPractices" && a.SupersededByArtifactId == null);

            var secondResponse = await admin.PostAsJsonAsync("/api/v1/agencies/1/templates/PrivacyPractices",
                new PublishDocumentTemplateRequest("# Second notice\nUpdated wording for {{agency.name}}"));
            secondResponse.EnsureSuccessStatusCode();
            var second = (await secondResponse.Content.ReadFromJsonAsync<DocumentTemplateDto>())!;
            Assert.Equal(first.Version + 1, second.Version);
            var unchanged = await db.DocumentArtifacts.AsNoTracking().SingleAsync(a => a.Id == artifact.Id);
            Assert.Equal(first.Version, unchanged.TemplateVersion);
            Assert.Equal("Agency", unchanged.TemplateOwner);
            Assert.Equal("PrivacyPractices", unchanged.TemplateKey);
            Assert.Equal(first.Body, (await db.DocumentTemplates.AsNoTracking().SingleAsync(t => t.Id == first.Id)).Body);

            (await owner.PostAsJsonAsync("/api/v1/people/101/documents/PrivacyPractices",
                new RenderAnnualDocumentRequest())).EnsureSuccessStatusCode();
            var live = await db.DocumentArtifacts.AsNoTracking().SingleAsync(a =>
                a.PersonId == 101 && a.Kind == "PrivacyPractices" && a.SupersededByArtifactId == null);
            Assert.Equal(second.Version, live.TemplateVersion);
        }
        finally
        {
            await factory.DeleteDocumentArtifactsAsync(101, AnnualDocumentKind.PrivacyPractices);
            await db.DocumentTemplates.Where(t => t.AgencyId == 1).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task GeneratedPrivacyNoticeDoesNotInventAcknowledgment()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var formId = await factory.CreateOutstandingFormAsync(101, "PrivacyPractices");
        await factory.DeleteDocumentArtifactsAsync(101, AnnualDocumentKind.PrivacyPractices);
        try
        {
            (await client.PostAsJsonAsync("/api/v1/people/101/documents/PrivacyPractices",
                new RenderAnnualDocumentRequest())).EnsureSuccessStatusCode();
            var response = await client.PostAsJsonAsync("/api/v1/people/101/forms/PrivacyPractices/attestation",
                new AttestFormRequest(formId, DateTime.Today));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }
        finally
        {
            await factory.DeleteDocumentArtifactsAsync(101, AnnualDocumentKind.PrivacyPractices);
        }
    }
}
