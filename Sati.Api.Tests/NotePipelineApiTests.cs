using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// The same pipeline the desktop suite covers, driven over HTTP. A rule that the
/// client enforces and the server does not is not a rule, so the workflow table,
/// the review boundaries, and the billing gate are all re-proven here against the
/// API rather than assumed from the shared contract.
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class NotePipelineApiTests
{
    private const int Scheduled = 0;
    private const int Pending = 1;
    private const int Logged = 2;
    private const int Held = 3;
    private const int Cancelled = 4;
    private const int Delayed = 5;
    private const int Approved = 6;
    private const int Returned = 7;
    private const int Abandoned = 8;
    private const int Blocked = 9;

    private static readonly int[] AllStatuses =
        [Scheduled, Pending, Logged, Held, Cancelled, Delayed, Approved, Returned, Abandoned, Blocked];

    private static readonly string[] StatusNames =
    [
        "Scheduled", "Pending", "Logged", "HeldForCompliance", "Cancelled",
        "Delayed", "Approved", "Returned", "Abandoned", "ComplianceBlocked"
    ];

    private readonly SatiApiFactory _factory;

    public NotePipelineApiTests(SatiApiFactory factory) => _factory = factory;

    [Fact]
    public async Task EveryStatusPairIsAcceptedOrRefusedExactlyAsTheWorkflowTableSays()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        foreach (var current in AllStatuses)
        {
            foreach (var target in AllStatuses)
            {
                var noteId = await _factory.CreateNoteInStatusAsync(current);
                var (_, revision) = await _factory.GetNoteStateAsync(noteId);

                var response = await client.PutAsJsonAsync($"/api/v1/notes/{noteId}",
                    SaveRequest(target, revision, $"{current} to {target}"));
                var (storedStatus, _) = await _factory.GetNoteStateAsync(noteId);

                if (NoteWorkflow.CanCaseManagerTransition(current, target))
                {
                    Assert.True(response.StatusCode == HttpStatusCode.OK,
                        $"{StatusNames[current]} -> {StatusNames[target]} was refused with " +
                        $"{response.StatusCode}.");
                    Assert.Equal(target, storedStatus);
                }
                else
                {
                    Assert.True(
                        response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest
                            or HttpStatusCode.UnprocessableEntity,
                        $"{StatusNames[current]} -> {StatusNames[target]} was allowed with " +
                        $"{response.StatusCode}.");
                    Assert.Equal(current, storedStatus);
                }
            }
        }
    }

    [Fact]
    public async Task ANoteCannotBeAuthoredIntoAWorkflowOwnedStatus()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        foreach (var status in AllStatuses)
        {
            var response = await client.PostAsJsonAsync("/api/v1/notes",
                SaveRequest(status, 0, "Authored note"));

            if (NoteWorkflow.IsCaseManagerWritableStatus(status))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var created = await response.Content.ReadFromJsonAsync<NoteDto>();
                Assert.Equal(StatusNames[status], created!.Status);
            }
            else
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }
    }

    [Theory]
    [InlineData("Visit")]
    [InlineData("Contact")]
    [InlineData("Phone")]
    [InlineData("Email")]
    [InlineData("Form")]
    [InlineData("Other")]
    public async Task EveryCarikaNoteTypeRoundTripsThroughTheApi(string noteType)
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var formType = noteType == "Form" ? "PCP" : null;
        var request = new SaveNoteRequest(
            $"Carika {noteType} note.",
            new DateTime(2026, 8, 3),
            "Pending",
            15,
            null,
            101,
            formType,
            noteType,
            null,
            null);

        var response = await client.PostAsJsonAsync("/api/v1/notes", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<NoteDto>();
        Assert.Equal(noteType, created!.NoteType);
        Assert.Equal(formType, created.FormType);
    }

    [Fact]
    public async Task ApprovalAndReturnAcceptOnlyASubmittedNote()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("supervisor-one");

        foreach (var status in AllStatuses)
        {
            var approvable = await _factory.CreateNoteInStatusAsync(status);
            var returnable = await _factory.CreateNoteInStatusAsync(status);
            var (_, approvableRevision) = await _factory.GetNoteStateAsync(approvable);
            var (_, returnableRevision) = await _factory.GetNoteStateAsync(returnable);

            var approve = await client.PostAsJsonAsync(
                $"/api/v1/supervisor/notes/{approvable}/approve",
                new SupervisorNoteActionRequest(null, approvableRevision));
            var returned = await client.PostAsJsonAsync(
                $"/api/v1/supervisor/notes/{returnable}/return",
                new SupervisorNoteActionRequest("Please add the service location.", returnableRevision));

            var expected = status == Logged;
            Assert.Equal(expected ? HttpStatusCode.OK : HttpStatusCode.Conflict, approve.StatusCode);
            Assert.Equal(expected ? HttpStatusCode.OK : HttpStatusCode.Conflict, returned.StatusCode);

            var (approvedStatus, _) = await _factory.GetNoteStateAsync(approvable);
            var (returnedStatus, _) = await _factory.GetNoteStateAsync(returnable);
            Assert.Equal(expected ? Approved : status, approvedStatus);
            Assert.Equal(expected ? Returned : status, returnedStatus);
        }
    }

    [Fact]
    public async Task AStaleRevisionIsRefusedThroughoutTheWorkflow()
    {
        using var author = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await _factory.CreateAuthenticatedClientAsync("supervisor-one");

        var draft = await _factory.CreateNoteInStatusAsync(Pending);
        var (_, draftRevision) = await _factory.GetNoteStateAsync(draft);
        var staleEdit = await author.PutAsJsonAsync($"/api/v1/notes/{draft}",
            SaveRequest(Logged, draftRevision - 1, "Stale"));
        Assert.Equal(HttpStatusCode.Conflict, staleEdit.StatusCode);

        var submitted = await _factory.CreateNoteInStatusAsync(Logged);
        var (_, submittedRevision) = await _factory.GetNoteStateAsync(submitted);
        var staleApproval = await supervisor.PostAsJsonAsync(
            $"/api/v1/supervisor/notes/{submitted}/approve",
            new SupervisorNoteActionRequest(null, submittedRevision - 1));
        Assert.Equal(HttpStatusCode.Conflict, staleApproval.StatusCode);

        var (status, _) = await _factory.GetNoteStateAsync(submitted);
        Assert.Equal(Logged, status);
    }

    [Fact]
    public async Task AReturnRequiresARecordedReason()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("supervisor-one");
        var noteId = await _factory.CreateNoteInStatusAsync(Logged);
        var (_, revision) = await _factory.GetNoteStateAsync(noteId);

        foreach (var reason in new string?[] { null, "", "   ", new string('x', 4_001) })
        {
            var response = await client.PostAsJsonAsync(
                $"/api/v1/supervisor/notes/{noteId}/return",
                new SupervisorNoteActionRequest(reason, revision));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var (status, _) = await _factory.GetNoteStateAsync(noteId);
        Assert.Equal(Logged, status);
    }

    [Fact]
    public async Task AnApprovedNoteIsClosedToItsAuthorAndToReview()
    {
        using var author = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await _factory.CreateAuthenticatedClientAsync("supervisor-one");
        var noteId = await _factory.CreateNoteInStatusAsync(Approved);
        var (_, revision) = await _factory.GetNoteStateAsync(noteId);

        var edit = await author.PutAsJsonAsync($"/api/v1/notes/{noteId}",
            SaveRequest(Pending, revision, "Rewritten after approval"));
        var delete = await author.DeleteAsync($"/api/v1/notes/{noteId}?expectedRevision={revision}");
        var undo = await supervisor.PostAsJsonAsync(
            $"/api/v1/supervisor/notes/{noteId}/return",
            new SupervisorNoteActionRequest("Undo", revision));

        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, undo.StatusCode);

        var (status, _) = await _factory.GetNoteStateAsync(noteId);
        Assert.Equal(Approved, status);
    }

    [Fact]
    public async Task DeletionIsAllowedOnlyForUnsubmittedWork()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        foreach (var status in AllStatuses)
        {
            var noteId = await _factory.CreateNoteInStatusAsync(status);
            var (_, revision) = await _factory.GetNoteStateAsync(noteId);

            var response = await client.DeleteAsync(
                $"/api/v1/notes/{noteId}?expectedRevision={revision}");

            if (NoteWorkflow.CanCaseManagerDelete(status))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            else
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    [Fact]
    public async Task ClosedWorkIsRedraftedBeforeItCanReachReviewAgain()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        foreach (var closed in new[] { Cancelled, Abandoned })
        {
            var noteId = await _factory.CreateNoteInStatusAsync(closed);
            var (_, revision) = await _factory.GetNoteStateAsync(noteId);

            var straightToReview = await client.PutAsJsonAsync($"/api/v1/notes/{noteId}",
                SaveRequest(Logged, revision, "Straight back to review"));
            Assert.Equal(HttpStatusCode.Conflict, straightToReview.StatusCode);

            var redraft = await client.PutAsJsonAsync($"/api/v1/notes/{noteId}",
                SaveRequest(Pending, revision, "Re-documented"));
            Assert.Equal(HttpStatusCode.OK, redraft.StatusCode);

            var (_, redraftedRevision) = await _factory.GetNoteStateAsync(noteId);
            var resubmit = await client.PutAsJsonAsync($"/api/v1/notes/{noteId}",
                SaveRequest(Logged, redraftedRevision, "Resubmitted"));
            Assert.Equal(HttpStatusCode.OK, resubmit.StatusCode);
        }
    }

    [Fact]
    public async Task TheReturnAndResubmitLoopSurvivesRepetition()
    {
        using var author = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await _factory.CreateAuthenticatedClientAsync("supervisor-one");
        var noteId = await _factory.CreateNoteInStatusAsync(Pending);

        for (var round = 1; round <= 3; round++)
        {
            var (_, revision) = await _factory.GetNoteStateAsync(noteId);
            var submit = await author.PutAsJsonAsync($"/api/v1/notes/{noteId}",
                SaveRequest(Logged, revision, $"Submission {round}"));
            Assert.Equal(HttpStatusCode.OK, submit.StatusCode);

            if (round == 3)
                break;

            var (_, submittedRevision) = await _factory.GetNoteStateAsync(noteId);
            var returned = await supervisor.PostAsJsonAsync(
                $"/api/v1/supervisor/notes/{noteId}/return",
                new SupervisorNoteActionRequest($"Correction {round} required.", submittedRevision));
            Assert.Equal(HttpStatusCode.OK, returned.StatusCode);

            var (_, returnedRevision) = await _factory.GetNoteStateAsync(noteId);
            var correct = await author.PutAsJsonAsync($"/api/v1/notes/{noteId}",
                SaveRequest(Pending, returnedRevision, $"Correction {round}"));
            Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        }

        var (_, finalRevision) = await _factory.GetNoteStateAsync(noteId);
        var approve = await supervisor.PostAsJsonAsync(
            $"/api/v1/supervisor/notes/{noteId}/approve",
            new SupervisorNoteActionRequest(null, finalRevision));

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var approved = await approve.Content.ReadFromJsonAsync<NoteDto>();
        Assert.Equal("Approved", approved!.Status);
        Assert.Equal(13, approved.ApprovedById);
        // The supervisor's last instruction survives approval.
        Assert.Equal("Correction 2 required.", approved.ReturnReason);
        Assert.False(approved.ComplianceOverride);
    }

    [Fact]
    public async Task ReviewNeverCrossesAssignmentOrAgency()
    {
        var noteId = await _factory.CreateNoteInStatusAsync(Logged);
        var (_, revision) = await _factory.GetNoteStateAsync(noteId);

        using var foreignSupervisor = await _factory.CreateAuthenticatedClientAsync("supervisor-two");
        var foreign = await foreignSupervisor.PostAsJsonAsync(
            $"/api/v1/supervisor/notes/{noteId}/approve",
            new SupervisorNoteActionRequest(null, revision));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        using var author = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var selfApproval = await author.PostAsJsonAsync(
            $"/api/v1/supervisor/notes/{noteId}/approve",
            new SupervisorNoteActionRequest(null, revision));
        Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);

        var (status, _) = await _factory.GetNoteStateAsync(noteId);
        Assert.Equal(Logged, status);
    }

    [Fact]
    public async Task ANoteBelongingToAnotherCaseManagerCannotBeEditedOrDeleted()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var foreignNote = await _factory.CreateNoteInStatusAsync(Pending, personId: 201);
        var (_, revision) = await _factory.GetNoteStateAsync(foreignNote);

        var edit = await client.PutAsJsonAsync($"/api/v1/notes/{foreignNote}",
            new SaveNoteRequest("Taken over", new DateTime(2026, 8, 3), "Logged", 60, null,
                201, null, null, null, null, revision));
        var delete = await client.DeleteAsync(
            $"/api/v1/notes/{foreignNote}?expectedRevision={revision}");

        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var (status, _) = await _factory.GetNoteStateAsync(foreignNote);
        Assert.Equal(Pending, status);
    }

    [Fact]
    public async Task APendingNoteCanBeReassignedWithinTheAuthorsCaseloadAndIsAudited()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var noteId = await _factory.CreateNoteInStatusAsync(Pending);
        var (_, revision) = await _factory.GetNoteStateAsync(noteId);
        var before = await _factory.GetAuditEventsAsync("note.reassigned");

        var response = await client.PutAsJsonAsync($"/api/v1/notes/{noteId}",
            new SaveNoteRequest("Correct client", new DateTime(2026, 8, 3), "Pending", 60,
                null, 102, null, null, null, null, revision));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<NoteDto>();
        Assert.Equal(102, updated!.PersonId);
        Assert.Equal(102, updated.Person?.Id);
        Assert.Equal(revision + 1, updated.Revision);

        var originalClientNotes = await client.GetFromJsonAsync<List<NoteDto>>(
            "/api/v1/people/101/notes");
        var correctedClientNotes = await client.GetFromJsonAsync<List<NoteDto>>(
            "/api/v1/people/102/notes");
        Assert.DoesNotContain(originalClientNotes!, note => note.Id == noteId);
        Assert.Contains(correctedClientNotes!, note => note.Id == noteId);

        var after = await _factory.GetAuditEventsAsync("note.reassigned");
        Assert.Equal(before.Count + 1, after.Count);
        var audit = after[^1];
        Assert.Equal("Note", audit.ResourceType);
        Assert.Equal(noteId.ToString(), audit.ResourceId);
        using var metadata = System.Text.Json.JsonDocument.Parse(audit.MetadataJson);
        Assert.Equal(101, metadata.RootElement.GetProperty("previousPersonId").GetInt32());
        Assert.Equal(102, metadata.RootElement.GetProperty("newPersonId").GetInt32());
        Assert.DoesNotContain("Correct client", audit.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReassignmentCannotMoveANoteIntoAnotherAgency()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var noteId = await _factory.CreateNoteInStatusAsync(Pending);
        var (_, revision) = await _factory.GetNoteStateAsync(noteId);
        var before = await _factory.GetAuditEventsAsync("note.reassigned");

        var response = await client.PutAsJsonAsync($"/api/v1/notes/{noteId}",
            new SaveNoteRequest("Cross-tenant move", new DateTime(2026, 8, 3), "Pending", 60,
                null, 201, null, null, null, null, revision));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var originalClientNotes = await client.GetFromJsonAsync<List<NoteDto>>(
            "/api/v1/people/101/notes");
        Assert.Contains(originalClientNotes!, note => note.Id == noteId);
        Assert.Equal(before.Count,
            (await _factory.GetAuditEventsAsync("note.reassigned")).Count);
    }

    [Fact]
    public async Task AStaleReassignmentCannotMoveTheNewerNoteOrWriteAnAudit()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var noteId = await _factory.CreateNoteInStatusAsync(Pending);
        var (_, revision) = await _factory.GetNoteStateAsync(noteId);
        var before = await _factory.GetAuditEventsAsync("note.reassigned");
        var winner = await client.PutAsJsonAsync($"/api/v1/notes/{noteId}",
            SaveRequest(Pending, revision, "Newer saved correction"));
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);

        var staleMove = await client.PutAsJsonAsync($"/api/v1/notes/{noteId}",
            new SaveNoteRequest("Stale move", new DateTime(2026, 8, 3), "Pending", 60,
                null, 102, null, null, null, null, revision));

        Assert.Equal(HttpStatusCode.Conflict, staleMove.StatusCode);
        var originalClientNotes = await client.GetFromJsonAsync<List<NoteDto>>(
            "/api/v1/people/101/notes");
        var stored = Assert.Single(originalClientNotes!, note => note.Id == noteId);
        Assert.Equal("Newer saved correction", stored.Narrative);
        Assert.Equal(before.Count,
            (await _factory.GetAuditEventsAsync("note.reassigned")).Count);
    }

    [Fact]
    public async Task OnlyAnApprovedNoteCanBecomeAClaimLine()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");

        foreach (var status in AllStatuses)
        {
            if (status == Approved)
                continue;

            var noteId = await _factory.CreateNoteInStatusAsync(status);
            var response = await client.PostAsJsonAsync("/api/v1/billing/claim-lines",
                new CreateClaimLineRequest(noteId, false, null));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task ANoteTravelsFromDraftToASubmittedClaim()
    {
        using var author = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await _factory.CreateAuthenticatedClientAsync("supervisor-one");
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var personId = await _factory.CreateBillingWorkflowPersonAsync();

        var create = await author.PostAsJsonAsync("/api/v1/notes",
            new SaveNoteRequest("Community support contact.", new DateTime(2026, 8, 3), "Logged",
                60, null, personId, null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var note = await create.Content.ReadFromJsonAsync<NoteDto>();

        var approve = await supervisor.PostAsJsonAsync(
            $"/api/v1/supervisor/notes/{note!.Id}/approve",
            new SupervisorNoteActionRequest(null, note.Revision));
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var claim = await admin.PostAsJsonAsync("/api/v1/billing/claim-lines",
            new CreateClaimLineRequest(note.Id, false, null));
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        var line = await claim.Content.ReadFromJsonAsync<ClaimLineDto>();
        Assert.Equal(4m, line!.Units);
        Assert.Equal(100m, line.ChargeAmount);
        Assert.False(line.IsComplianceException);

        var duplicate = await admin.PostAsJsonAsync("/api/v1/billing/claim-lines",
            new CreateClaimLineRequest(note.Id, false, null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var submit = await admin.PostAsync(
            $"/api/v1/billing/periods/{line.BillingPeriodId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
    }

    [Fact]
    public async Task TheDocumentedOverrideTravelsFromTheNoteOntoTheClaim()
    {
        using var supervisor = await _factory.CreateAuthenticatedClientAsync("supervisor-one");
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var personId = await _factory.CreateBillingWorkflowPersonAsync();
        var noteId = await _factory.CreateNoteInStatusAsync(Logged, personId);
        var (_, revision) = await _factory.GetNoteStateAsync(noteId);

        var approved = await supervisor.PostAsJsonAsync(
            $"/api/v1/supervisor/notes/{noteId}/approve-override",
            new SupervisorNoteActionRequest("Documented supervisory exception.", revision));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var claim = await admin.PostAsJsonAsync("/api/v1/billing/claim-lines",
            new CreateClaimLineRequest(noteId, false, null));

        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        var line = await claim.Content.ReadFromJsonAsync<ClaimLineDto>();
        Assert.True(line!.IsComplianceException);
        Assert.Equal("Documented supervisory exception.", line.ComplianceExceptionReason);
    }

    [Fact]
    public async Task BillingIsRefusedToEveryRoleButAnAdministrator()
    {
        var noteId = await _factory.CreateNoteInStatusAsync(Approved);

        foreach (var username in new[] { "case-manager-one", "supervisor-one" })
        {
            using var client = await _factory.CreateAuthenticatedClientAsync(username);
            var response = await client.PostAsJsonAsync("/api/v1/billing/claim-lines",
                new CreateClaimLineRequest(noteId, false, null));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    private static SaveNoteRequest SaveRequest(int status, int expectedRevision, string narrative) =>
        new(narrative, new DateTime(2026, 8, 3), StatusNames[status], 60, null, 101,
            null, null, null, null, expectedRevision);
}
