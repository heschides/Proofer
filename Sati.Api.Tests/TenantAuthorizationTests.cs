using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

public sealed class TenantAuthorizationTests : IClassFixture<SatiApiFactory>
{
    private readonly SatiApiFactory _factory;

    public TenantAuthorizationTests(SatiApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ProtectedEndpointRejectsAnonymousRequest()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/providers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpointRejectsABadgeAfterTheUsersRoleChanges()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("stale-badge-user");
        await _factory.ChangeUserRoleAsync(14, "Supervisor");

        var response = await client.GetAsync("/api/v1/providers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProviderReadReturnsOnlyTheActorsAgency()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var providers = await client.GetFromJsonAsync<List<ProviderDto>>("/api/v1/providers");

        var provider = Assert.Single(providers!);
        Assert.Equal(301, provider.Id);
        Assert.Equal("Provider One", provider.Name);
    }

    [Fact]
    public async Task AdministratorCannotModifyAnotherAgencysProvider()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var request = ProviderRequest("Attempted cross-agency update");

        var response = await client.PutAsJsonAsync("/api/v1/providers/401", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var otherAgencyClient = await _factory.CreateAuthenticatedClientAsync("admin-two");
        var providers = await otherAgencyClient.GetFromJsonAsync<List<ProviderDto>>("/api/v1/providers");
        Assert.Equal("Provider Two", Assert.Single(providers!).Name);
    }

    [Fact]
    public async Task CaseManagerCannotCreateProvider()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.PostAsJsonAsync("/api/v1/providers", ProviderRequest("New provider"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PersonJournalFromAnotherAgencyIsHidden()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.GetAsync("/api/v1/people/201/journal");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PersonJournalFromAnotherAgencyCannotBeChanged()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.PutAsJsonAsync(
            "/api/v1/people/201/journal",
            new SaveJournalRequest("Attempted cross-agency change"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var ownerClient = await _factory.CreateAuthenticatedClientAsync("case-manager-two");
        var journal = await ownerClient.GetFromJsonAsync<string>("/api/v1/people/201/journal");
        Assert.Equal("Agency two journal", journal);
    }

    [Fact]
    public async Task AdministratorCannotResetAnotherAgencysPassword()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/21/password",
            new ResetPasswordRequest("Replacement-Password-42!"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var unchangedAccount = await _factory.CreateAuthenticatedClientAsync("admin-two");
        Assert.NotNull(unchangedAccount.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task BillingLossReportContainsOnlyTheCaseManagersOwnConsumers()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var report = await client.GetFromJsonAsync<ConsumerBillingLossReportDto>(
            "/api/v1/reports/consumer-billing-loss?start=2026-07-01&end=2026-07-31");

        Assert.NotEmpty(report!.Consumers);
        Assert.Contains(report.Consumers, consumer => consumer.PersonId == 101);
        Assert.DoesNotContain(report.Consumers, consumer => consumer.PersonId == 201);
    }

    [Fact]
    public async Task AdministratorCannotGenerateAnotherAgencysBillingFile()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var response = await client.PostAsJsonAsync(
            "/api/v1/billing/periods/1201/edi",
            new GenerateEdiRequest(true));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BillingFileRejectsAForeignAgencySourceEvenInsideAnOwnedPeriod()
    {
        await _factory.AddForeignAgencyNoteToBillingPeriodAsync(1101, 602);
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var response = await client.PostAsJsonAsync(
            "/api/v1/billing/periods/1101/edi",
            new GenerateEdiRequest(true));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RepeatingAClaimLineCommandCannotCreateDuplicateBilling()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var request = new CreateClaimLineRequest(502, false, null);

        var first = await client.PostAsJsonAsync("/api/v1/billing/claim-lines", request);
        var duplicate = await client.PostAsJsonAsync("/api/v1/billing/claim-lines", request);
        var auditEvents = await _factory.GetAuditEventsAsync("billing-claim-line.created");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Single(auditEvents, candidate => candidate.ResourceId == "502");
    }

    [Theory]
    [InlineData("/api/v1/at-requests/1001")]
    [InlineData("/api/v1/at-requests/1001/snapshot")]
    public async Task AnotherAgencysAtRequestAndGeneratedSnapshotAreHidden(string path)
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnotherAgencysAssessmentCannotBeChanged()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.PutAsJsonAsync(
            "/api/v1/assessments/801/document",
            new SaveAssessmentDocumentRequest("{\"attempted\":\"change\"}", 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SupervisorCannotAuthorACaseManagersAssessment()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("supervisor-one");

        var editResponse = await client.PutAsJsonAsync(
            "/api/v1/assessments/701/document",
            new SaveAssessmentDocumentRequest("{\"writtenBy\":\"supervisor\"}", 1));
        var createResponse = await client.PostAsync(
            "/api/v1/people/101/assessments/draft?authorUserId=12",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, editResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, createResponse.StatusCode);
    }

    [Fact]
    public async Task CaseManagerCanStillAuthorTheirOwnAssessment()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var revision = await _factory.GetAssessmentRevisionAsync(701);

        var response = await client.PutAsJsonAsync(
            "/api/v1/assessments/701/document",
            new SaveAssessmentDocumentRequest("{\"writtenBy\":\"case-manager\"}", revision));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SupervisorCannotActOnAnotherAgencysNote()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("supervisor-one");

        var response = await client.PostAsJsonAsync(
            "/api/v1/supervisor/notes/601/return",
            new SupervisorNoteActionRequest("This must remain inside Agency Two."));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SuccessfulAssessmentChangeCreatesAPhiMinimizedAuditEvent()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var revision = await _factory.GetAssessmentRevisionAsync(701);

        var response = await client.PutAsJsonAsync(
            "/api/v1/assessments/701/document",
            new SaveAssessmentDocumentRequest(
                "{\"privateNarrative\":\"must not enter audit\"}",
                revision));
        var events = await _factory.GetAuditEventsAsync("assessment.updated");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auditEvent = Assert.Single(events.TakeLast(1));
        Assert.Equal(1, auditEvent.AgencyId);
        Assert.Equal(12, auditEvent.ActorUserId);
        Assert.Equal("Assessment", auditEvent.ResourceType);
        Assert.False(string.IsNullOrWhiteSpace(auditEvent.CorrelationId));
        Assert.Equal("{}", auditEvent.MetadataJson);
    }

    [Fact]
    public async Task StaleAssessmentSaveIsRejectedWithoutErasingTheNewerCopy()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var firstResponse = await client.PutAsJsonAsync(
            "/api/v1/assessments/702/document",
            new SaveAssessmentDocumentRequest("{\"copy\":\"newer\"}", 1));
        var staleResponse = await client.PutAsJsonAsync(
            "/api/v1/assessments/702/document",
            new SaveAssessmentDocumentRequest("{\"copy\":\"stale\"}", 1));
        var storedRevision = await _factory.GetAssessmentRevisionAsync(702);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var updated = await firstResponse.Content.ReadFromJsonAsync<ComprehensiveAssessmentDto>();
        Assert.Equal(2, updated!.Revision);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal(2, storedRevision);
    }

    [Fact]
    public async Task RejectedCrossAgencyActionDoesNotCreateASuccessAuditEvent()
    {
        var before = await _factory.GetAuditEventsAsync("note.returned");
        using var client = await _factory.CreateAuthenticatedClientAsync("supervisor-one");

        var response = await client.PostAsJsonAsync(
            "/api/v1/supervisor/notes/601/return",
            new SupervisorNoteActionRequest("Cross-agency attempt"));
        var after = await _factory.GetAuditEventsAsync("note.returned");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before.Count, after.Count);
    }

    [Fact]
    public async Task AuditEventsCannotBeChangedThroughTheApplicationContext()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");

        await Assert.ThrowsAsync<InvalidOperationException>(
            _factory.TryToModifyFirstAuditEventAsync);
    }

    [Fact]
    public async Task AdministratorReadsOnlyTheirAgencysAuditEvents()
    {
        using var otherAgencyClient = await _factory.CreateAuthenticatedClientAsync("admin-two");
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var events = await client.GetFromJsonAsync<List<AuditEventDto>>(
            "/api/v1/audit-events?take=500");

        Assert.NotEmpty(events!);
        Assert.All(events!, auditEvent => Assert.Equal(1, auditEvent.AgencyId));
        Assert.DoesNotContain(events!, auditEvent => auditEvent.ActorUserId == 21);
    }

    [Fact]
    public async Task CaseManagerCannotReadTheAgencyAuditTrail()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.GetAsync("/api/v1/audit-events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdministratorDashboardIsAgencyScoped()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var overview = await client.GetFromJsonAsync<AdminOverviewDto>("/api/v1/admin/overview");
        var people = await client.GetFromJsonAsync<List<AdminPersonListItemDto>>("/api/v1/admin/people");
        var activity = await client.GetFromJsonAsync<List<AdminActivityDto>>("/api/v1/admin/activity?days=30&take=500");

        Assert.NotNull(overview);
        Assert.Equal(1, overview.AgencyId);
        Assert.Equal("Agency One", overview.AgencyName);
        Assert.Equal(4, overview.UserCount);
        Assert.Equal(2, overview.PersonCount);
        Assert.Equal(1, overview.NotesThisMonth);
        Assert.NotEmpty(people!);
        Assert.All(people!, person => Assert.DoesNotContain("Two", person.DisplayName));
        Assert.Contains(people!, person => person.PersonId == 101);
        Assert.DoesNotContain(people!, person => person.PersonId == 201);
        Assert.NotEmpty(activity!);
        Assert.All(activity!, item => Assert.NotEqual(21, item.ActorUserId));
    }

    [Theory]
    [InlineData("/api/v1/admin/overview")]
    [InlineData("/api/v1/admin/people")]
    [InlineData("/api/v1/admin/activity")]
    public async Task CaseManagerCannotOpenAdministratorDashboard(string path)
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PersonLifecyclePreservesChangesRejectsStaleWritesAndGeneratesAuditorPdf()
    {
        using var caseManager = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var caseload = await caseManager.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var original = Assert.Single(caseload!, person => person.Id == 102);
        var update = PersonRequest(original, "Updated", "Revised lifecycle biography.");

        var updateResponse = await caseManager.PutAsJsonAsync("/api/v1/people/102", update);
        var updated = await updateResponse.Content.ReadFromJsonAsync<PersonDto>();
        var staleResponse = await caseManager.PutAsJsonAsync("/api/v1/people/102", update);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(2, updated!.Revision);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        var forbiddenHistory = await caseManager.GetAsync("/api/v1/people/102/history");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenHistory.StatusCode);

        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var historyResponse = await admin.GetAsync("/api/v1/people/102/history");
        var history = await historyResponse.Content.ReadFromJsonAsync<List<PersonVersionDto>>();
        Assert.Contains("no-store", historyResponse.Headers.CacheControl?.ToString());
        Assert.Equal(2, history!.Count);
        Assert.Equal("TrackingBaseline", history[0].ChangeKind);
        Assert.Equal("Updated", history[1].ChangeKind);
        Assert.Equal(12, history[1].ActorUserId);
        var firstName = Assert.Single(history[1].Changes, change => change.Field == "firstName");
        Assert.Equal("Lifecycle", firstName.PreviousValue);
        Assert.Equal("Updated", firstName.NewValue);
        var biography = Assert.Single(history[1].Changes, change => change.Field == "bio");
        Assert.Equal("Initial lifecycle biography.", biography.PreviousValue);
        Assert.Equal("Revised lifecycle biography.", biography.NewValue);

        var crossAgency = await admin.GetAsync("/api/v1/people/201/history");
        Assert.Equal(HttpStatusCode.NotFound, crossAgency.StatusCode);

        var pdfResponse = await admin.GetAsync("/api/v1/people/102/history.pdf");
        var pdf = await pdfResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, pdfResponse.StatusCode);
        Assert.Equal("application/pdf", pdfResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", pdfResponse.Headers.CacheControl?.ToString());
        Assert.True(pdf.Length > 2_000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        var qaOutput = Environment.GetEnvironmentVariable("SATI_PDF_QA_OUTPUT");
        if (!string.IsNullOrWhiteSpace(qaOutput))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(qaOutput)!);
            await File.WriteAllBytesAsync(qaOutput, pdf);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            _factory.TryToModifyFirstPersonVersionAsync);
    }

    [Fact]
    public async Task AdministratorCanInitializeHistoryForAnUntouchedPerson()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var response = await admin.GetAsync("/api/v1/people/101/history");
        var history = await response.Content.ReadFromJsonAsync<List<PersonVersionDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(history!, version => version.ChangeKind == "TrackingBaseline");
    }

    private static SaveProviderRequest ProviderRequest(string name) => new(
        "Other", name, null, null, null, null, null, null, 0, false, null, null, null);

    private static SavePersonRequest PersonRequest(PersonDto person, string firstName, string bio) => new(
        firstName,
        person.LastName ?? string.Empty,
        person.BirthDate,
        person.Gender,
        person.EffectiveDate,
        bio,
        person.Waiver,
        person.MaineCareId,
        person.DiagnosisCode,
        person.PlaceOfService,
        person.EvergreenId,
        person.OpenWithVR,
        person.HasGuardian,
        person.GuardianName,
        person.PhoneNumber,
        person.Address,
        person.PrimaryCareProvider,
        person.HealthcareSystemName,
        person.HasHomeSupport,
        person.HasSelfDirectedHomeSupport,
        person.HasSharedLiving,
        person.HasCommunitySupport1To1,
        person.HasCommunitySupportSelfDirected,
        person.HasCommunitySupportDayProgram,
        person.DayProgramCount,
        person.HasEmploymentSpecialist,
        person.HasWorkSupports,
        person.IsEmployed,
        [],
        person.Revision);
}
