using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class FormAttestationRulesTests
{
    [Fact]
    public async Task SavingAFormNoteDoesNotCompleteTheMatchingForm()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = DateTime.Today.AddMonths(-6);
            var form = new Form(FormType.Q2R, DateTime.Today.AddDays(30))
            {
                PersonId = fixture.PersonOneId
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
        }

        var note = Note.Create(
            "Evidence narrative that must not mutate the form.",
            DateTime.Today,
            NoteStatus.Pending,
            15,
            fixture.PersonOneId,
            FormType.Q2R,
            NoteType.Form);

        await fixture.NotesFromAnotherSession().AddNoteAsync(note);

        await using var verification = fixture.Factory.CreateDbContext();
        var stored = await verification.Forms.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == formId);
        Assert.Null(stored.CompletedDate);
        Assert.False(stored.IsCompliant);
        Assert.Empty(await verification.FormAttestations
            .Where(candidate => candidate.FormId == formId)
            .ToListAsync());
    }

    [Fact]
    public void PendingListUsesTheNotesPersonAndEventDateCycle()
    {
        var effective = new DateTime(2025, 1, 1);
        var noteDate = new DateTime(2025, 2, 10);
        var notes = new[]
        {
            new NoteFact(71, 22, "Q1R", noteDate, "Logged")
        };
        var forms = new[]
        {
            new FormFact(101, 99, "Q1R", new DateTime(2025, 4, 1), null),
            new FormFact(102, 22, "Q1R", new DateTime(2025, 4, 1), null),
            new FormFact(103, 22, "Q1R", new DateTime(2026, 4, 1), null)
        };

        var pending = Assert.Single(FormAttestationRules.PendingAttestations(
            notes, forms, effective, new DateTime(2026, 9, 3)));

        Assert.Equal(102, pending.FormId);
        Assert.Equal(22, pending.PersonId);
        Assert.Equal(new DateTime(2025, 1, 1), pending.CycleStart);
        Assert.Equal(new DateTime(2026, 1, 1), pending.CycleEnd);
        Assert.Equal(71, pending.EvidenceNoteId);
    }

    [Fact]
    public void AttestationBeforeItsOwnCycleIsRejected()
    {
        var decision = FormAttestationRules.Evaluate(
            "Q1R",
            new DateTime(2026, 1, 31),
            new DateTime(2026, 2, 1),
            new DateTime(2026, 9, 3),
            AttestationActorKind.CaseManager,
            []);

        Assert.False(decision.Accepted);
        Assert.Equal(FormAttestationRules.BeforeCycleMessage, decision.DateError);
    }

    [Fact]
    public void ReleaseRequiresALiveNonDraftArtifact()
    {
        var cycleStart = new DateTime(2026, 1, 1);
        var draft = new ArtifactFact(
            41, 7, AnnualDocumentKind.ReleaseMedical.ToString(), cycleStart, IsDraft: true);

        var withoutArtifact = FormAttestationRules.Evaluate(
            "Release_Medical", new DateTime(2026, 2, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.CaseManager, []);
        var withDraft = FormAttestationRules.Evaluate(
            "Release_Medical", new DateTime(2026, 2, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.CaseManager, [draft]);
        var withFinished = FormAttestationRules.Evaluate(
            "Release_Medical", new DateTime(2026, 2, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.CaseManager,
            [draft with { ArtifactId = 42, IsDraft = false }]);

        Assert.False(withoutArtifact.Accepted);
        Assert.False(withDraft.Accepted);
        Assert.True(withFinished.Accepted);
    }

    [Fact]
    public void ReclassificationRequiresACompletedComprehensiveAssessmentInTheSameCycle()
    {
        var cycleStart = new DateTime(2026, 1, 1);
        var assessment = new FormFact(
            81, 7, "ComprehensiveAssessment", new DateTime(2026, 2, 1), null);

        var incomplete = FormAttestationRules.Evaluate(
            "Reclassification", new DateTime(2026, 3, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.CaseManager, [], [assessment]);
        var completed = FormAttestationRules.Evaluate(
            "Reclassification", new DateTime(2026, 3, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.CaseManager, [],
            [assessment with { CompletedDate = new DateTime(2026, 2, 2) }]);

        Assert.False(incomplete.Accepted);
        Assert.True(completed.Accepted);
    }

    [Fact]
    public void OnlyASupervisorWithAReasonCanOverrideAMissingPrerequisite()
    {
        var cycleStart = new DateTime(2026, 1, 1);

        var caseManager = FormAttestationRules.Evaluate(
            "Release_Agency", new DateTime(2026, 3, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.CaseManager, [], [],
            "PDF service was unavailable.");
        var supervisorWithoutReason = FormAttestationRules.Evaluate(
            "Release_Agency", new DateTime(2026, 3, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.Supervisor, []);
        var supervisor = FormAttestationRules.Evaluate(
            "Release_Agency", new DateTime(2026, 3, 1), cycleStart,
            new DateTime(2026, 9, 3), AttestationActorKind.Supervisor, [], [],
            "PDF service was unavailable.");

        Assert.False(caseManager.Accepted);
        Assert.False(supervisorWithoutReason.Accepted);
        Assert.True(supervisor.Accepted);
        Assert.True(supervisor.SupervisorOverrideAccepted);
    }

    [Fact]
    public void RemovingEvidenceFromThePendingProjectionDoesNotRevokeAnAttestation()
    {
        var completedOn = new DateTime(2026, 3, 15);
        var form = new Form(FormType.Q1R, new DateTime(2026, 4, 1));
        form.Attest(FormAttestation.Attested(
            completedOn,
            AttestationActorKind.CaseManager,
            actorUserId: 31,
            recordedAtUtc: DateTime.UtcNow,
            evidenceNoteId: 71));

        var pending = FormAttestationRules.PendingAttestations(
            [],
            [new FormFact(101, 22, "Q1R", form.DueDate, form.CompletedDate)],
            new DateTime(2026, 1, 1),
            new DateTime(2026, 9, 3));

        Assert.Empty(pending);
        Assert.Equal(completedOn, form.CompletedDate);
        Assert.Single(form.Attestations);
        Assert.Equal(71, form.Attestations[0].EvidenceNoteId);
    }

    [Fact]
    public async Task LocalAttestationAndRevocationAppendLedgerRowsAndAuditEvents()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        int formId;
        Form detached;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = DateTime.Today.AddMonths(-6);
            var form = new Form(FormType.Q3R, DateTime.Today.AddDays(20))
            {
                PersonId = fixture.PersonOneId
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
            detached = await db.Forms.AsNoTracking().SingleAsync(candidate => candidate.Id == formId);
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new FormService(fixture.Factory, session);
        var completedOn = DateTime.Today.AddDays(-2);

        await service.AttestAsync(detached, completedOn);
        await service.RevokeAttestationAsync(detached, "Entered against the wrong cycle.");

        await using var verification = fixture.Factory.CreateDbContext();
        var stored = await verification.Forms.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == formId);
        var ledger = await verification.FormAttestations.AsNoTracking()
            .Where(candidate => candidate.FormId == formId)
            .OrderBy(candidate => candidate.Id)
            .ToListAsync();
        var audits = await verification.AuditEvents.AsNoTracking()
            .Where(candidate => candidate.ResourceType == "Form" && candidate.ResourceId == formId.ToString())
            .OrderBy(candidate => candidate.Id)
            .ToListAsync();

        Assert.Null(stored.CompletedDate);
        Assert.Collection(
            ledger,
            attestation =>
            {
                Assert.Equal(FormAttestationKind.Attested, attestation.Kind);
                Assert.Equal(completedOn, attestation.CompletedOn);
                Assert.Equal(fixture.CaseManagerOne.Id, attestation.ActorUserId);
                Assert.Equal(
                    FormAttestationRules.NoPrerequisitesStateJson,
                    attestation.PrerequisiteStateJson);
            },
            revocation =>
            {
                Assert.Equal(FormAttestationKind.Revoked, revocation.Kind);
                Assert.Equal("Entered against the wrong cycle.", revocation.Reason);
            });
        Assert.Contains(audits, candidate => candidate.Action == "form.attested");
        Assert.Contains(audits, candidate => candidate.Action == "form.attestation-revoked");
        Assert.DoesNotContain(audits, candidate => candidate.MetadataJson.Contains("wrong cycle", StringComparison.OrdinalIgnoreCase));

        verification.FormAttestations.Remove(ledger[0]);
        var appendOnly = await Assert.ThrowsAsync<InvalidOperationException>(
            () => verification.SaveChangesAsync());
        Assert.Contains("append-only", appendOnly.Message, StringComparison.OrdinalIgnoreCase);
    }
}
