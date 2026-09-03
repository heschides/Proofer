using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;
using Sati.Models;
using System.Text.Json;

namespace Sati.Data;

public sealed class DocumentTemplateService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService session,
    DocumentTemplatePdfComposer composer) : IDocumentTemplateService
{
    public async Task<IReadOnlyList<DocumentTemplateDto>> GetVersionsAsync(AnnualDocumentKind kind)
    {
        var actor = RequireAdmin();
        await using var db = await contextFactory.CreateDbContextAsync();
        var templates = await db.DocumentTemplates.AsNoTracking()
            .Where(template => template.Kind == kind &&
                (template.AgencyId == actor.AgencyId || template.AgencyId == null))
            .OrderByDescending(template => template.Version).ToListAsync();
        return templates.Select(ToDto).ToList();
    }

    public async Task<DocumentTemplateDto> PublishAsync(AnnualDocumentKind kind, string body)
    {
        var actor = RequireAdmin();
        await using var db = await contextFactory.CreateDbContextAsync();
        var version = (await db.DocumentTemplates
            .Where(template => template.AgencyId == actor.AgencyId && template.Kind == kind)
            .MaxAsync(template => (int?)template.Version) ?? 0) + 1;
        var template = DocumentTemplate.Publish(actor.AgencyId, kind, version, body, DateTime.UtcNow, actor.Id);
        db.DocumentTemplates.Add(template);
        LocalAuditTrail.Record(db, actor, LocalAuditActions.DocumentTemplatePublished, "Agency", actor.AgencyId,
            JsonSerializer.Serialize(new { kind = kind.ToString(), version }));
        await db.SaveChangesAsync();
        return ToDto(template);
    }

    public async Task<AgencyReleaseResult> GeneratePrivacyPracticesAsync(int personId, DateTime? cycleStart = null)
    {
        var actor = session.CurrentUser ?? throw new InvalidOperationException("Sign in before generating a document.");
        await using var db = await contextFactory.CreateDbContextAsync();
        if (!await LocalTenantAccess.CanAccessPersonAsync(db, actor, personId))
            throw new InvalidOperationException("The consumer is not in your accessible caseload.");
        var person = await db.People.AsNoTracking().SingleAsync(candidate => candidate.Id == personId);
        var agency = await db.Agencies.AsNoTracking().SingleAsync(candidate => candidate.Id == actor.AgencyId);
        var effective = person.EffectiveDate ?? throw new InvalidOperationException("The consumer has no effective date.");
        var start = cycleStart?.Date ?? AnnualDocumentCycle.CurrentStart(effective, DateTime.Today);
        if (AnnualDocumentCycle.CurrentStart(effective, start) != start)
            throw new ArgumentException("The document cycle must begin on the consumer's effective-date anniversary.", nameof(cycleStart));
        var kind = AnnualDocumentKind.PrivacyPractices;
        var templates = await db.DocumentTemplates.AsNoTracking()
            .Where(template => template.Kind == kind &&
                (template.AgencyId == actor.AgencyId || template.AgencyId == null)).ToListAsync();
        var selected = DocumentTemplateResolution.Resolve(actor.AgencyId, kind,
            templates.Select(template => new DocumentTemplateFact(
                template.Id, template.AgencyId, template.Kind.ToString(), template.Version,
                template.PublishedAtUtc, template.RetiredAtUtc)))
            ?? throw new InvalidOperationException("No published privacy-practices template is available.");
        var template = templates.Single(candidate => candidate.Id == selected.Id);
        var now = DateTime.UtcNow;
        var rendered = composer.Generate(kind, template.Body, new DocumentTemplateRenderContext(
            agency.Name, AgencyReleaseService.ComposeAddress(agency.Street, agency.City, agency.State, agency.Zip),
            agency.EdiContactPhone, person.FullName, person.BirthDate, start, AnnualDocumentCycle.EndInclusive(effective, start),
            actor.DisplayName, actor.Role.ToString()), now);
        var fileName = $"Privacy-Practices-{personId}.pdf";
        var owner = DocumentTemplateRules.OwnerName(template.AgencyId);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DocumentArtifactStore.StageGeneratedAsync(db, personId, actor.AgencyId, kind, start,
            DocumentArtifactOrigin.GeneratedInSati, now, actor.Id, rendered.Pdf, fileName,
            rendered.BlankFields, default, owner, kind.ToString(), template.Version);
        LocalAuditTrail.Record(db, actor, LocalAuditActions.DocumentGenerated, "Person", personId,
            JsonSerializer.Serialize(new
            {
                kind = kind.ToString(), cycleStart = start.ToString("yyyy-MM-dd"),
                origin = DocumentArtifactOrigin.GeneratedInSati.ToString(),
                templateOwner = owner, templateKey = kind.ToString(), templateVersion = template.Version
            }));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new AgencyReleaseResult(rendered.Pdf, fileName);
    }

    private User RequireAdmin()
    {
        var actor = session.CurrentUser ?? throw new InvalidOperationException("Sign in before managing templates.");
        if (!actor.HasAdminPermissions)
            throw new UnauthorizedAccessException("Administration permission is required to manage templates.");
        return actor;
    }

    private static DocumentTemplateDto ToDto(DocumentTemplate template) => new(
        template.Id, template.AgencyId, template.Kind.ToString(), template.Version, template.Body,
        template.PublishedAtUtc, template.PublishedByUserId, template.RetiredAtUtc,
        DocumentTemplateRules.OwnerName(template.AgencyId));
}
