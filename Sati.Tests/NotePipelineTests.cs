using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Models;
using Sati.Models.Billing;
using Sati.Services.Billing;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The desktop-local pipeline from note authoring through supervisory review and
/// into a billing claim, exercised across every note status rather than along the
/// one happy path. The API mirrors these rules; see NotePipelineApiTests.
/// </summary>
public sealed class NotePipelineTests
{
    private static readonly NoteStatus[] AllStatuses = Enum.GetValues<NoteStatus>();

    // ---------------------------------------------------------------------
    // Authoring and the workflow table
    // ---------------------------------------------------------------------

    [Fact]
    public async Task EveryStatusPairIsAcceptedOrRefusedExactlyAsTheWorkflowTableSays()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var service = fixture.NotesAs(fixture.CaseManagerOne);
        var day = 1;

        foreach (var current in AllStatuses)
        {
            foreach (var target in AllStatuses)
            {
                var noteId = await fixture.SeedNoteAsync(
                    fixture.PersonOneId, current, fixture.ServiceDate(day++));
                var draft = await fixture.DetachedNoteAsync(noteId);
                draft.Status = target;
                draft.Narrative = $"{current} to {target}";

                var allowed = NoteWorkflow.CanCaseManagerTransition((int?)current, (int?)target);
                if (allowed)
                {
                    await service.UpdateNoteAsync(draft);
                    Assert.Equal(target, await fixture.StatusOfAsync(noteId));
                }
                else
                {
                    await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateNoteAsync(draft));
                    Assert.Equal(current, await fixture.StatusOfAsync(noteId));
                }
            }
        }
    }

    [Fact]
    public async Task ACaseManagerCannotAuthorAStatusOwnedByAnotherWorkflow()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var service = fixture.NotesAs(fixture.CaseManagerOne);

        foreach (var status in AllStatuses)
        {
            var note = Note.Create("New note", fixture.ServiceDate(1), status, 30, fixture.PersonOneId);
            if (NoteWorkflow.IsCaseManagerWritableStatus((int?)status))
            {
                var saved = await service.AddNoteAsync(note);
                Assert.Equal(status, await fixture.StatusOfAsync(saved.Id));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddNoteAsync(note));
            }
        }
    }

    [Fact]
    public async Task DeletionIsAllowedOnlyForUnsubmittedWork()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var service = fixture.NotesAs(fixture.CaseManagerOne);
        var day = 1;

        foreach (var status in AllStatuses)
        {
            var noteId = await fixture.SeedNoteAsync(
                fixture.PersonOneId, status, fixture.ServiceDate(day++));
            var note = await fixture.DetachedNoteAsync(noteId);

            if (NoteWorkflow.CanCaseManagerDelete((int?)status))
            {
                await service.DeleteNoteAsync(note);
                Assert.False(await fixture.NoteExistsAsync(noteId));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteNoteAsync(note));
                Assert.True(await fixture.NoteExistsAsync(noteId));
            }
        }
    }

    [Fact]
    public async Task AStaleRevisionIsRefusedForBothEditsAndDeletes()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var service = fixture.NotesAs(fixture.CaseManagerOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Pending, fixture.ServiceDate(1));

        var first = await fixture.DetachedNoteAsync(noteId);
        var concurrent = await fixture.DetachedNoteAsync(noteId);

        first.Narrative = "Winner";
        await service.UpdateNoteAsync(first);

        concurrent.Narrative = "Loser";
        await Assert.ThrowsAsync<NoteConcurrencyException>(() => service.UpdateNoteAsync(concurrent));
        await Assert.ThrowsAsync<NoteConcurrencyException>(() => service.DeleteNoteAsync(concurrent));
        Assert.Equal("Winner", await fixture.NarrativeOfAsync(noteId));
    }

    [Fact]
    public async Task APendingNoteCanBeReassignedWithinTheCaseManagersOwnCaseloadAndIsAudited()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var service = fixture.NotesAs(fixture.CaseManagerOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Pending, fixture.ServiceDate(1));
        var draft = await fixture.DetachedNoteAsync(noteId);
        draft.PersonId = fixture.PersonIncompleteId;

        await service.UpdateNoteAsync(draft);

        var stored = await fixture.NoteAsync(noteId);
        Assert.Equal(fixture.PersonIncompleteId, stored.PersonId);
        Assert.Equal(2, stored.Revision);

        await using var db = fixture.Factory.CreateDbContext();
        var reassignment = await db.AuditEvents.AsNoTracking()
            .SingleAsync(candidate => candidate.Action == "note.reassigned");
        using var metadata = System.Text.Json.JsonDocument.Parse(reassignment.MetadataJson);
        Assert.Equal(fixture.PersonOneId,
            metadata.RootElement.GetProperty("previousPersonId").GetInt32());
        Assert.Equal(fixture.PersonIncompleteId,
            metadata.RootElement.GetProperty("newPersonId").GetInt32());
        Assert.DoesNotContain("Seeded note", reassignment.MetadataJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("peer")]
    [InlineData("foreign")]
    public async Task ANoteCannotBeReassignedOutsideTheCaseManagersOwnCaseload(string target)
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var service = fixture.NotesAs(fixture.CaseManagerOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Pending, fixture.ServiceDate(1));
        var draft = await fixture.DetachedNoteAsync(noteId);
        draft.PersonId = target == "peer" ? fixture.PersonPeerId : fixture.PersonTwoId;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateNoteAsync(draft));

        Assert.Equal(fixture.PersonOneId, (await fixture.NoteAsync(noteId)).PersonId);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.False(await db.AuditEvents.AnyAsync(candidate => candidate.Action == "note.reassigned"));
    }

    [Fact]
    public async Task AStaleReassignmentCannotMoveTheNewerNoteOrWriteAReassignmentAudit()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var service = fixture.NotesAs(fixture.CaseManagerOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Pending, fixture.ServiceDate(1));
        var winner = await fixture.DetachedNoteAsync(noteId);
        var staleMove = await fixture.DetachedNoteAsync(noteId);
        winner.Narrative = "Newer saved correction";
        staleMove.PersonId = fixture.PersonIncompleteId;

        await service.UpdateNoteAsync(winner);
        await Assert.ThrowsAsync<NoteConcurrencyException>(() => service.UpdateNoteAsync(staleMove));

        var stored = await fixture.NoteAsync(noteId);
        Assert.Equal(fixture.PersonOneId, stored.PersonId);
        Assert.Equal("Newer saved correction", stored.Narrative);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.False(await db.AuditEvents.AnyAsync(candidate => candidate.Action == "note.reassigned"));
    }

    // ---------------------------------------------------------------------
    // Supervisory review
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ApprovalAndReturnAcceptOnlyASubmittedNote()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var supervisor = fixture.SupervisionAs(fixture.SupervisorOne);
        var day = 1;

        foreach (var status in AllStatuses)
        {
            var approvable = await fixture.SeedNoteAsync(
                fixture.PersonOneId, status, fixture.ServiceDate(day++));
            var returnable = await fixture.SeedNoteAsync(
                fixture.PersonOneId, status, fixture.ServiceDate(day++));
            var expected = NoteWorkflow.CanSupervisorTransition((int?)status, NoteWorkflow.Approved);

            if (expected)
            {
                await supervisor.ApproveNoteAsync(approvable, fixture.SupervisorOne.Id,
                    await fixture.RevisionOfAsync(approvable));
                await supervisor.ReturnNoteAsync(returnable, fixture.SupervisorOne.Id,
                    "Please add the service location.", await fixture.RevisionOfAsync(returnable));
                Assert.Equal(NoteStatus.Approved, await fixture.StatusOfAsync(approvable));
                Assert.Equal(NoteStatus.Returned, await fixture.StatusOfAsync(returnable));
            }
            else
            {
                var approvableRevision = await fixture.RevisionOfAsync(approvable);
                var returnableRevision = await fixture.RevisionOfAsync(returnable);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    supervisor.ApproveNoteAsync(approvable, fixture.SupervisorOne.Id, approvableRevision));
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    supervisor.ReturnNoteAsync(returnable, fixture.SupervisorOne.Id, "No",
                        returnableRevision));
                Assert.Equal(status, await fixture.StatusOfAsync(approvable));
            }
        }
    }

    [Fact]
    public async Task ReviewIsRefusedAcrossAssignmentAndAcrossAgency()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Logged, fixture.ServiceDate(1));
        var revision = await fixture.RevisionOfAsync(noteId);

        var foreign = fixture.SupervisionAs(fixture.SupervisorTwo);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            foreign.ApproveNoteAsync(noteId, fixture.SupervisorTwo.Id, revision));

        // A case manager may not review at all, not even their own work.
        var author = fixture.SupervisionAs(fixture.CaseManagerOne);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            author.ApproveNoteAsync(noteId, fixture.CaseManagerOne.Id, revision));

        // Nor may a reviewer act under someone else's identity.
        var impersonating = fixture.SupervisionAs(fixture.SupervisorOne);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            impersonating.ApproveNoteAsync(noteId, fixture.SupervisorTwo.Id, revision));

        Assert.Equal(NoteStatus.Logged, await fixture.StatusOfAsync(noteId));
    }

    [Fact]
    public async Task AStaleRevisionIsRefusedForApprovalAndReturn()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var supervisor = fixture.SupervisionAs(fixture.SupervisorOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Logged, fixture.ServiceDate(1));
        var stale = await fixture.RevisionOfAsync(noteId) - 1;

        await Assert.ThrowsAsync<NoteConcurrencyException>(() =>
            supervisor.ApproveNoteAsync(noteId, fixture.SupervisorOne.Id, stale));
        await Assert.ThrowsAsync<NoteConcurrencyException>(() =>
            supervisor.ReturnNoteAsync(noteId, fixture.SupervisorOne.Id, "Reason", stale));
        Assert.Equal(NoteStatus.Logged, await fixture.StatusOfAsync(noteId));
    }

    [Fact]
    public async Task AReturnRequiresARecordedReason()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var supervisor = fixture.SupervisionAs(fixture.SupervisorOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Logged, fixture.ServiceDate(1));
        var revision = await fixture.RevisionOfAsync(noteId);

        foreach (var empty in new[] { string.Empty, "   ", null })
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                supervisor.ReturnNoteAsync(noteId, fixture.SupervisorOne.Id, empty!, revision));
        }

        await Assert.ThrowsAsync<ArgumentException>(() =>
            supervisor.ReturnNoteAsync(noteId, fixture.SupervisorOne.Id,
                new string('x', 4_001), revision));
        Assert.Equal(NoteStatus.Logged, await fixture.StatusOfAsync(noteId));
    }

    // ---------------------------------------------------------------------
    // The correction loop
    // ---------------------------------------------------------------------

    [Fact]
    public async Task TheReturnAndResubmitLoopSurvivesRepetition()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var notes = fixture.NotesAs(fixture.CaseManagerOne);
        var supervisor = fixture.SupervisionAs(fixture.SupervisorOne);

        var created = await notes.AddNoteAsync(Note.Create(
            "First draft", fixture.BillableDate, NoteStatus.Pending, 60, fixture.PersonOneId));
        var noteId = created.Id;

        for (var round = 1; round <= 3; round++)
        {
            var submitting = await fixture.DetachedNoteAsync(noteId);
            submitting.Status = NoteStatus.Logged;
            submitting.Narrative = $"Submission {round}";
            await notes.UpdateNoteAsync(submitting);
            Assert.Equal(NoteStatus.Logged, await fixture.StatusOfAsync(noteId));

            if (round < 3)
            {
                await supervisor.ReturnNoteAsync(noteId, fixture.SupervisorOne.Id,
                    $"Correction {round} required.", await fixture.RevisionOfAsync(noteId));
                Assert.Equal(NoteStatus.Returned, await fixture.StatusOfAsync(noteId));

                var correcting = await fixture.DetachedNoteAsync(noteId);
                correcting.Status = NoteStatus.Pending;
                correcting.Narrative = $"Correction {round}";
                await notes.UpdateNoteAsync(correcting);
            }
        }

        await supervisor.ApproveNoteAsync(noteId, fixture.SupervisorOne.Id,
            await fixture.RevisionOfAsync(noteId));

        var stored = await fixture.NoteAsync(noteId);
        Assert.Equal(NoteStatus.Approved, stored.Status);
        Assert.Equal(fixture.SupervisorOne.Id, stored.ApprovedById);
        Assert.NotNull(stored.ApprovedAt);
        // The last return stays on the record; approval does not erase the history
        // of what the supervisor asked for.
        Assert.Equal("Correction 2 required.", stored.ReturnReason);
        Assert.False(stored.ComplianceOverride);
    }

    [Fact]
    public async Task AnApprovedNoteIsClosedToItsAuthor()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var notes = fixture.NotesAs(fixture.CaseManagerOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Approved, fixture.BillableDate);

        var edit = await fixture.DetachedNoteAsync(noteId);
        edit.Narrative = "Rewritten after approval";
        edit.Status = NoteStatus.Pending;
        await Assert.ThrowsAsync<InvalidOperationException>(() => notes.UpdateNoteAsync(edit));

        var delete = await fixture.DetachedNoteAsync(noteId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => notes.DeleteNoteAsync(delete));

        var supervisor = fixture.SupervisionAs(fixture.SupervisorOne);
        var revision = await fixture.RevisionOfAsync(noteId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            supervisor.ReturnNoteAsync(noteId, fixture.SupervisorOne.Id, "Undo", revision));

        Assert.Equal(NoteStatus.Approved, await fixture.StatusOfAsync(noteId));
    }

    [Fact]
    public async Task ClosedWorkIsRedraftedBeforeItCanReachReviewAgain()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var notes = fixture.NotesAs(fixture.CaseManagerOne);

        foreach (var closed in new[] { NoteStatus.Cancelled, NoteStatus.Abandoned })
        {
            var noteId = await fixture.SeedNoteAsync(
                fixture.PersonOneId, closed, fixture.ServiceDate(1));

            var straightToReview = await fixture.DetachedNoteAsync(noteId);
            straightToReview.Status = NoteStatus.Logged;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                notes.UpdateNoteAsync(straightToReview));

            var redrafted = await fixture.DetachedNoteAsync(noteId);
            redrafted.Status = NoteStatus.Pending;
            redrafted.Narrative = "Re-documented";
            await notes.UpdateNoteAsync(redrafted);

            var resubmitted = await fixture.DetachedNoteAsync(noteId);
            resubmitted.Status = NoteStatus.Logged;
            await notes.UpdateNoteAsync(resubmitted);
            Assert.Equal(NoteStatus.Logged, await fixture.StatusOfAsync(noteId));
        }
    }

    // ---------------------------------------------------------------------
    // Reading and sweeping
    // ---------------------------------------------------------------------

    [Fact]
    public async Task NoteReadsAreScopedToTheCallersOwnPeopleAndSupervisees()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        // Use today rather than "yesterday": on the first of a month, yesterday is
        // outside the monthly query this test is explicitly exercising.
        await fixture.SeedNoteAsync(fixture.PersonOneId, NoteStatus.Logged, fixture.BillableDate);

        var owner = fixture.NotesAs(fixture.CaseManagerOne);
        Assert.NotEmpty(await owner.GetAllByPersonAsync(fixture.PersonOneId));
        Assert.NotEmpty(await owner.GetMonthlyNotesAsync(fixture.CaseManagerOne.Id));

        // A peer in the same agency has no claim on another case manager's caseload.
        var peer = fixture.NotesAs(fixture.CaseManagerPeer);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            peer.GetAllByPersonAsync(fixture.PersonOneId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            peer.GetMonthlyNotesAsync(fixture.CaseManagerOne.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            peer.GetDayScheduleAsync(fixture.CaseManagerOne.Id, fixture.ServiceDate(1)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            peer.GetByYearAsync(fixture.CaseManagerOne.Id, fixture.ServiceDate(1).Year));

        // The assigned supervisor may read their supervisee's work.
        var supervisor = fixture.NotesAs(fixture.SupervisorOne);
        Assert.NotEmpty(await supervisor.GetAllByPersonAsync(fixture.PersonOneId));
        Assert.NotEmpty(await supervisor.GetMonthlyNotesAsync(fixture.CaseManagerOne.Id));

        // A supervisor in another agency may not.
        var foreign = fixture.NotesAs(fixture.SupervisorTwo);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            foreign.GetAllByPersonAsync(fixture.PersonOneId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            foreign.GetMonthlyNotesAsync(fixture.CaseManagerOne.Id));
    }

    [Fact]
    public async Task CalendarYearReadIncludesTheWholeLastDayAndLoadsClientNames()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var notes = fixture.NotesAs(fixture.CaseManagerOne);
        var year = DateTime.Today.Year;
        var before = await fixture.SeedNoteAsync(
            fixture.PersonOneId,
            NoteStatus.Logged,
            new DateTime(year - 1, 12, 31, 23, 59, 0));
        var first = await fixture.SeedNoteAsync(
            fixture.PersonOneId,
            NoteStatus.Logged,
            new DateTime(year, 1, 1, 0, 0, 0));
        var last = await fixture.SeedNoteAsync(
            fixture.PersonOneId,
            NoteStatus.Logged,
            new DateTime(year, 12, 31, 23, 59, 59));
        var after = await fixture.SeedNoteAsync(
            fixture.PersonOneId,
            NoteStatus.Logged,
            new DateTime(year + 1, 1, 1, 0, 0, 0));

        var result = await notes.GetByYearAsync(fixture.CaseManagerOne.Id, year);

        Assert.Contains(result, note => note.Id == first);
        Assert.Contains(result, note => note.Id == last);
        Assert.DoesNotContain(result, note => note.Id == before);
        Assert.DoesNotContain(result, note => note.Id == after);
        Assert.All(result, note => Assert.False(string.IsNullOrWhiteSpace(note.Person.FullName)));
    }

    [Fact]
    public async Task MonthlyReadIncludesTheWholeLastDay()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var notes = fixture.NotesAs(fixture.CaseManagerOne);
        var firstDay = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var nextMonth = firstDay.AddMonths(1);
        var before = await fixture.SeedNoteAsync(
            fixture.PersonOneId,
            NoteStatus.Logged,
            firstDay.AddTicks(-1));
        var first = await fixture.SeedNoteAsync(
            fixture.PersonOneId,
            NoteStatus.Logged,
            firstDay);
        var last = await fixture.SeedNoteAsync(
            fixture.PersonOneId,
            NoteStatus.Logged,
            nextMonth.AddTicks(-1));
        var after = await fixture.SeedNoteAsync(
            fixture.PersonOneId,
            NoteStatus.Logged,
            nextMonth);

        var result = await notes.GetMonthlyNotesAsync(fixture.CaseManagerOne.Id);

        Assert.Contains(result, note => note.Id == first);
        Assert.Contains(result, note => note.Id == last);
        Assert.DoesNotContain(result, note => note.Id == before);
        Assert.DoesNotContain(result, note => note.Id == after);
    }

    [Fact]
    public async Task LocalPersistenceNormalizesFutureWorkToAScheduledPlanWithoutActualTime()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var service = fixture.NotesAs(fixture.CaseManagerOne);
        var reminderDate = DateTime.Today.AddDays(10);
        var note = Note.Create(
            "Check whether transportation was arranged.",
            reminderDate,
            NoteStatus.Logged,
            60,
            fixture.PersonOneId,
            FormType.PCP,
            NoteType.Form);
        note.StartTime = 120;
        note.CaseManagerJustification = "Future input cannot carry this.";

        await service.AddNoteAsync(note);

        var stored = await fixture.DetachedNoteAsync(note.Id);
        Assert.Equal(reminderDate.Date, stored.EventDate);
        Assert.Equal(NoteStatus.Scheduled, stored.Status);
        Assert.Equal(NoteType.Form, stored.NoteType);
        Assert.Equal(60, stored.Minutes);
        Assert.Null(stored.StartTime);
        Assert.Equal(FormType.PCP, stored.FormType);
        Assert.Null(stored.CaseManagerJustification);
        Assert.Null(stored.VisitDocumentationJson);
    }

    [Fact]
    public async Task TheAbandonmentSweepTouchesOnlyTheCallersOwnOverdueDrafts()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var stale = DateTime.Today.AddDays(-30);

        var ownDraft = await fixture.SeedNoteAsync(fixture.PersonOneId, NoteStatus.Pending, stale);
        var ownScheduled = await fixture.SeedNoteAsync(fixture.PersonOneId, NoteStatus.Scheduled, stale);
        var ownRecent = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Pending, DateTime.Today);
        var peerDraft = await fixture.SeedNoteAsync(fixture.PersonPeerId, NoteStatus.Pending, stale);
        var foreignDraft = await fixture.SeedNoteAsync(fixture.PersonTwoId, NoteStatus.Pending, stale);

        await fixture.NotesAs(fixture.CaseManagerOne).UpdateAbandonedNotesAsync(8);

        Assert.Equal(NoteStatus.Abandoned, await fixture.StatusOfAsync(ownDraft));
        Assert.Equal(NoteStatus.Scheduled, await fixture.StatusOfAsync(ownScheduled));
        Assert.Equal(NoteStatus.Pending, await fixture.StatusOfAsync(ownRecent));
        Assert.Equal(NoteStatus.Pending, await fixture.StatusOfAsync(peerDraft));
        Assert.Equal(NoteStatus.Pending, await fixture.StatusOfAsync(foreignDraft));
    }

    // ---------------------------------------------------------------------
    // Billing
    // ---------------------------------------------------------------------

    [Fact]
    public async Task OnlyAnApprovedNoteCanBecomeAClaimLine()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var billing = fixture.BillingAs(fixture.AdminOne);
        var day = 1;

        foreach (var status in AllStatuses)
        {
            var noteId = await fixture.SeedNoteAsync(
                fixture.PersonOneId, status, fixture.ServiceDate(day++));

            if (status == NoteStatus.Approved)
            {
                var line = await billing.CreateClaimLineAsync(noteId);
                Assert.Equal(noteId, line.NoteId);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    billing.CreateClaimLineAsync(noteId));
            }
        }
    }

    [Fact]
    public async Task TheFirstClaimLineOfANewMonthAttachesToItsOwnPeriod()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var billing = fixture.BillingAs(fixture.AdminOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Approved, fixture.BillableDate);

        var line = await billing.CreateClaimLineAsync(noteId);

        await using var db = fixture.Factory.CreateDbContext();
        var period = await db.BillingPeriods.Include(p => p.Lines)
            .SingleAsync(p => p.UserId == fixture.CaseManagerOne.Id &&
                p.Month == fixture.BillableDate.Month && p.Year == fixture.BillableDate.Year);
        Assert.NotEqual(0, line.BillingPeriodId);
        Assert.Equal(period.Id, line.BillingPeriodId);
        Assert.Single(period.Lines);
        Assert.Equal(BillingStatus.Draft, period.Status);
    }

    [Fact]
    public async Task ANoteCanBeClaimedOnlyOnce()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var billing = fixture.BillingAs(fixture.AdminOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Approved, fixture.BillableDate);

        await billing.CreateClaimLineAsync(noteId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            billing.CreateClaimLineAsync(noteId));

        await using var db = fixture.Factory.CreateDbContext();
        Assert.Equal(1, await db.ClaimLines.CountAsync(line => line.NoteId == noteId));
    }

    [Fact]
    public async Task ASubmittedPeriodAcceptsNoFurtherClaimLines()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var billing = fixture.BillingAs(fixture.AdminOne);
        var first = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Approved, fixture.BillableDate);
        // Both notes must land in the SAME billing period, because that is what the guard
        // is about. A period is keyed by the service date's month and year, so a blind
        // AddDays(1) opens a fresh draft period whenever BillableDate (DateTime.Today) is
        // the last day of a month, and the test then passes for the wrong reason — it fails
        // outright, as it did on 2026-08-31.
        var secondDate = fixture.BillableDate.Day == 1
            ? fixture.BillableDate.AddDays(1)
            : fixture.BillableDate.AddDays(-1);
        var second = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Approved, secondDate);

        var line = await billing.CreateClaimLineAsync(first);
        await billing.SubmitBillingPeriodAsync(line.BillingPeriodId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            billing.CreateClaimLineAsync(second));

        // Submitting again is a no-op rather than an error, so a retried command
        // cannot double-submit a period.
        await billing.SubmitBillingPeriodAsync(line.BillingPeriodId);
        await using var db = fixture.Factory.CreateDbContext();
        var period = await db.BillingPeriods.SingleAsync(p => p.Id == line.BillingPeriodId);
        Assert.Equal(BillingStatus.Submitted, period.Status);
        Assert.NotNull(period.SubmittedAt);
    }

    [Fact]
    public async Task ASubmittedPeriodReturnsToDraftOnlyBeforeExchangeHistoryExists()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var billing = fixture.BillingAs(fixture.AdminOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Approved, fixture.BillableDate);
        var line = await billing.CreateClaimLineAsync(noteId);
        await billing.SubmitBillingPeriodAsync(line.BillingPeriodId);

        await billing.ReturnBillingPeriodToDraftAsync(line.BillingPeriodId);

        await using (var verification = fixture.Factory.CreateDbContext())
        {
            var returned = await verification.BillingPeriods.SingleAsync(
                period => period.Id == line.BillingPeriodId);
            Assert.Equal(BillingStatus.Draft, returned.Status);
            Assert.Null(returned.SubmittedAt);
            Assert.True(await verification.AuditEvents.AnyAsync(item =>
                item.Action == "billing-period.returned-to-draft" &&
                item.ResourceId == line.BillingPeriodId.ToString()));
        }

        await billing.SubmitBillingPeriodAsync(line.BillingPeriodId);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.BillingSubmissionEvents.Add(new BillingSubmissionEvent
            {
                AgencyId = fixture.AdminOne.AgencyId,
                BillingPeriodId = line.BillingPeriodId,
                OccurredAtUtc = DateTime.UtcNow,
                Stage = BillingSubmissionStage.Generated
            });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            billing.ReturnBillingPeriodToDraftAsync(line.BillingPeriodId));
    }

    [Fact]
    public async Task AnEmptyPeriodCannotBeSubmitted()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var billing = fixture.BillingAs(fixture.AdminOne);
        var period = await billing.GetOrCreateBillingPeriodAsync(
            fixture.CaseManagerOne.Id, fixture.BillableDate.Month, fixture.BillableDate.Year);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            billing.SubmitBillingPeriodAsync(period.Id));
    }

    [Fact]
    public async Task APeriodWithoutFrozenClaimDetailsCannotBeSubmitted()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var billing = fixture.BillingAs(fixture.AdminOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Approved, fixture.BillableDate);
        var line = await billing.CreateClaimLineAsync(noteId);

        await using (var db = fixture.Factory.CreateDbContext())
        {
            var storedLine = await db.ClaimLines.SingleAsync(candidate => candidate.Id == line.Id);
            storedLine.ClaimSnapshotJson = null;
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            billing.SubmitBillingPeriodAsync(line.BillingPeriodId));

        Assert.Contains("not ready to submit", error.Message, StringComparison.OrdinalIgnoreCase);
        await using var verification = fixture.Factory.CreateDbContext();
        var period = await verification.BillingPeriods.SingleAsync(candidate => candidate.Id == line.BillingPeriodId);
        Assert.Equal(BillingStatus.Draft, period.Status);
    }

    [Fact]
    public async Task TheDocumentedOverrideTravelsFromTheNoteOntoTheClaim()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var supervisor = fixture.SupervisionAs(fixture.SupervisorOne);
        var billing = fixture.BillingAs(fixture.AdminOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Logged, fixture.BillableDate);

        await supervisor.ApproveWithOverrideAsync(noteId, fixture.SupervisorOne.Id,
            "Documented supervisory exception.", await fixture.RevisionOfAsync(noteId));

        // The caller asks for no exception; the note's record decides anyway.
        var line = await billing.CreateClaimLineAsync(noteId, false, null);

        Assert.True(line.IsComplianceException);
        Assert.Equal("Documented supervisory exception.", line.ComplianceExceptionReason);
    }

    [Fact]
    public async Task BillingUsesTheCurrentPermissionInsteadOfTheRoleLabel()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var billingNote = await fixture.SeedNoteAsync(
            fixture.PersonOneId, NoteStatus.Approved, fixture.BillableDate);
        var deniedNote = await fixture.SeedNoteAsync(
            fixture.PersonPeerId, NoteStatus.Approved, fixture.BillableDate.AddDays(-1));

        await using (var db = fixture.Factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(user => user.Id == fixture.CaseManagerOne.Id)).Permissions =
                UserPermissions.Billing;
            (await db.Users.SingleAsync(user => user.Id == fixture.AdminOne.Id)).Permissions =
                UserPermissions.Administration;
            await db.SaveChangesAsync();
        }

        var service = new BillingService(fixture.Factory);
        var billingActor = new AgencyActor(
            fixture.CaseManagerOne.Id, fixture.CaseManagerOne.AgencyId, UserPermissions.Billing);
        var adminWithoutBilling = new AgencyActor(
            fixture.AdminOne.Id, fixture.AdminOne.AgencyId, UserPermissions.Administration);

        Assert.Equal(billingNote,
            (await service.CreateClaimLineAsync(billingActor, billingNote)).NoteId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateClaimLineAsync(adminWithoutBilling, deniedNote));

        await using (var db = fixture.Factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(user => user.Id == fixture.CaseManagerOne.Id)).Permissions =
                UserPermissions.None;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetApprovedUnbilledNotesAsync(billingActor));
    }

    [Fact]
    public async Task BillingNeverCrossesAnAgencyBoundary()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var foreignNote = await fixture.SeedNoteAsync(
            fixture.PersonTwoId, NoteStatus.Approved, fixture.BillableDate);

        var billing = fixture.BillingAs(fixture.AdminOne);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            billing.CreateClaimLineAsync(foreignNote));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            billing.GetOrCreateBillingPeriodAsync(
                fixture.CaseManagerTwo.Id, fixture.BillableDate.Month, fixture.BillableDate.Year));

        var unbilled = await billing.GetApprovedUnbilledNotesAsync();
        Assert.DoesNotContain(unbilled, note => note.Id == foreignNote);
    }

    [Fact]
    public async Task AnApprovedNoteMissingBillingIdentityIsRefused()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var billing = fixture.BillingAs(fixture.AdminOne);
        var noteId = await fixture.SeedNoteAsync(
            fixture.PersonIncompleteId, NoteStatus.Approved, fixture.BillableDate);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            billing.CreateClaimLineAsync(noteId));
        Assert.Contains("MaineCare", failure.Message, StringComparison.OrdinalIgnoreCase);

        await using var db = fixture.Factory.CreateDbContext();
        Assert.Equal(0, await db.ClaimLines.CountAsync(line => line.NoteId == noteId));
    }

    // ---------------------------------------------------------------------
    // The whole pipeline
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ANoteTravelsFromDraftToASubmittedClaim()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var notes = fixture.NotesAs(fixture.CaseManagerOne);
        var supervisor = fixture.SupervisionAs(fixture.SupervisorOne);
        var billing = fixture.BillingAs(fixture.AdminOne);

        var created = await notes.AddNoteAsync(Note.Create(
            "Community support contact.", fixture.BillableDate, NoteStatus.Pending, 60,
            fixture.PersonOneId));

        var submitting = await fixture.DetachedNoteAsync(created.Id);
        submitting.Status = NoteStatus.Logged;
        await notes.UpdateNoteAsync(submitting);

        var pending = await supervisor.GetPendingNotesAsync(fixture.SupervisorOne.Id);
        Assert.Contains(pending, note => note.Id == created.Id);

        await supervisor.ApproveNoteAsync(created.Id, fixture.SupervisorOne.Id,
            await fixture.RevisionOfAsync(created.Id));

        var unbilled = await billing.GetApprovedUnbilledNotesAsync();
        Assert.Contains(unbilled, note => note.Id == created.Id);

        var line = await billing.CreateClaimLineAsync(created.Id);
        await billing.SubmitBillingPeriodAsync(line.BillingPeriodId);

        Assert.Equal(4m, line.Units);
        Assert.Equal(100m, line.ChargeAmount);
        Assert.Equal("G9012", line.ProcedureCode);
        Assert.False(line.IsComplianceException);

        // Once claimed, the note is no longer an unbilled candidate.
        Assert.DoesNotContain(
            await billing.GetApprovedUnbilledNotesAsync(),
            note => note.Id == created.Id);

        await using var db = fixture.Factory.CreateDbContext();
        var actions = await db.AuditEvents.OrderBy(e => e.Id)
            .Select(e => e.Action).ToListAsync();
        Assert.Equal(
            ["note.created", "note.updated", "note.approved", "billing-claim-line.created",
             "billing-period.submitted"],
            actions);
    }

    [Fact]
    public async Task ReviewPagingRemainsStableWhenEarlierRowsAreApproved()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        for (var i = 0; i < 23; i++)
            await fixture.SeedNoteAsync(fixture.PersonOneId, NoteStatus.Logged, fixture.ServiceDate(i));
        await fixture.SeedNoteAsync(fixture.PersonTwoId, NoteStatus.Logged, fixture.ServiceDate(0));
        var service = fixture.SupervisionAs(fixture.SupervisorOne);
        var filter = new NoteReviewQuery(UserId: fixture.CaseManagerOne.Id);
        var first = await service.GetReviewPageAsync(fixture.SupervisorOne.Id, filter: filter);
        Assert.Equal(10, first.Notes.Count);
        Assert.NotNull(first.NextAfterId);
        Assert.All(first.Notes, note => Assert.Equal(fixture.PersonOneId, note.PersonId));
        foreach (var note in first.Notes)
            await service.ApproveNoteAsync(note.Id, fixture.SupervisorOne.Id, note.Revision);
        var addedLater = await fixture.SeedNoteAsync(fixture.PersonOneId, NoteStatus.Logged, fixture.ServiceDate(0));
        var second = await service.GetReviewPageAsync(fixture.SupervisorOne.Id, first.NextAfterId!.Value,
            first.ThroughId, filter);
        var third = await service.GetReviewPageAsync(fixture.SupervisorOne.Id, second.NextAfterId!.Value,
            first.ThroughId, filter);
        Assert.Equal(10, second.Notes.Count);
        Assert.Equal(3, third.Notes.Count);
        Assert.Null(third.NextAfterId);
        var ids = first.Notes.Concat(second.Notes).Concat(third.Notes).Select(note => note.Id).ToList();
        Assert.Equal(ids.OrderByDescending(id => id), ids);
        Assert.Equal(23, ids.Distinct().Count());
        Assert.DoesNotContain(addedLater, ids);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.SupervisionAs(fixture.CaseManagerOne).GetReviewPageAsync(fixture.CaseManagerOne.Id));
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(61, false)]
    [InlineData(0, false)]
    public async Task ThresholdApprovalChecksStoredDurationAndPreservesAudit(int minutes, bool allowed)
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var id = await fixture.SeedNoteAsync(fixture.PersonOneId, NoteStatus.Logged, fixture.ServiceDate(0));
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var row = await db.Notes.SingleAsync(n => n.Id == id);
            row.Minutes = minutes;
            row.NoteType = NoteType.Contact;
            await db.SaveChangesAsync();
        }
        var service = fixture.SupervisionAs(fixture.SupervisorOne);
        var revision = await fixture.RevisionOfAsync(id);
        if (allowed)
        {
            await service.ApproveNoteAsync(id, fixture.SupervisorOne.Id, revision, 4);
            Assert.Equal(NoteStatus.Approved, await fixture.StatusOfAsync(id));
            await using var db = fixture.Factory.CreateDbContext();
            var audit = await db.AuditEvents.SingleAsync(e => e.Action == "note.approved");
            Assert.Contains("\"maximumUnits\":4", audit.MetadataJson);
            await Assert.ThrowsAsync<NoteConcurrencyException>(() =>
                service.ApproveNoteAsync(id, fixture.SupervisorOne.Id, revision, 4));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ApproveNoteAsync(id, fixture.SupervisorOne.Id, revision, 4));
            Assert.Equal(NoteStatus.Logged, await fixture.StatusOfAsync(id));
        }
    }

    // ---------------------------------------------------------------------
    // Fixture
    // ---------------------------------------------------------------------

    private sealed class PipelineFixture : IAsyncDisposable
    {
        private const int AgencyOne = 101;
        private const int AgencyTwo = 102;

        private readonly SqliteConnection _connection;

        private PipelineFixture(SqliteConnection connection, DbContextOptions<SatiContext> options)
        {
            _connection = connection;
            Factory = new PipelineContextFactory(options);
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public User CaseManagerOne { get; private set; } = null!;
        public User CaseManagerPeer { get; private set; } = null!;
        public User CaseManagerTwo { get; private set; } = null!;
        public User SupervisorOne { get; private set; } = null!;
        public User SupervisorTwo { get; private set; } = null!;
        public User AdminOne { get; private set; } = null!;
        public int PersonOneId { get; private set; }
        public int PersonPeerId { get; private set; }
        public int PersonTwoId { get; private set; }
        public int PersonIncompleteId { get; private set; }

        /// <summary>A date inside the current compliance cycle and billing month.</summary>
        public DateTime BillableDate { get; } = DateTime.Today;

        /// <summary>
        /// Distinct dates so that seeded notes never contend for the same service
        /// minutes. Overlap has its own rules and its own tests.
        /// </summary>
        public DateTime ServiceDate(int offset) => DateTime.Today.AddDays(-offset);

        public static async Task<PipelineFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
            var fixture = new PipelineFixture(connection, options);
            await fixture.SeedAsync();
            return fixture;
        }

        public INoteService NotesAs(User user) => new NoteService(Factory, SessionFor(user));

        public ISupervisorService SupervisionAs(User user) =>
            new SupervisorService(Factory, SessionFor(user));

        public ActorBoundBillingService BillingAs(User user) =>
            new(new BillingService(Factory), user.ToAgencyActor());

        private static ISessionService SessionFor(User user)
        {
            var session = new SessionService();
            session.SetUser(user);
            return session;
        }

        public sealed class ActorBoundBillingService(
            IBillingService service,
            AgencyActor actor)
        {
            public Task<ClaimLine> CreateClaimLineAsync(
                int noteId,
                bool isComplianceException = false,
                string? complianceExceptionReason = null) =>
                service.CreateClaimLineAsync(
                    actor, noteId, isComplianceException, complianceExceptionReason);

            public Task<BillingPeriod> GetOrCreateBillingPeriodAsync(
                int userId,
                int month,
                int year) => service.GetOrCreateBillingPeriodAsync(actor, userId, month, year);

            public Task SubmitBillingPeriodAsync(int billingPeriodId) =>
                service.SubmitBillingPeriodAsync(actor, billingPeriodId);

            public Task ReturnBillingPeriodToDraftAsync(int billingPeriodId) =>
                service.ReturnBillingPeriodToDraftAsync(actor, billingPeriodId);

            public Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync() =>
                service.GetApprovedUnbilledNotesAsync(actor);
        }

        public async Task<int> SeedNoteAsync(int personId, NoteStatus status, DateTime eventDate)
        {
            await using var db = Factory.CreateDbContext();
            var person = await db.People.SingleAsync(candidate => candidate.Id == personId);
            var note = Note.Create("Seeded note", eventDate, status, 60, personId);
            note.AgencyId = person.AgencyId;
            if (status == NoteStatus.Approved)
            {
                note.ApprovedById = SupervisorOne.Id;
                note.ApprovedAt = DateTime.UtcNow;
            }
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            return note.Id;
        }

        public async Task<Note> NoteAsync(int noteId)
        {
            await using var db = Factory.CreateDbContext();
            return await db.Notes.AsNoTracking().SingleAsync(note => note.Id == noteId);
        }

        /// <summary>A detached copy, the way a client hands one back for an update.</summary>
        public Task<Note> DetachedNoteAsync(int noteId) => NoteAsync(noteId);

        public async Task<NoteStatus?> StatusOfAsync(int noteId) => (await NoteAsync(noteId)).Status;

        public async Task<int> RevisionOfAsync(int noteId) => (await NoteAsync(noteId)).Revision;

        public async Task<string> NarrativeOfAsync(int noteId) => (await NoteAsync(noteId)).Narrative;

        public async Task<bool> NoteExistsAsync(int noteId)
        {
            await using var db = Factory.CreateDbContext();
            return await db.Notes.AnyAsync(note => note.Id == noteId);
        }

        private async Task SeedAsync()
        {
            await using var db = Factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();

            // Agencies 1 and 2 are seeded by the model itself, so this fixture
            // takes its own ids rather than colliding with them.
            db.Agencies.AddRange(BillableAgency(AgencyOne, "Agency One"),
                BillableAgency(AgencyTwo, "Agency Two"));

            SupervisorOne = User.Create(11, "supervisor-one", "Supervisor One", "hash", "salt",
                UserRole.Supervisor, null, AgencyOne);
            SupervisorTwo = User.Create(21, "supervisor-two", "Supervisor Two", "hash", "salt",
                UserRole.Supervisor, null, AgencyTwo);
            AdminOne = User.Create(12, "admin-one", "Admin One", "hash", "salt",
                UserRole.Admin, null, AgencyOne);
            CaseManagerOne = User.Create(13, "case-manager-one", "Case Manager One", "hash", "salt",
                UserRole.CaseManager, SupervisorOne.Id, AgencyOne);
            CaseManagerPeer = User.Create(14, "case-manager-peer", "Case Manager Peer", "hash", "salt",
                UserRole.CaseManager, SupervisorOne.Id, AgencyOne);
            CaseManagerTwo = User.Create(22, "case-manager-two", "Case Manager Two", "hash", "salt",
                UserRole.CaseManager, SupervisorTwo.Id, AgencyTwo);
            db.Users.AddRange(SupervisorOne, SupervisorTwo, AdminOne,
                CaseManagerOne, CaseManagerPeer, CaseManagerTwo);

            var personOne = BillablePerson(CaseManagerOne.Id, AgencyOne, "Owned");
            var personPeer = BillablePerson(CaseManagerPeer.Id, AgencyOne, "Peer");
            var personTwo = BillablePerson(CaseManagerTwo.Id, AgencyTwo, "Foreign");
            var personIncomplete = BillablePerson(CaseManagerOne.Id, AgencyOne, "Incomplete");
            personIncomplete.MaineCareId = null;
            db.People.AddRange(personOne, personPeer, personTwo, personIncomplete);
            await db.SaveChangesAsync();

            PersonOneId = personOne.Id;
            PersonPeerId = personPeer.Id;
            PersonTwoId = personTwo.Id;
            PersonIncompleteId = personIncomplete.Id;
        }

        private static Agency BillableAgency(int id, string name) => new()
        {
            Id = id,
            Name = name,
            // A valid NPI, check digit included; BillingRules verifies it.
            Npi = "1999999984",
            TaxId = "111111111",
            Street = "1 First Street",
            City = "Portland",
            State = "ME",
            Zip = "04101",
            BillingProcedureCode = "G9012",
            BillingModifier = "HI",
            BillingUnitRate = 25m,
            EdiSubmitterId = "SATITEST1",
            EdiPayerName = "MEDICAID MAINE",
            EdiPayerId = "MCDME",
            EdiContactName = "Test Billing",
            EdiContactPhone = "2075550101"
        };

        private static Person BillablePerson(int userId, int agencyId, string firstName)
        {
            // Dates are relative to today so the compliance cycle stays current
            // however long from now the suite is run.
            var effective = DateTime.Today.AddMonths(-1);
            var person = Person.CreatePerson(userId, firstName, "Person", string.Empty,
                new DateTime(1990, 1, 1), effective, WaiverType.Section21, new Settings());
            person.AgencyId = agencyId;
            person.Gender = Gender.Unknown;
            person.MaineCareId = $"MC{userId}{agencyId}{firstName.Length}";
            person.DiagnosisCode = "F89";
            person.PlaceOfService = (int)PlaceOfService.Office;
            person.BillingStreet = "10 Test Street";
            person.BillingCity = "Portland";
            person.BillingState = "ME";
            person.BillingZip = "04101";
            person.Forms = CompliantForms(effective);
            return person;
        }

        /// <summary>
        /// The four required annual documents, compliant, due later in the current
        /// cycle. No review is due yet, so the compliance gate passes cleanly.
        /// </summary>
        private static List<Form> CompliantForms(DateTime effective)
        {
            var dueDate = effective.AddMonths(6);
            var forms = new List<Form>();
            foreach (var type in new[]
                     {
                         FormType.PCP, FormType.ComprehensiveAssessment,
                         FormType.Reclassification, FormType.SafetyPlan
                     })
            {
                var form = new Form(type, dueDate, DateTime.Today);
                form.SetInitialCompletion(effective);
                forms.Add(form);
            }
            return forms;
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class PipelineContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);

        public Task<SatiContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
