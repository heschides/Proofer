using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// <c>POST /api/v1/admin/consumers/{id}/delete-in-window</c> — rule-3 deletion.
///
/// <para>
/// The billing-gate and window-boundary permutations are exhaustively covered against the local
/// implementation in <c>ConsumerDeletionInWindowTests</c>, which the API route mirrors closely.
/// These tests focus on what is specific to the API: route wiring, HTTP status codes, and tenant
/// isolation.
/// </para>
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class ConsumerDeletionInWindowApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task AFreshlyCreatedConsumerIsDeletable()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var person = await CreateConsumerAsync(admin);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var artifact = new ServerDocumentArtifact { PersonId = person.Id, AgencyId = 1, Kind = "PrivacyPractices",
                CycleStart = DateTime.Today, Origin = "GeneratedInSati", GeneratedAtUtc = DateTime.UtcNow,
                GeneratedByUserId = person.UserId, BlankFieldsJson = "[]" };
            db.DocumentArtifacts.Add(artifact);
            db.SafetyPlans.Add(new ServerSafetyPlan { PersonId = person.Id, AuthorUserId = person.UserId,
                CycleStart = DateTime.Today, DocumentJson = SafetyPlanRules.EmptyDocumentJson() });
            await db.SaveChangesAsync();
            db.DocumentAcknowledgments.Add(new ServerDocumentAcknowledgment { DocumentArtifactId = artifact.Id,
                RecordedByUserId = person.UserId, RecordedAtUtc = DateTime.UtcNow, ReceivedOn = DateTime.Today });
            await db.SaveChangesAsync();
        }

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/consumers/{person.Id}/delete-in-window",
            new DeleteConsumerInWindowRequest(
                person.Revision, ConsumerDeletionRules.ConsumerAttestation, "Created in error during a demo."));
        var result = await response.Content.ReadFromJsonAsync<ConsumerDeletionResultDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(person.Id, result!.PersonId);
        Assert.Equal(1, result.SafetyPlansDeleted);
        Assert.Equal(1, result.DocumentAcknowledgmentsDeleted);
        var caseload = await admin.GetFromJsonAsync<List<PersonDto>>($"/api/v1/caseload?userId={person.UserId}");
        Assert.DoesNotContain(caseload!, p => p.Id == person.Id);
    }

    [Fact]
    public async Task ACaseManagerCannotDelete()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var person = await CreateConsumerAsync(admin);

        var response = await caseManager.PostAsJsonAsync(
            $"/api/v1/admin/consumers/{person.Id}/delete-in-window",
            new DeleteConsumerInWindowRequest(
                person.Revision, ConsumerDeletionRules.ConsumerAttestation, "Attempted forgery."));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnAdminCannotDeleteAConsumerInAnotherAgency()
    {
        using var agencyOneAdmin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var agencyTwoCaseManager = await factory.CreateAuthenticatedClientAsync("case-manager-two");
        var foreignPerson = await CreateConsumerAsync(agencyTwoCaseManager);

        var response = await agencyOneAdmin.PostAsJsonAsync(
            $"/api/v1/admin/consumers/{foreignPerson.Id}/delete-in-window",
            new DeleteConsumerInWindowRequest(
                foreignPerson.Revision, ConsumerDeletionRules.ConsumerAttestation, "Wrong agency."));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AStaleRevisionIsRefused()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var person = await CreateConsumerAsync(admin);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/consumers/{person.Id}/delete-in-window",
            new DeleteConsumerInWindowRequest(
                person.Revision + 1, ConsumerDeletionRules.ConsumerAttestation, "Stale."));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AnInvalidAttestationIsRefused()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var person = await CreateConsumerAsync(admin);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/consumers/{person.Id}/delete-in-window",
            new DeleteConsumerInWindowRequest(person.Revision, "wrong-attestation", "Reason."));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // The test-data attestation must not unlock the broader rule-3 command — an older client
    // sending the wrong (narrower) attestation must not accidentally succeed at the wider one.
    [Fact]
    public async Task TheTestDataAttestationDoesNotSatisfyRuleThreeDeletion()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var person = await CreateConsumerAsync(admin);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/consumers/{person.Id}/delete-in-window",
            new DeleteConsumerInWindowRequest(
                person.Revision, TestDataDeletionRules.ConsumerAttestation, "Reason."));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AConsumerOutsideTheWindowIsRefused()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var person = await CreateConsumerAsync(admin);
        await BackdateCreatedAtAsync(person.Id, daysAgo: 20);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/consumers/{person.Id}/delete-in-window",
            new DeleteConsumerInWindowRequest(
                person.Revision, ConsumerDeletionRules.ConsumerAttestation, "Too late."));
        var problem = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("consumer_outside_deletion_window", problem!.Code);
    }

    [Fact]
    public async Task AConsumerUnderAnActiveLegalHoldIsRefused()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var person = await CreateConsumerAsync(admin);
        await admin.PostAsJsonAsync(
            "/api/v1/admin/legal-holds",
            new PlaceLegalHoldRequest(person.Id, "Program integrity review", null, null, DateTime.UtcNow));

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/consumers/{person.Id}/delete-in-window",
            new DeleteConsumerInWindowRequest(
                person.Revision, ConsumerDeletionRules.ConsumerAttestation, "Should be blocked."));
        var problem = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("consumer_legal_hold", problem!.Code);
        var caseload = await admin.GetFromJsonAsync<List<PersonDto>>($"/api/v1/caseload?userId={person.UserId}");
        Assert.Contains(caseload!, p => p.Id == person.Id);
    }

    // ---- Helpers ----

    private async Task BackdateCreatedAtAsync(int personId, int daysAgo)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE People SET CreatedAtUtc = {DateTime.UtcNow.AddDays(-daysAgo)} WHERE Id = {personId}");
    }

    private static async Task<PersonDto> CreateConsumerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/people",
            ValidRequest() with { LastName = Guid.NewGuid().ToString("N")[..10] });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PersonDto>())!;
    }

    private static SavePersonRequest ValidRequest() => new(
        "Deletable", "Test", new DateTime(1990, 4, 3), "Unknown", null,
        "A consumer created for rule-3 deletion tests.", "None",
        null, null, null, null, false, false, null, null, null, null, null, null, null, null,
        null, false, false, false, false, false, false, 1, false, false, false,
        [], 0, true, false, null, null, false, false, "deletion-window-test@example.test");
}
