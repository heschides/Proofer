using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Forms;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapAnnualPackets(RouteGroupBuilder api)
    {
        api.MapGet("/people/{personId:int}/annual-documents", async Task<IResult> (
            int personId, DateTime cycleStart, ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            var person = await AccessibleSafetyPerson(db, actor, personId, ct);
            if (person is null) return Results.NotFound();
            try { return Results.Ok(await AnnualStatus(db, actor, person, cycleStart, ct)); }
            catch (ArgumentException) { return InvalidSafetyCycle(); }
        });
        api.MapPost("/people/{personId:int}/documents/privacy-practices/acknowledgment", async Task<IResult> (
            int personId, AcknowledgeDocumentRequest request, ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            if (await AccessibleSafetyPerson(db, actor, personId, ct) is null) return Results.NotFound();
            var artifact = await db.DocumentArtifacts.SingleOrDefaultAsync(x => x.Id == request.DocumentArtifactId &&
                x.PersonId == personId && x.AgencyId == actor.AgencyId && x.Kind == "PrivacyPractices" && x.Origin == "GeneratedInSati" &&
                x.SupersededByArtifactId == null, ct);
            if (artifact is null) return Results.NotFound();
            var error = DocumentAcknowledgmentRules.Validate(request, artifact.GeneratedAtUtc.ToLocalTime(), DateTime.Today);
            if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["receipt"] = [error] });
            var receipt = new ServerDocumentAcknowledgment { DocumentArtifactId = artifact.Id, ReceivedOn = request.ReceivedOn?.Date,
                GoodFaithEffortReason = request.GoodFaithEffortReason?.Trim(), RecordedAtUtc = DateTime.UtcNow, RecordedByUserId = actor.UserId };
            db.DocumentAcknowledgments.Add(receipt);
            audit.Record(actor, "document.acknowledged", "DocumentArtifact", artifact.Id);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new DocumentAcknowledgmentDto(receipt.Id, receipt.DocumentArtifactId, receipt.ReceivedOn,
                receipt.GoodFaithEffortReason, receipt.RecordedByUserId, receipt.RecordedAtUtc));
        });
        api.MapPost("/people/{personId:int}/documents/verify", async Task<IResult> (
            int personId, VerifyDocumentRequest request, ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            if (await AccessibleSafetyPerson(db, actor, personId, ct) is null) return Results.NotFound();
            var artifact = await db.DocumentArtifacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.DocumentArtifactId &&
                x.PersonId == personId && x.AgencyId == actor.AgencyId, ct);
            if (artifact is null) return Results.NotFound();
            var matches = DocumentVerification.Matches(artifact.ContentSha256, artifact.ByteCount, request);
            audit.Record(actor, "document.verified", "DocumentArtifact", artifact.Id, JsonSerializer.Serialize(new { matches }));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new VerifyDocumentResult(matches, matches ? "This file matches the recorded original." : "This file does not match a recorded generated original."));
        });
        api.MapPost("/people/{personId:int}/annual-packet", async Task<IResult> (
            int personId, SaveAnnualPacketRequest request, ClaimsPrincipal principal, HttpContext http,
            ApiDbContext db, AuditTrail audit, AnnualPacketComposer composer, CancellationToken ct) =>
        {
            // Keep authorization, medical-release attestation and artifact replacement in one snapshot.
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, ct)) return Results.NotFound();
            var person = await db.People.AsNoTracking().SingleAsync(x => x.Id == personId, ct);
            AnnualDocumentsStatusDto status;
            try { status = await AnnualStatus(db, actor, person, request.CycleStart, ct); }
            catch (ArgumentException) { return InvalidSafetyCycle(); }
            if (!status.Window.IsOpen) return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["packet"] = [$"The packet opens on {status.Window.OpensOn:yyyy-MM-dd}."] });
            var agency = await db.Agencies.AsNoTracking().SingleAsync(x => x.Id == actor.AgencyId, ct);
            var candidates = await db.DocumentTemplates.AsNoTracking().Where(x => x.AgencyId == actor.AgencyId || x.AgencyId == null).ToListAsync(ct);
            var selected = new List<DocumentTemplateDto>();
            foreach (var kind in new[] { AnnualDocumentKind.PrivacyPractices, AnnualDocumentKind.MedicalRecordsRequest })
            {
                var fact = DocumentTemplateResolution.Resolve(actor.AgencyId, kind,
                    candidates.Select(x => new DocumentTemplateFact(x.Id, x.AgencyId, x.Kind, x.Version, x.PublishedAtUtc, x.RetiredAtUtc)));
                if (fact is not null) selected.Add(ToDocumentTemplateDto(candidates.Single(x => x.Id == fact.Id)));
            }
            var linked = await db.PersonProviders.Where(x => x.PersonId == personId && x.IsPrimaryCare && x.EndDate == null)
                .Select(x => (int?)x.ProviderId).SingleOrDefaultAsync(ct);
            var directory = await db.Providers.AsNoTracking().Where(x => x.AgencyId == actor.AgencyId).ToListAsync(ct);
            var recipient = RecordsRecipient.Resolve(linked, directory.Select(x => new RecordsProviderFact(x.Id, x.ParentProviderId, x.Name,
                ComposeAddress(x.Street, x.City, x.State, x.Zip), x.Phone)).ToList());
            var cycle = request.CycleStart.Date;
            var plan = await db.SafetyPlans.AsNoTracking().Where(x => x.PersonId == personId && x.CycleStart == cycle)
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
            var medical = await db.Forms.AnyAsync(x => x.PersonId == personId && x.Type == "Release_Medical" && x.CompletedDate != null &&
                x.DueDate > cycle && x.DueDate <= status.Window.EndsOn.AddDays(1), ct);
            var input = new PacketRenderInput(new(personId, $"{person.FirstName} {person.LastName}".Trim(), person.BirthDate,
                person.GuardianName, agency.Name, ComposeAddress(agency.Street, agency.City, agency.State, agency.Zip),
                agency.EdiContactPhone, actor.DisplayName, actor.Role), cycle, status.Window.EndsOn, DateTime.UtcNow, actor.UserId,
                status.Artifacts, plan is null ? null : ToSafetyPlan(plan), selected, medical, recipient?.Name, recipient?.Address, recipient?.Phone);
            var rendered = composer.Render(input);
            var recorded = new List<DocumentArtifactDto>();
            foreach (var file in rendered.Documents)
                recorded.Add(DocumentArtifactPersistence.ToDto(await DocumentArtifactPersistence.StageGeneratedAsync(db, personId, actor.AgencyId,
                    file.Kind, cycle, file.Origin, input.GeneratedAtUtc, actor.UserId, file.Pdf, file.FileName, file.BlankFields,
                    ct, file.TemplateOwner, file.TemplateKey, file.TemplateVersion, file.SourceContentId, file.SourceContentVersion)));
            var zip = AnnualPacketComposer.Zip(input, rendered, recorded);
            audit.Record(actor, "annual-packet.saved", "Person", personId);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            PreventSensitiveResponseCaching(http);
            return Results.File(zip, "application/zip", $"Annual-Documents-{personId}-{cycle:yyyy-MM-dd}.zip");
        });
    }
    private static async Task<AnnualDocumentsStatusDto> AnnualStatus(ApiDbContext db, Actor actor,
        ServerPerson person, DateTime cycle, CancellationToken ct)
    {
        var days = await db.Settings.Where(x => x.AgencyId == actor.AgencyId).Select(x => (int?)x.AnnualPacketOpenDaysBefore).FirstOrDefaultAsync(ct) ?? 30;
        var window = AnnualPacketWindow.ForCycle(person.EffectiveDate ?? throw new ArgumentException("Set an effective date first."), cycle.Date, DateTime.Today, days);
        var artifacts = (await db.DocumentArtifacts.AsNoTracking().Where(x => x.PersonId == person.Id && x.CycleStart == cycle.Date &&
            x.SupersededByArtifactId == null).ToListAsync(ct)).Select(DocumentArtifactPersistence.ToDto).ToList();
        var ids = artifacts.Select(x => x.Id).ToArray();
        var acknowledged = await db.DocumentAcknowledgments.Where(x => ids.Contains(x.DocumentArtifactId)).Select(x => x.DocumentArtifactId).Distinct().ToListAsync(ct);
        var pcp = await db.Forms.AnyAsync(x => x.PersonId == person.Id && x.Type == "PCP" && x.CompletedDate != null &&
            x.DueDate > cycle && x.DueDate <= window.EndsOn.AddDays(1), ct);
        return new(window, artifacts, acknowledged, AnnualDocumentReminder.Describe(window.IsOpen, pcp, artifacts));
    }
}
