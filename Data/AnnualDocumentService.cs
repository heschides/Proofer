using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;
using Sati.Models;

namespace Sati.Data;

public sealed class AnnualDocumentService(IDbContextFactory<SatiContext> factory, ISessionService session,
    AnnualPacketComposer composer) : IAnnualDocumentService
{
    private User Actor => session.CurrentUser ?? throw new UnauthorizedAccessException();
    private static async Task<Person> RequirePerson(SatiContext db, User actor, int personId)
    {
        if (!await LocalTenantAccess.CanAccessPersonAsync(db, actor, personId)) throw new UnauthorizedAccessException();
        return await db.People.AsNoTracking().SingleAsync(x => x.Id == personId);
    }
    public async Task<AnnualDocumentsStatusDto> GetStatusAsync(int personId, DateTime cycleStart)
    {
        var actor = Actor; await using var db = await factory.CreateDbContextAsync();
        var person = await RequirePerson(db, actor, personId);
        return await GetStatusCoreAsync(db, actor, person, cycleStart);
    }
    private static async Task<AnnualDocumentsStatusDto> GetStatusCoreAsync(SatiContext db, User actor, Person person, DateTime cycleStart)
    {
        var personId = person.Id;
        var days = await db.Settings.Where(x => x.AgencyId == actor.AgencyId).Select(x => (int?)x.AnnualPacketOpenDaysBefore).FirstOrDefaultAsync() ?? 30;
        var window = AnnualPacketWindow.ForCycle(person.EffectiveDate ?? throw new InvalidOperationException("Set an effective date first."), cycleStart.Date, DateTime.Today, days);
        var artifacts = (await db.DocumentArtifacts.AsNoTracking().Where(x => x.PersonId == personId && x.CycleStart == cycleStart.Date && x.SupersededByArtifactId == null).ToListAsync())
            .Select(DocumentArtifactStore.ToDto).ToList();
        var ids = artifacts.Select(x => x.Id).ToArray();
        var acknowledged = await db.DocumentAcknowledgments.Where(x => ids.Contains(x.DocumentArtifactId)).Select(x => x.DocumentArtifactId).Distinct().ToListAsync();
        var pcp = await db.Forms.AnyAsync(x => x.PersonId == personId && x.Type == FormType.PCP && x.CompletedDate != null && x.DueDate > cycleStart && x.DueDate <= window.EndsOn.AddDays(1));
        return new(window, artifacts, acknowledged, AnnualDocumentReminder.Describe(window.IsOpen, pcp, artifacts));
    }
    public async Task<DocumentAcknowledgmentDto> AcknowledgeAsync(int personId, AcknowledgeDocumentRequest request)
    {
        var actor = Actor; await using var db = await factory.CreateDbContextAsync();
        await RequirePerson(db, actor, personId);
        var artifact = await db.DocumentArtifacts.SingleOrDefaultAsync(x => x.Id == request.DocumentArtifactId && x.PersonId == personId &&
            x.AgencyId == actor.AgencyId && x.Kind == AnnualDocumentKind.PrivacyPractices && x.Origin == DocumentArtifactOrigin.GeneratedInSati && x.SupersededByArtifactId == null)
            ?? throw new InvalidOperationException("Reload the current privacy notice first.");
        var error = DocumentAcknowledgmentRules.Validate(request, artifact.GeneratedAtUtc.ToLocalTime(), DateTime.Today);
        if (error is not null) throw new ArgumentException(error);
        var receipt = new DocumentAcknowledgment { DocumentArtifactId = artifact.Id, ReceivedOn = request.ReceivedOn?.Date,
            GoodFaithEffortReason = request.GoodFaithEffortReason?.Trim(), RecordedByUserId = actor.Id, RecordedAtUtc = DateTime.UtcNow };
        db.DocumentAcknowledgments.Add(receipt);
        LocalAuditTrail.Record(db, actor, "document.acknowledged", "DocumentArtifact", artifact.Id);
        await db.SaveChangesAsync();
        return new(receipt.Id, receipt.DocumentArtifactId, receipt.ReceivedOn, receipt.GoodFaithEffortReason, receipt.RecordedByUserId, receipt.RecordedAtUtc);
    }
    public async Task<VerifyDocumentResult> VerifyAsync(int personId, VerifyDocumentRequest request)
    {
        var actor = Actor; await using var db = await factory.CreateDbContextAsync();
        await RequirePerson(db, actor, personId);
        var artifact = await db.DocumentArtifacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.DocumentArtifactId &&
            x.PersonId == personId && x.AgencyId == actor.AgencyId) ?? throw new UnauthorizedAccessException();
        var matches = DocumentVerification.Matches(artifact.ContentSha256, artifact.ByteCount, request);
        LocalAuditTrail.Record(db, actor, "document.verified", "DocumentArtifact", artifact.Id, System.Text.Json.JsonSerializer.Serialize(new { matches }));
        await db.SaveChangesAsync();
        return new(matches, matches ? "This file matches the recorded original." : "This file does not match a recorded generated original.");
    }
    public async Task<AgencyReleaseResult> SavePacketAsync(int personId, DateTime cycleStart)
    {
        var actor = Actor; await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var person = await RequirePerson(db, actor, personId);
        if (!SafetyPlanRules.CanAuthor(actor.Id, actor.Permissions, person.UserId)) throw new UnauthorizedAccessException();
        var status = await GetStatusCoreAsync(db, actor, person, cycleStart);
        if (!status.Window.IsOpen) throw new InvalidOperationException($"The packet opens on {status.Window.OpensOn:d}.");
        var agency = await db.Agencies.AsNoTracking().SingleAsync(x => x.Id == actor.AgencyId);
        var candidates = await db.DocumentTemplates.AsNoTracking().Where(x => x.AgencyId == actor.AgencyId || x.AgencyId == null).ToListAsync();
        var selected = new List<DocumentTemplateDto>();
        foreach (var kind in new[] { AnnualDocumentKind.PrivacyPractices, AnnualDocumentKind.MedicalRecordsRequest })
        {
            var fact = DocumentTemplateResolution.Resolve(actor.AgencyId, kind, candidates.Select(x => new DocumentTemplateFact(x.Id, x.AgencyId, x.Kind.ToString(), x.Version, x.PublishedAtUtc, x.RetiredAtUtc)));
            if (fact is not null)
            {
                var item = candidates.Single(x => x.Id == fact.Id);
                selected.Add(new(item.Id, item.AgencyId, item.Kind.ToString(), item.Version, item.Body, item.PublishedAtUtc, item.PublishedByUserId, item.RetiredAtUtc, DocumentTemplateRules.OwnerName(item.AgencyId)));
            }
        }
        var linked = await db.PersonProviders.Where(x => x.PersonId == personId && x.IsPrimaryCare && x.EndDate == null).Select(x => (int?)x.ProviderId).SingleOrDefaultAsync();
        var directory = await db.Providers.AsNoTracking().Where(x => x.AgencyId == actor.AgencyId).ToListAsync();
        var recipient = RecordsRecipient.Resolve(linked, directory.Select(x => new RecordsProviderFact(x.Id, x.ParentProviderId, x.Name,
            AgencyReleaseService.ComposeAddress(x.Street, x.City, x.State, x.Zip), x.Phone)).ToList());
        var safety = await db.SafetyPlans.AsNoTracking().Where(x => x.PersonId == personId && x.CycleStart == cycleStart.Date).OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        SafetyPlanDto? plan = safety is null ? null : new(safety.Id, safety.PersonId, safety.AuthorUserId, safety.CycleStart, safety.Status, safety.Version, safety.Revision,
            safety.CreatedAtUtc, safety.UpdatedAtUtc, safety.SubmittedAtUtc, safety.ApprovedAtUtc, safety.ApprovedByUserId, safety.ReturnReason, safety.DocumentJson);
        var medical = await db.Forms.AnyAsync(x => x.PersonId == personId && x.Type == FormType.Release_Medical && x.CompletedDate != null &&
            x.DueDate > cycleStart && x.DueDate <= status.Window.EndsOn.AddDays(1));
        var input = new PacketRenderInput(new(personId, person.FullName, person.BirthDate, person.GuardianName, agency.Name,
            AgencyReleaseService.ComposeAddress(agency.Street, agency.City, agency.State, agency.Zip), agency.EdiContactPhone, actor.DisplayName, actor.Role.ToString()),
            cycleStart.Date, status.Window.EndsOn, DateTime.UtcNow, actor.Id, status.Artifacts, plan, selected, medical, recipient?.Name, recipient?.Address, recipient?.Phone);
        var rendered = composer.Render(input);
        var recorded = new List<DocumentArtifactDto>();
        foreach (var file in rendered.Documents)
            recorded.Add(DocumentArtifactStore.ToDto(await DocumentArtifactStore.StageGeneratedAsync(db, personId, actor.AgencyId, file.Kind, cycleStart,
                file.Origin, input.GeneratedAtUtc, actor.Id, file.Pdf, file.FileName, file.BlankFields, default, file.TemplateOwner, file.TemplateKey,
                file.TemplateVersion, file.SourceContentId, file.SourceContentVersion)));
        var zip = AnnualPacketComposer.Zip(input, rendered, recorded);
        LocalAuditTrail.Record(db, actor, "annual-packet.saved", "Person", personId);
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return new(zip, $"Annual-Documents-{personId}-{cycleStart:yyyy-MM-dd}.zip");
    }
}
