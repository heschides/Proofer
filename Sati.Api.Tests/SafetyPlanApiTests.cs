using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class SafetyPlanApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task SupervisorCannotApproveAnotherSupervisorsCaseload()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var plan = new ServerSafetyPlan { PersonId = 103, AuthorUserId = 19, CycleStart = new DateTime(2040, 1, 1), Status = "ReadyForReview", DocumentJson = CompleteDocument() };
        db.SafetyPlans.Add(plan); await db.SaveChangesAsync();
        try
        {
            var response = await client.PostAsJsonAsync($"/api/v1/safety-plans/{plan.Id}/approve", new ReviewSafetyPlanRequest(1));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await db.SafetyPlans.Where(x => x.Id == plan.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task NullSectionsAreRejectedWithoutServerFailure()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var response = await client.PutAsJsonAsync("/api/v1/safety-plans/1/document", new SaveSafetyPlanDocumentRequest("{\"schemaVersion\":1,\"sections\":null}", 1));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    internal static string CompleteDocument() => JsonSerializer.Serialize(new SafetyPlanDocument(1,
        SafetyPlanRules.SectionIds.Select(id => new SafetyPlanSection(id, "Synthetic test content.")).ToList()));

    [Fact]
    public async Task ReviewLocksContentRejectsStaleSaveAndRecordsApprovedSource()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var cycle = DateTime.Today.AddMonths(-2).Date;
        var person = new ServerPerson { UserId = 12, AgencyId = 1, FirstName = "Synthetic", LastName = "Safety", EffectiveDate = cycle };
        db.People.Add(person); await db.SaveChangesAsync();
        try
        {
            var started = await owner.PostAsJsonAsync($"/api/v1/people/{person.Id}/safety-plans/draft?authorUserId=12&cycleStart={cycle:yyyy-MM-dd}", new {});
            started.EnsureSuccessStatusCode();
            var plan = (await started.Content.ReadFromJsonAsync<SafetyPlanDto>())!;
            var saved = await owner.PutAsJsonAsync($"/api/v1/safety-plans/{plan.Id}/document", new SaveSafetyPlanDocumentRequest(CompleteDocument(), plan.Revision));
            saved.EnsureSuccessStatusCode();
            var stale = await owner.PutAsJsonAsync($"/api/v1/safety-plans/{plan.Id}/document", new SaveSafetyPlanDocumentRequest(CompleteDocument(), plan.Revision));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            plan = (await saved.Content.ReadFromJsonAsync<SafetyPlanDto>())!;
            var submitted = await owner.PostAsJsonAsync($"/api/v1/safety-plans/{plan.Id}/submit?authorUserId=12&expectedRevision={plan.Revision}", new {});
            submitted.EnsureSuccessStatusCode(); plan = (await submitted.Content.ReadFromJsonAsync<SafetyPlanDto>())!;
            Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"/api/v1/safety-plans/{plan.Id}/document", new SaveSafetyPlanDocumentRequest(CompleteDocument(), plan.Revision))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/v1/safety-plans/{plan.Id}/approve", new ReviewSafetyPlanRequest(plan.Revision))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await supervisor.GetAsync($"/api/v1/people/{person.Id}/safety-plans/latest?cycleStart={cycle:yyyy-MM-dd}")).StatusCode);
            var approved = await supervisor.PostAsJsonAsync($"/api/v1/safety-plans/{plan.Id}/approve", new ReviewSafetyPlanRequest(plan.Revision));
            approved.EnsureSuccessStatusCode();
            (await supervisor.PostAsJsonAsync($"/api/v1/people/{person.Id}/documents/SafetyPlan", new RenderAnnualDocumentRequest(cycle))).EnsureSuccessStatusCode();
            var artifact = await db.DocumentArtifacts.SingleAsync(x => x.PersonId == person.Id);
            Assert.Equal("GeneratedInSati", artifact.Origin); Assert.Equal(plan.Id, artifact.SourceContentId); Assert.Equal(plan.Version, artifact.SourceContentVersion);
            var revised = await owner.PostAsJsonAsync($"/api/v1/people/{person.Id}/safety-plans/draft?authorUserId=12&cycleStart={cycle:yyyy-MM-dd}", new {});
            revised.EnsureSuccessStatusCode();
            var revision = (await revised.Content.ReadFromJsonAsync<SafetyPlanDto>())!;
            Assert.NotEqual(plan.Id, revision.Id); Assert.Equal(plan.Version + 1, revision.Version);
            Assert.Equal("Approved", (await db.SafetyPlans.AsNoTracking().SingleAsync(x => x.Id == plan.Id)).Status);
        }
        finally
        {
            await db.DocumentArtifacts.Where(x => x.PersonId == person.Id).ExecuteDeleteAsync();
            await db.SafetyPlans.Where(x => x.PersonId == person.Id).ExecuteDeleteAsync();
            await db.People.Where(x => x.Id == person.Id).ExecuteDeleteAsync();
        }
    }
}
