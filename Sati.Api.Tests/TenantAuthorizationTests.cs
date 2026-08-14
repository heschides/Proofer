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
    public async Task ReleaseVersionIsPublicAndMatchesThePackagedClientRelease()
    {
        using var client = _factory.CreateAnonymousClient();

        var release = await client.GetFromJsonAsync<Dictionary<string, string>>("/health/version");

        Assert.NotNull(release);
        Assert.Equal("Sati.Api", release["product"]);
        Assert.Equal("1.2.13", release["releaseVersion"]);
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
    public async Task IncidentCollectionDeduplicatesAndAgencyAdminCannotSeeAnotherTenant()
    {
        using var agencyOneReporter = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var agencyTwoReporter = await _factory.CreateAuthenticatedClientAsync("case-manager-two");
        var occurredAt = DateTime.UtcNow;
        var first = new IncidentReportRequest(
            "REF_A10001", "Desktop", "Error", "billing.queue.load", "1.2.2",
            "ABCDEF0123456789ABCDEF0123456789", occurredAt);
        var foreign = new IncidentReportRequest(
            "REF_B20001", "Desktop", "Critical", "calendar.day.open", "1.2.2",
            "BBBBBB0123456789BBBBBB0123456789", occurredAt);

        Assert.Equal(HttpStatusCode.Accepted,
            (await agencyOneReporter.PostAsJsonAsync("/api/v1/incidents", first)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await agencyOneReporter.PostAsJsonAsync("/api/v1/incidents", first with { Reference = "REF_A10002" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await agencyTwoReporter.PostAsJsonAsync("/api/v1/incidents", foreign)).StatusCode);

        using var adminOne = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var dashboard = await adminOne.GetFromJsonAsync<AdminIncidentDashboardDto>("/api/v1/admin/incidents");

        var incident = Assert.Single(dashboard!.Incidents, item => item.Operation == "billing.queue.load");
        Assert.Equal(1, incident.AgencyId);
        Assert.Equal(2, incident.OccurrenceCount);
        Assert.DoesNotContain(dashboard.Incidents, item => item.AgencyId == 2);
        Assert.Equal(2, dashboard.Health.TotalOccurrences);
    }

    [Fact]
    public async Task ConcurrentIncidentReportsProduceOneGroupWithTheExactCount()
    {
        using var reporter = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        const int reportCount = 24;
        var occurredAt = DateTime.UtcNow;
        var responses = await Task.WhenAll(Enumerable.Range(0, reportCount).Select(index =>
            reporter.PostAsJsonAsync("/api/v1/incidents", new IncidentReportRequest(
                $"REF_PAR{index:000}",
                "Desktop",
                index == reportCount - 1 ? "Critical" : "Warning",
                "calendar.concurrent.open",
                "1.2.2",
                "DDDDEE0123456789DDDDEE0123456789",
                occurredAt.AddMilliseconds(index)))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var dashboard = await admin.GetFromJsonAsync<AdminIncidentDashboardDto>("/api/v1/admin/incidents");
        var incident = Assert.Single(dashboard!.Incidents,
            item => item.Operation == "calendar.concurrent.open");
        Assert.Equal(reportCount, incident.OccurrenceCount);
        Assert.Equal("Critical", incident.Severity);
    }

    [Fact]
    public async Task AgencyAdminCannotOpenPlatformDashboardButPlatformOperatorCan()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/v1/platform/incidents")).StatusCode);

        using var platform = await _factory.CreateAuthenticatedClientAsync("platform-operator");
        var response = await platform.GetAsync("/api/v1/platform/incidents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<PlatformIncidentDashboardDto>();
        Assert.NotNull(dashboard);
        Assert.Equal(2, dashboard.Agencies.Count);
        var audit = await _factory.GetAuditEventsAsync("platform-incidents.viewed");
        Assert.Contains(audit, item => item.ActorUserId == 31);
    }

    [Fact]
    public async Task PlatformOperatorCannotUseAgencyBusinessEndpoints()
    {
        using var platform = await _factory.CreateAuthenticatedClientAsync("platform-operator");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await platform.GetAsync("/api/v1/providers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await platform.GetAsync("/api/v1/users/switchable")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await platform.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task PlatformOperatorMayReachOnlyItsSelfServicePasswordEndpoint()
    {
        using var platform = await _factory.CreateAuthenticatedClientAsync("platform-operator");

        var response = await platform.PutAsJsonAsync(
            "/api/v1/users/me/password",
            new ChangePasswordRequest("deliberately-wrong", "NewPassword123!"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        Assert.Equal("invalid_current_password", error!.Code);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await platform.PutAsJsonAsync(
                "/api/v1/users/11/password",
                new ResetPasswordRequest("NewPassword123!"))).StatusCode);
    }

    [Fact]
    public async Task PlatformOperatorMayReportPlatformIncidentWithoutExposingItToAgencyAdmin()
    {
        using var platform = await _factory.CreateAuthenticatedClientAsync("platform-operator");
        var report = new IncidentReportRequest(
            "REF_PLATFORM01", "Desktop", "Error", "platform.dashboard.load", "1.2.5",
            "EEEEEE0123456789EEEEEE0123456789", DateTime.UtcNow);

        var first = await platform.PostAsJsonAsync("/api/v1/incidents", report);
        var retry = await platform.PostAsJsonAsync("/api/v1/incidents", report);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);

        var created = await first.Content.ReadFromJsonAsync<IncidentGroupDto>();
        Assert.Equal(IncidentScopes.Platform, created!.Scope);

        var platformDashboard = await platform.GetFromJsonAsync<PlatformIncidentDashboardDto>(
            "/api/v1/platform/incidents");
        var platformIncident = Assert.Single(platformDashboard!.Incidents,
            item => item.Operation == "platform.dashboard.load");
        Assert.Equal(1, platformIncident.OccurrenceCount);

        using var agencyAdmin = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var agencyDashboard = await agencyAdmin.GetFromJsonAsync<AdminIncidentDashboardDto>(
            "/api/v1/admin/incidents");
        Assert.DoesNotContain(agencyDashboard!.Incidents,
            item => item.Operation == "platform.dashboard.load");
    }

    [Fact]
    public async Task IncidentCollectionRejectsNarrativeLikeOperationAndInvalidFingerprint()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var unsafeReport = new IncidentReportRequest(
            "REF_A10003", "Desktop", "Error", "Person One narrative failed", "1.2.2",
            "not-a-hash", DateTime.UtcNow);

        var response = await client.PostAsJsonAsync("/api/v1/incidents", unsafeReport);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AgencyAdminMayResolveOnlyItsOwnIncidentAndTheChangeIsAudited()
    {
        using var reporter = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var report = new IncidentReportRequest(
            "REF_STATUS01", "Desktop", "Warning", "notes.refresh", "1.2.2",
            "CCCCCC0123456789CCCCCC0123456789", DateTime.UtcNow);
        var createdResponse = await reporter.PostAsJsonAsync("/api/v1/incidents", report);
        var created = await createdResponse.Content.ReadFromJsonAsync<IncidentGroupDto>();

        using var otherAdmin = await _factory.CreateAuthenticatedClientAsync("admin-two");
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherAdmin.PutAsJsonAsync(
                $"/api/v1/admin/incidents/{created!.Id}/status",
                new UpdateIncidentStatusRequest("Resolved"))).StatusCode);

        using var owningAdmin = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var response = await owningAdmin.PutAsJsonAsync(
            $"/api/v1/admin/incidents/{created.Id}/status",
            new UpdateIncidentStatusRequest("Resolved"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Resolved", (await response.Content.ReadFromJsonAsync<IncidentGroupDto>())!.Status);
        var audit = await _factory.GetAuditEventsAsync("incident-status.updated");
        Assert.Contains(audit, item => item.AgencyId == 1 && item.ActorUserId == 11);
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
    public async Task DraftSubmissionApprovalAndBillingProduceATest837()
    {
        var personId = await _factory.CreateBillingWorkflowPersonAsync();
        using var caseManager = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var draftResponse = await caseManager.PostAsJsonAsync(
            "/api/v1/notes",
            new SaveNoteRequest(
                "End-to-end billing workflow test",
                new DateTime(2026, 6, 10),
                "Pending",
                60,
                null,
                personId,
                null,
                "Contact",
                null,
                null));
        Assert.Equal(HttpStatusCode.OK, draftResponse.StatusCode);
        var draft = await draftResponse.Content.ReadFromJsonAsync<NoteDto>();

        using var supervisor = await _factory.CreateAuthenticatedClientAsync("supervisor-one");
        var beforeSubmission = await supervisor.GetFromJsonAsync<List<NoteDto>>(
            "/api/v1/supervisor/notes?compliant=true&allSupervisees=true");
        Assert.DoesNotContain(beforeSubmission!, note => note.Id == draft!.Id);

        var loggedResponse = await caseManager.PutAsJsonAsync(
            $"/api/v1/notes/{draft!.Id}",
            new SaveNoteRequest(
                draft.Narrative,
                draft.EventDate,
                "Logged",
                draft.Minutes,
                draft.StartTime,
                draft.PersonId,
                draft.FormType,
                draft.NoteType,
                draft.CaseManagerJustification,
                draft.VisitDocumentationJson,
                draft.Revision));
        Assert.Equal(HttpStatusCode.OK, loggedResponse.StatusCode);
        var logged = await loggedResponse.Content.ReadFromJsonAsync<NoteDto>();

        var reviewQueue = await supervisor.GetFromJsonAsync<List<NoteDto>>(
            "/api/v1/supervisor/notes?compliant=true&allSupervisees=true");
        Assert.Contains(reviewQueue!, note => note.Id == logged!.Id);

        var approvalResponse = await supervisor.PostAsJsonAsync(
            $"/api/v1/supervisor/notes/{logged!.Id}/approve",
            new SupervisorNoteActionRequest(null, logged.Revision));
        Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);
        var approved = await approvalResponse.Content.ReadFromJsonAsync<NoteDto>();
        Assert.Equal("Approved", approved!.Status);

        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var claimResponse = await admin.PostAsJsonAsync(
            "/api/v1/billing/claim-lines",
            new CreateClaimLineRequest(approved.Id, false, null));
        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
        var claim = await claimResponse.Content.ReadFromJsonAsync<ClaimLineDto>();

        var submissionResponse = await admin.PostAsJsonAsync(
            $"/api/v1/billing/periods/{claim!.BillingPeriodId}/submit",
            new { });
        Assert.Equal(HttpStatusCode.OK, submissionResponse.StatusCode);

        var ediResponse = await admin.PostAsJsonAsync(
            $"/api/v1/billing/periods/{claim.BillingPeriodId}/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")));
        Assert.True(
            ediResponse.StatusCode == HttpStatusCode.OK,
            await ediResponse.Content.ReadAsStringAsync());
        var edi = await ediResponse.Content.ReadFromJsonAsync<EdiFileDto>();
        Assert.Contains(".OATEST", edi!.FileName);
        Assert.Contains($"CLM*{claim.BillingPeriodId}-{approved.Id}", edi.Content);
        Assert.Contains("ST*837*", edi.Content);
    }

    [Fact]
    public async Task SupervisorNonCompliantQueueReturnsTheReasonsUsedByTheGate()
    {
        var noteId = await _factory.CreateNonCompliantReviewNoteAsync();
        using var supervisor = await _factory.CreateAuthenticatedClientAsync("supervisor-one");

        var queue = await supervisor.GetFromJsonAsync<List<NoteDto>>(
            "/api/v1/supervisor/notes?compliant=false&allSupervisees=true");
        var note = Assert.Single(queue!, candidate => candidate.Id == noteId);

        Assert.NotNull(note.ComplianceFailureReasons);
        Assert.Contains(note.ComplianceFailureReasons!, reason =>
            reason.Contains("PCP", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(note.ComplianceFailureReasons!, reason =>
            reason.Contains("Comprehensive Assessment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdministratorCannotGenerateAnotherAgencysBillingFile()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var response = await client.PostAsJsonAsync(
            "/api/v1/billing/periods/1201/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BillingFileRejectsAForeignAgencySourceEvenInsideAnOwnedPeriod()
    {
        await _factory.AddForeignAgencyNoteToBillingPeriodAsync(1101, 602);
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var response = await client.PostAsJsonAsync(
            "/api/v1/billing/periods/1101/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")));

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
        var line = await first.Content.ReadFromJsonAsync<ClaimLineDto>();
        Assert.Equal("G9012", line!.ProcedureCode);
        Assert.Equal("HI", line.ProcedureModifier);
        Assert.Equal(4m, line.Units);
        Assert.Equal(100m, line.ChargeAmount);
        Assert.Single(auditEvents, candidate => candidate.ResourceId == "502");
    }

    [Fact]
    public async Task RetryingEdiGenerationReplaysTheExactFileAndAuditsOnce()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-two");
        var key = Guid.NewGuid().ToString("N");
        var auditBefore = await _factory.GetAuditEventsAsync("billing-edi.generated");

        var firstResponse = await client.PostAsJsonAsync(
            "/api/v1/billing/periods/1202/edi",
            new GenerateEdiRequest(true, key));
        var retryResponse = await client.PostAsJsonAsync(
            "/api/v1/billing/periods/1202/edi",
            new GenerateEdiRequest(true, key));
        var reusedResponse = await client.PostAsJsonAsync(
            "/api/v1/billing/periods/1202/edi",
            new GenerateEdiRequest(false, key));

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<EdiFileDto>();
        var retry = await retryResponse.Content.ReadFromJsonAsync<EdiFileDto>();
        Assert.Equal(first, retry);
        Assert.Contains("NM1*IL*1*Two*Person****MI*222222~", first!.Content);
        Assert.Contains("N3*20 Test Street~", first.Content);
        Assert.Contains("CLM*1202-603*33.25***11::1", first.Content);
        Assert.Contains("SV1*HC:G9012:HI*33.25*UN*1.33*11", first.Content);
        Assert.Equal(106, first.Content.Split('~', StringSplitOptions.RemoveEmptyEntries)[0].Length + 1);
        var segments = first.Content.Split('~', StringSplitOptions.RemoveEmptyEntries);
        var se = segments.Single(segment => segment.TrimStart().StartsWith("SE*", StringComparison.Ordinal));
        var declared = int.Parse(se.Trim().Split('*')[1]);
        var stIndex = Array.FindIndex(segments, segment => segment.TrimStart().StartsWith("ST*", StringComparison.Ordinal));
        var seIndex = Array.FindIndex(segments, segment => segment.TrimStart().StartsWith("SE*", StringComparison.Ordinal));
        Assert.Equal(seIndex - stIndex + 1, declared);
        Assert.Equal(HttpStatusCode.Conflict, reusedResponse.StatusCode);
        var reusedError = await reusedResponse.Content.ReadFromJsonAsync<ApiErrorDto>();
        Assert.Equal("idempotency_key_reused", reusedError!.Code);
        Assert.Equal(1, await _factory.GetEdiGenerationCountAsync(key));
        var auditAfter = await _factory.GetAuditEventsAsync("billing-edi.generated");
        Assert.Equal(auditBefore.Count + 1, auditAfter.Count);
        Assert.Single(auditAfter, candidate => candidate.ResourceId == "1202");
    }

    [Fact]
    public async Task RetryingBillingPeriodSubmissionReturnsTheOriginalSuccessAndAuditsOnce()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-two");
        var auditBefore = await _factory.GetAuditEventsAsync("billing-period.submitted");

        var firstResponse = await client.PostAsJsonAsync(
            "/api/v1/billing/periods/1201/submit",
            new { });
        var retryResponse = await client.PostAsJsonAsync(
            "/api/v1/billing/periods/1201/submit",
            new { });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<BillingPeriodDto>();
        var retry = await retryResponse.Content.ReadFromJsonAsync<BillingPeriodDto>();
        Assert.Equal(first!.Id, retry!.Id);
        Assert.Equal(first.Status, retry.Status);
        Assert.Equal(first.SubmittedAt, retry.SubmittedAt);
        Assert.Equal(first.Lines.Select(line => line.Id), retry.Lines.Select(line => line.Id));
        var auditAfter = await _factory.GetAuditEventsAsync("billing-period.submitted");
        Assert.Equal(auditBefore.Count + 1, auditAfter.Count);
        Assert.Single(auditAfter, candidate => candidate.ResourceId == "1201");
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
    public async Task StaleAtRequestSaveCannotReplaceNewerFinancialDetailsOrItems()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var original = await client.GetFromJsonAsync<AtRequestDto>("/api/v1/at-requests/902");

        var firstResponse = await client.PutAsJsonAsync(
            "/api/v1/at-requests/902",
            AtRequestRequest(original!, "Newer Vendor", "Newer Item", original!.Revision));
        var staleResponse = await client.PutAsJsonAsync(
            "/api/v1/at-requests/902",
            AtRequestRequest(original, "Stale Vendor", "Stale Item", original.Revision));
        var stored = await client.GetFromJsonAsync<AtRequestDto>("/api/v1/at-requests/902");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var updated = await firstResponse.Content.ReadFromJsonAsync<AtRequestDto>();
        Assert.Equal(2, updated!.Revision);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        var error = await staleResponse.Content.ReadFromJsonAsync<ApiErrorDto>();
        Assert.Equal("stale_at_request", error!.Code);
        Assert.Equal("Newer Vendor", stored!.VendorName);
        Assert.Equal("Newer Item", Assert.Single(stored.Items).Name);
        Assert.Equal(2, stored.Revision);
    }

    [Fact]
    public async Task StaleAtRequestDeleteCannotRemoveTheNewerCopy()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var original = await client.GetFromJsonAsync<AtRequestDto>("/api/v1/at-requests/903");
        var updateResponse = await client.PutAsJsonAsync(
            "/api/v1/at-requests/903",
            AtRequestRequest(original!, "Newer Delete Guard", "Guard Item", original!.Revision));

        var deleteResponse = await client.DeleteAsync(
            $"/api/v1/at-requests/903?expectedRevision={original.Revision}");
        var stored = await client.GetFromJsonAsync<AtRequestDto>("/api/v1/at-requests/903");

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
        Assert.Equal("Newer Delete Guard", stored!.VendorName);
        Assert.Equal(2, stored.Revision);
    }

    [Fact]
    public async Task AtRequestUpdateWithoutAnExpectedRevisionFailsClosed()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var original = await client.GetFromJsonAsync<AtRequestDto>("/api/v1/at-requests/904");
        var legacyRequest = new
        {
            personId = original!.PersonId,
            clientName = original.ClientName,
            clientEvergreenId = original.ClientEvergreenId,
            caseManagerName = original.CaseManagerName,
            caseManagerEmail = original.CaseManagerEmail,
            caseManagerPhone = original.CaseManagerPhone,
            caseManagerAgency = original.CaseManagerAgency,
            vendorName = "Legacy overwrite attempt",
            vendorBillingLocation = original.VendorBillingLocation,
            vendorProgramContact = original.VendorProgramContact,
            vendorBillingContact = original.VendorBillingContact,
            salesTax = original.SalesTax,
            submittedDate = original.SubmittedDate,
            decisionDate = original.DecisionDate,
            status = original.Status,
            items = original.Items
        };

        var response = await client.PutAsJsonAsync("/api/v1/at-requests/904", legacyRequest);
        var stored = await client.GetFromJsonAsync<AtRequestDto>("/api/v1/at-requests/904");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Legacy Guard Vendor", stored!.VendorName);
        Assert.Equal(1, stored.Revision);
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
    public async Task StaleNoteSaveIsRejectedWithoutErasingTheNewerCopy()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var notes = await client.GetFromJsonAsync<List<NoteDto>>("/api/v1/people/101/notes");
        var original = Assert.Single(notes!, note => note.Id == 503);

        var firstResponse = await client.PutAsJsonAsync(
            "/api/v1/notes/503",
            NoteRequest(original, "Newer saved narrative", original.Revision));
        var staleResponse = await client.PutAsJsonAsync(
            "/api/v1/notes/503",
            NoteRequest(original, "Stale narrative", original.Revision));
        var stored = Assert.Single(
            (await client.GetFromJsonAsync<List<NoteDto>>("/api/v1/people/101/notes"))!,
            note => note.Id == 503);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var updated = await firstResponse.Content.ReadFromJsonAsync<NoteDto>();
        Assert.Equal(2, updated!.Revision);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        var error = await staleResponse.Content.ReadFromJsonAsync<ApiErrorDto>();
        Assert.Equal("stale_note", error!.Code);
        Assert.Equal("Newer saved narrative", stored.Narrative);
        Assert.Equal(2, stored.Revision);
    }

    [Fact]
    public async Task StaleNoteDeleteIsRejectedWithoutRemovingTheNewerCopy()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var notes = await client.GetFromJsonAsync<List<NoteDto>>("/api/v1/people/101/notes");
        var original = Assert.Single(notes!, note => note.Id == 504);
        var updateResponse = await client.PutAsJsonAsync(
            "/api/v1/notes/504",
            NoteRequest(original, "Newer copy must survive", original.Revision));

        var deleteResponse = await client.DeleteAsync(
            $"/api/v1/notes/504?expectedRevision={original.Revision}");
        var stored = Assert.Single(
            (await client.GetFromJsonAsync<List<NoteDto>>("/api/v1/people/101/notes"))!,
            note => note.Id == 504);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
        Assert.Equal("Newer copy must survive", stored.Narrative);
        Assert.Equal(2, stored.Revision);
    }

    [Fact]
    public async Task NoteUpdateWithoutAnExpectedRevisionFailsClosed()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var notes = await client.GetFromJsonAsync<List<NoteDto>>("/api/v1/people/101/notes");
        var original = Assert.Single(notes!, note => note.Id == 506);
        var legacyRequest = new
        {
            narrative = "An older client attempted this update",
            eventDate = original.EventDate,
            status = original.Status,
            minutes = original.Minutes,
            startTime = original.StartTime,
            personId = original.PersonId,
            formType = original.FormType,
            noteType = original.NoteType,
            caseManagerJustification = original.CaseManagerJustification,
            visitDocumentationJson = original.VisitDocumentationJson
        };

        var response = await client.PutAsJsonAsync("/api/v1/notes/506", legacyRequest);
        var stored = Assert.Single(
            (await client.GetFromJsonAsync<List<NoteDto>>("/api/v1/people/101/notes"))!,
            note => note.Id == 506);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Legacy client guard note", stored.Narrative);
        Assert.Equal(1, stored.Revision);
    }

    [Fact]
    public async Task StaleSupervisorDecisionCannotReplaceTheCompletedDecision()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("supervisor-one");

        var firstResponse = await client.PostAsJsonAsync(
            "/api/v1/supervisor/notes/505/return",
            new SupervisorNoteActionRequest("Needs correction", 1));
        var staleResponse = await client.PostAsJsonAsync(
            "/api/v1/supervisor/notes/505/return",
            new SupervisorNoteActionRequest("Stale second decision", 1));
        var events = await _factory.GetAuditEventsAsync("note.returned");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var returned = await firstResponse.Content.ReadFromJsonAsync<NoteDto>();
        Assert.Equal(2, returned!.Revision);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Single(events, candidate => candidate.ResourceId == "505");
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

    [Fact]
    public async Task AdministratorOperationsStatusIsAgencyScopedAndReportsPolicy()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var response = await client.GetAsync("/api/v1/admin/operations");
        var operations = await response.Content.ReadFromJsonAsync<AdminOperationsDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.NotNull(operations);
        Assert.Equal("Healthy", operations.DatabaseStatus);
        Assert.Equal("PolicyOnly", operations.RetentionEnforcementMode);
        Assert.Equal(OperationalPolicyDefaults.AuditRetentionDays, operations.AuditRetentionDays);
        Assert.Equal(OperationalPolicyDefaults.EdiReplayRetentionDays, operations.EdiReplayRetentionDays);
        Assert.True(operations.AuditEventCount > 0);
        Assert.True(operations.EdiReplayCount >= 0);
        Assert.True(operations.EdiReplayCharacters >= 0);
    }

    [Fact]
    public async Task AuditExportIsTenantScopedReasonGatedAndAudited()
    {
        const string reason = "Quarterly internal compliance review";
        using var otherAgencyClient = await _factory.CreateAuthenticatedClientAsync("admin-two");
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var before = await _factory.GetAuditEventsAsync("audit.exported");
        var request = new AdminAuditExportRequest(
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow,
            reason);

        var response = await client.PostAsJsonAsync("/api/v1/admin/audit-export.csv", request);
        var csv = System.Text.Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
        var after = await _factory.GetAuditEventsAsync("audit.exported");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("admin-one", csv);
        Assert.DoesNotContain("admin-two", csv);
        Assert.Contains(reason, csv);
        Assert.Equal(before.Count + 1, after.Count);
        var exportEvent = after[^1];
        Assert.Equal(1, exportEvent.AgencyId);
        Assert.Equal(11, exportEvent.ActorUserId);
        Assert.Equal("AuditEvent", exportEvent.ResourceType);
        Assert.DoesNotContain(reason, exportEvent.MetadataJson);
        Assert.Contains("rowCount", exportEvent.MetadataJson);
    }

    [Fact]
    public async Task AuditExportRejectsMissingBusinessReason()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var before = await _factory.GetAuditEventsAsync("audit.exported");

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/audit-export.csv",
            new AdminAuditExportRequest(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, "short"));
        var after = await _factory.GetAuditEventsAsync("audit.exported");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Count, after.Count);
    }

    [Fact]
    public async Task CaseManagerCannotExportAgencyAuditActivity()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/audit-export.csv",
            new AdminAuditExportRequest(
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow,
                "Internal compliance review"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    [Theory]
    [InlineData("/api/v1/admin/overview")]
    [InlineData("/api/v1/admin/operations")]
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
        // Simulate a pre-billing-address client: omitted fields must preserve the server values.
        var update = PersonRequest(original, "Updated", "Revised lifecycle biography.") with
        {
            BillingStreet = null,
            BillingCity = null,
            BillingState = null,
            BillingZip = null,
            UpdateBillingAddress = false
        };

        var updateResponse = await caseManager.PutAsJsonAsync("/api/v1/people/102", update);
        var updated = await updateResponse.Content.ReadFromJsonAsync<PersonDto>();
        var staleResponse = await caseManager.PutAsJsonAsync("/api/v1/people/102", update);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(2, updated!.Revision);
        Assert.Equal("10 First Avenue", updated.BillingStreet);
        Assert.Equal("Portland", updated.BillingCity);
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

    [Fact]
    public async Task SettingsRejectStaleAndLegacyWritesWithoutFalseAuditEventsOrTenantLeakage()
    {
        using var agencyOneAdmin = await _factory.CreateAuthenticatedClientAsync("admin-one");
        using var agencyTwoAdmin = await _factory.CreateAuthenticatedClientAsync("admin-two");
        var original = await agencyOneAdmin.GetFromJsonAsync<SettingsDto>("/api/v1/settings");
        var otherAgencyOriginal = await agencyTwoAdmin.GetFromJsonAsync<SettingsDto>("/api/v1/settings");
        var auditBefore = await _factory.GetAuditEventsAsync("settings.updated");

        var successfulResponse = await agencyOneAdmin.PutAsJsonAsync(
            "/api/v1/settings",
            original! with { ProductivityThreshold = original.ProductivityThreshold + 7 });
        var successful = await successfulResponse.Content.ReadFromJsonAsync<SettingsDto>();

        Assert.Equal(HttpStatusCode.OK, successfulResponse.StatusCode);
        Assert.Equal(original.Revision + 1, successful!.Revision);

        var staleResponse = await agencyOneAdmin.PutAsJsonAsync(
            "/api/v1/settings",
            original with { ProductivityThreshold = original.ProductivityThreshold + 13 });
        var staleError = await staleResponse.Content.ReadFromJsonAsync<ApiErrorDto>();

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal("stale_settings", staleError!.Code);

        var serializerOptions = new System.Text.Json.JsonSerializerOptions(
            System.Text.Json.JsonSerializerDefaults.Web);
        var legacyRequest = System.Text.Json.JsonSerializer
            .SerializeToNode(successful, serializerOptions)!.AsObject();
        legacyRequest.Remove("revision");
        legacyRequest["productivityThreshold"] = original.ProductivityThreshold + 19;

        var legacyResponse = await agencyOneAdmin.PutAsJsonAsync("/api/v1/settings", legacyRequest);
        var legacyError = await legacyResponse.Content.ReadFromJsonAsync<ApiErrorDto>();

        Assert.Equal(HttpStatusCode.Conflict, legacyResponse.StatusCode);
        Assert.Equal("stale_settings", legacyError!.Code);

        var stored = await agencyOneAdmin.GetFromJsonAsync<SettingsDto>("/api/v1/settings");
        var otherAgencyStored = await agencyTwoAdmin.GetFromJsonAsync<SettingsDto>("/api/v1/settings");
        var auditAfter = await _factory.GetAuditEventsAsync("settings.updated");

        Assert.Equal(original.ProductivityThreshold + 7, stored!.ProductivityThreshold);
        Assert.Equal(successful.Revision, stored.Revision);
        Assert.Equal(otherAgencyOriginal, otherAgencyStored);
        Assert.Equal(auditBefore.Count + 1, auditAfter.Count);
        var savedEvent = auditAfter[^1];
        Assert.Equal(1, savedEvent.AgencyId);
        Assert.Equal(11, savedEvent.ActorUserId);
        Assert.Equal("Settings", savedEvent.ResourceType);
    }

    [Fact]
    public async Task ScratchpadAutosaveRejectsStaleAndLegacyWritesWithoutLosingWork()
    {
        using var firstSession = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var secondSession = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var otherUser = await _factory.CreateAuthenticatedClientAsync("case-manager-two");
        var firstCopy = await firstSession.GetFromJsonAsync<ScratchpadDto>("/api/v1/scratchpad/today");
        var staleCopy = await secondSession.GetFromJsonAsync<ScratchpadDto>("/api/v1/scratchpad/today");
        var otherOriginal = await otherUser.GetFromJsonAsync<ScratchpadDto>("/api/v1/scratchpad/today");
        var auditBefore = await _factory.GetAuditEventsAsync("scratchpad.updated");

        var savedResponse = await firstSession.PutAsJsonAsync(
            "/api/v1/scratchpad",
            new SaveScratchpadRequest(firstCopy!.Id, "Newer scratchpad work", firstCopy.Revision));
        var saved = await savedResponse.Content.ReadFromJsonAsync<ScratchpadDto>();

        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        Assert.Equal(firstCopy.Revision + 1, saved!.Revision);
        Assert.Equal("Newer scratchpad work", saved.Content);

        var staleResponse = await secondSession.PutAsJsonAsync(
            "/api/v1/scratchpad",
            new SaveScratchpadRequest(staleCopy!.Id, "Stale work must survive locally", staleCopy.Revision));
        var staleError = await staleResponse.Content.ReadFromJsonAsync<ApiErrorDto>();

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal("stale_scratchpad", staleError!.Code);

        var legacyRequest = System.Text.Json.JsonSerializer.SerializeToNode(
            new SaveScratchpadRequest(saved.Id, "Legacy overwrite", saved.Revision),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.AsObject();
        legacyRequest.Remove("expectedRevision");

        var legacyResponse = await firstSession.PutAsJsonAsync("/api/v1/scratchpad", legacyRequest);
        var legacyError = await legacyResponse.Content.ReadFromJsonAsync<ApiErrorDto>();

        Assert.Equal(HttpStatusCode.Conflict, legacyResponse.StatusCode);
        Assert.Equal("stale_scratchpad", legacyError!.Code);

        var noOpResponse = await firstSession.PutAsJsonAsync(
            "/api/v1/scratchpad",
            new SaveScratchpadRequest(saved.Id, saved.Content, saved.Revision));
        var noOp = await noOpResponse.Content.ReadFromJsonAsync<ScratchpadDto>();

        Assert.Equal(HttpStatusCode.OK, noOpResponse.StatusCode);
        Assert.Equal(saved.Revision, noOp!.Revision);

        var crossUserResponse = await otherUser.PutAsJsonAsync(
            "/api/v1/scratchpad",
            new SaveScratchpadRequest(saved.Id, "Cross-user overwrite", otherOriginal!.Revision));
        Assert.Equal(HttpStatusCode.NotFound, crossUserResponse.StatusCode);

        var stored = await firstSession.GetFromJsonAsync<ScratchpadDto>("/api/v1/scratchpad/today");
        var otherStored = await otherUser.GetFromJsonAsync<ScratchpadDto>("/api/v1/scratchpad/today");
        var auditAfter = await _factory.GetAuditEventsAsync("scratchpad.updated");

        Assert.Equal("Newer scratchpad work", stored!.Content);
        Assert.Equal(saved.Revision, stored.Revision);
        Assert.Equal(otherOriginal.Content, otherStored!.Content);
        Assert.Equal(otherOriginal.Revision, otherStored.Revision);
        Assert.Equal(auditBefore.Count + 1, auditAfter.Count);
        var savedEvent = auditAfter[^1];
        Assert.Equal(1, savedEvent.AgencyId);
        Assert.Equal(12, savedEvent.ActorUserId);
        Assert.Equal("Scratchpad", savedEvent.ResourceType);
    }

    private static SaveProviderRequest ProviderRequest(string name) => new(
        "Other", name, null, null, null, null, null, null, 0, false, null, null, null);

    private static SaveNoteRequest NoteRequest(NoteDto note, string narrative, int expectedRevision) => new(
        narrative,
        note.EventDate,
        note.Status,
        note.Minutes,
        note.StartTime,
        note.PersonId,
        note.FormType,
        note.NoteType,
        note.CaseManagerJustification,
        note.VisitDocumentationJson,
        expectedRevision);

    private static SaveAtRequestRequest AtRequestRequest(
        AtRequestDto request,
        string vendorName,
        string itemName,
        int expectedRevision) => new(
        request.PersonId,
        request.ClientName,
        request.ClientEvergreenId,
        request.CaseManagerName,
        request.CaseManagerEmail,
        request.CaseManagerPhone,
        request.CaseManagerAgency,
        vendorName,
        request.VendorBillingLocation,
        request.VendorProgramContact,
        request.VendorBillingContact,
        request.SalesTax,
        request.SubmittedDate,
        request.DecisionDate,
        request.Status,
        [new SaveAtRequestItemRequest(0, itemName, 50m, 2, null)],
        expectedRevision);

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
        person.BillingStreet,
        person.BillingCity,
        person.BillingState,
        person.BillingZip,
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
        person.Revision,
        true);
}
