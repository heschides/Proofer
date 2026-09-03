using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using System.Text.Json;

namespace Sati.Data;

public sealed class FormService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IFormService
{
    public async Task UpdateFormAsync(Form form)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);

        if (stored.CompletedDate?.Date != form.CompletedDate?.Date)
        {
            throw new InvalidOperationException(
                "A completion date can be changed only through an attestation or revocation.");
        }

        stored.OpenedDate = form.OpenedDate?.Date;
        await context.SaveChangesAsync();
        form.OpenedDate = stored.OpenedDate;
    }

    public Task AttestAsync(Form form, DateTime completedOn, int? evidenceNoteId = null) =>
        AttestAsync(form, completedOn, evidenceNoteId, supervisorOverrideReason: null);

    public async Task AttestAsync(
        Form form,
        DateTime completedOn,
        int? evidenceNoteId,
        string? supervisorOverrideReason)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);
        if (stored.CompletedDate is not null)
            throw new InvalidOperationException(
                "This form already has a live attestation. Revoke it before recording a replacement.");
        var cycle = FormAttestationRules.ResolveCycle(
            stored.Person.EffectiveDate
                ?? throw new InvalidOperationException("The consumer has no effective date."),
            stored.DueDate)
            ?? throw new InvalidOperationException("The form is not attached to a valid compliance cycle.");
        var actorKind = actor.Id == stored.Person.UserId
            ? AttestationActorKind.CaseManager
            : AttestationActorKind.Supervisor;
        var artifacts = await LoadArtifactFactsAsync(
            context, stored.PersonId, cycle.CycleStart, cancellationToken: default);
        var formFacts = await LoadFormFactsAsync(context, stored.PersonId);
        var decision = FormAttestationRules.Evaluate(
            stored.Type.ToString(), completedOn, cycle.CycleStart, DateTime.Today,
            actorKind, artifacts, formFacts, supervisorOverrideReason);
        if (!decision.Accepted)
        {
            if (decision.DateError is not null)
                throw new ArgumentOutOfRangeException(nameof(completedOn), decision.DateError);
            throw new InvalidOperationException(string.Join(" ",
                decision.UnmetPrerequisites.Select(prerequisite => prerequisite.Message)));
        }

        if (evidenceNoteId is int noteId)
        {
            var evidenceIsValid = await context.Notes.AsNoTracking().AnyAsync(note =>
                note.Id == noteId &&
                note.PersonId == stored.PersonId &&
                note.FormType == stored.Type &&
                note.EventDate != null &&
                note.EventDate.Value.Date >= cycle.CycleStart.Date &&
                note.EventDate.Value.Date < cycle.CycleEnd.Date &&
                (note.Status == NoteStatus.Pending ||
                 note.Status == NoteStatus.Logged ||
                 note.Status == NoteStatus.Approved));
            if (!evidenceIsValid)
                throw new ArgumentException("The cited note is not matching form evidence.", nameof(evidenceNoteId));
        }

        var prerequisiteArtifactIds = MatchingArtifactIds(stored.Type.ToString(), artifacts);
        var prerequisiteStateJson = FormAttestationRules.PrerequisiteStateJson(
            decision, prerequisiteArtifactIds, supervisorOverrideReason);
        var attestation = FormAttestation.Attested(
            completedOn,
            actorKind,
            actor.Id,
            DateTime.UtcNow,
            evidenceNoteId,
            prerequisiteStateJson: prerequisiteStateJson,
            reason: decision.SupervisorOverrideAccepted ? supervisorOverrideReason : null);
        stored.Attest(attestation);
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.FormAttested,
            "Form",
            stored.Id,
            JsonSerializer.Serialize(new
            {
                formType = stored.Type.ToString(),
                cycleStart = cycle.CycleStart.ToString("yyyy-MM-dd"),
                completedOn = completedOn.Date.ToString("yyyy-MM-dd"),
                actorKind = actorKind.ToString(),
                prerequisiteArtifactIds,
                supervisorOverride = decision.SupervisorOverrideAccepted
            }));
        if (decision.SupervisorOverrideAccepted)
        {
            LocalAuditTrail.Record(
                context,
                actor,
                LocalAuditActions.FormPrerequisiteOverridden,
                "Form",
                stored.Id,
                JsonSerializer.Serialize(new
                {
                    formType = stored.Type.ToString(),
                    cycleStart = cycle.CycleStart.ToString("yyyy-MM-dd"),
                    unmetPrerequisites = decision.UnmetPrerequisites
                        .Select(item => item.Kind.ToString())
                        .Distinct()
                        .Order()
                        .ToArray()
                }));
        }
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException(
                "This form's attestation changed in another session. Refresh it and try again.",
                exception);
        }
        form.Attest(FormAttestation.Attested(
            stored.CompletedDate!.Value,
            actorKind,
            actor.Id,
            attestation.RecordedAtUtc,
            evidenceNoteId,
            prerequisiteStateJson,
            decision.SupervisorOverrideAccepted ? supervisorOverrideReason : null));
    }

    public async Task<FormPrerequisiteStatusDto> GetPrerequisiteStatusAsync(Form form)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);
        var cycle = FormAttestationRules.ResolveCycle(
            stored.Person.EffectiveDate ?? throw new InvalidOperationException("The consumer has no effective date."),
            stored.DueDate)
            ?? throw new InvalidOperationException("The form is not attached to a valid compliance cycle.");
        var artifacts = await LoadArtifactFactsAsync(context, stored.PersonId, cycle.CycleStart, default);
        var forms = await LoadFormFactsAsync(context, stored.PersonId);
        var actorKind = actor.Id == stored.Person.UserId
            ? AttestationActorKind.CaseManager
            : AttestationActorKind.Supervisor;
        var decision = FormAttestationRules.Evaluate(
            stored.Type.ToString(), DateTime.Today, cycle.CycleStart, DateTime.Today,
            actorKind, artifacts, forms);
        var prerequisite = FormAttestationRules.PrerequisiteFor(stored.Type.ToString());
        return new FormPrerequisiteStatusDto(
            prerequisite.ToString(),
            decision.UnmetPrerequisites.Count == 0,
            decision.UnmetPrerequisites.Count == 0
                ? prerequisite == PrerequisiteKind.None
                    ? "No additional document prerequisite applies."
                    : "The prerequisite is satisfied."
                : string.Join(" ", decision.UnmetPrerequisites.Select(item => item.Message)),
            MatchingArtifactIds(stored.Type.ToString(), artifacts),
            actorKind == AttestationActorKind.Supervisor);
    }

    public async Task<DocumentArtifactDto> RecordExternalPrerequisiteAsync(Form form, string note)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);
        var entry = AnnualDocumentCatalog.ForFormType(stored.Type.ToString())
            ?? throw new InvalidOperationException("This form does not have an external-document prerequisite.");
        var cycle = FormAttestationRules.ResolveCycle(
            stored.Person.EffectiveDate ?? throw new InvalidOperationException("The consumer has no effective date."),
            stored.DueDate)
            ?? throw new InvalidOperationException("The form is not attached to a valid compliance cycle.");
        var artifact = await DocumentArtifactStore.StageExternalAsync(
            context, stored.PersonId, actor.AgencyId, entry.Kind, cycle.CycleStart,
            DateTime.UtcNow, actor.Id, note, default);
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.DocumentRecordedExternal,
            "Person",
            stored.PersonId,
            JsonSerializer.Serialize(new
            {
                kind = entry.Kind.ToString(),
                cycleStart = cycle.CycleStart.ToString("yyyy-MM-dd")
            }));
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return DocumentArtifactStore.ToDto(artifact);
    }

    public async Task RevokeAttestationAsync(Form form, string reason)
    {
        var actor = CurrentCaseManager();
        await using var context = await contextFactory.CreateDbContextAsync();
        var stored = await LoadOwnedFormAsync(context, actor, form.Id);
        if (stored.CompletedDate is null)
            return;

        var actorKind = actor.Id == stored.Person.UserId
            ? AttestationActorKind.CaseManager
            : AttestationActorKind.Supervisor;
        var revocation = FormAttestation.Revoked(
            actorKind, actor.Id, DateTime.UtcNow, reason);
        stored.RevokeAttestation(revocation);
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.FormAttestationRevoked,
            "Form",
            stored.Id,
            JsonSerializer.Serialize(new
            {
                formType = stored.Type.ToString(),
                actorKind = actorKind.ToString()
            }));
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException(
                "This form's attestation changed in another session. Refresh it and try again.",
                exception);
        }
        form.RevokeAttestation(FormAttestation.Revoked(
            actorKind,
            actor.Id,
            revocation.RecordedAtUtc,
            reason));
    }

    public async Task OpenFormAsync(Form form)
    {
        form.OpenedDate = DateTime.Today;
        await UpdateFormAsync(form);
    }

    public async Task DeleteFormsAsync(IEnumerable<Form> forms)
    {
        var actor = CurrentCaseManager();
        var ids = forms.Select(candidate => candidate.Id).Where(id => id > 0).Distinct().ToList();
        await using var context = await contextFactory.CreateDbContextAsync();
        var owned = await (from stored in context.Forms
                           join person in context.People on stored.PersonId equals person.Id
                           where ids.Contains(stored.Id) &&
                                 person.UserId == actor.Id &&
                                 person.AgencyId == actor.AgencyId
                           select stored).ToListAsync();
        if (owned.Count != ids.Count)
            throw new UnauthorizedAccessException("One or more forms are outside the signed-in caseload.");
        if (await context.FormAttestations.AnyAsync(attestation => ids.Contains(attestation.FormId)))
        {
            throw new InvalidOperationException(
                "A form with attestation history cannot be deleted. Its compliance history is append-only.");
        }
        context.Forms.RemoveRange(owned);
        await context.SaveChangesAsync();
    }

    private User CurrentCaseManager()
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in case manager is required.");
        if (!actor.HasCaseManagerPermissions && !actor.HasSupervisorPermissions)
            throw new UnauthorizedAccessException("A case manager or supervisor account is required.");
        return actor;
    }

    private static async Task<Form> LoadOwnedFormAsync(SatiContext context, User actor, int formId)
    {
        var form = await context.Forms
            .Include(candidate => candidate.Person)
            .Include(candidate => candidate.Attestations)
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == formId &&
                candidate.Person.AgencyId == actor.AgencyId)
            ?? throw new UnauthorizedAccessException("That form is outside the signed-in agency.");
        if (!await LocalTenantAccess.CanAccessUserAsync(context, actor, form.Person.UserId))
            throw new UnauthorizedAccessException("That form is outside the signed-in caseload.");
        return form;
    }

    private static async Task<List<ArtifactFact>> LoadArtifactFactsAsync(
        SatiContext context,
        int personId,
        DateTime cycleStart,
        CancellationToken cancellationToken) =>
        await context.DocumentArtifacts.AsNoTracking()
            .Where(artifact => artifact.PersonId == personId &&
                artifact.CycleStart == cycleStart.Date &&
                artifact.SupersededByArtifactId == null)
            .Select(artifact => new ArtifactFact(
                artifact.Id,
                artifact.PersonId,
                artifact.Kind.ToString(),
                artifact.CycleStart,
                artifact.Origin == DocumentArtifactOrigin.Draft,
                artifact.Origin == DocumentArtifactOrigin.RecordedAsExternal))
            .ToListAsync(cancellationToken);

    private static async Task<List<FormFact>> LoadFormFactsAsync(SatiContext context, int personId) =>
        await context.Forms.AsNoTracking()
            .Where(form => form.PersonId == personId)
            .Select(form => new FormFact(
                form.Id, form.PersonId, form.Type.ToString(), form.DueDate, form.CompletedDate))
            .ToListAsync();

    private static int[] MatchingArtifactIds(string formType, IReadOnlyCollection<ArtifactFact> artifacts)
    {
        var entry = AnnualDocumentCatalog.ForFormType(formType);
        return entry is null
            ? []
            : artifacts.Where(artifact =>
                    artifact.Kind.Equals(entry.Kind.ToString(), StringComparison.OrdinalIgnoreCase) &&
                    !artifact.IsDraft)
                .Select(artifact => artifact.ArtifactId)
                .Distinct().Order().ToArray();
    }
}
