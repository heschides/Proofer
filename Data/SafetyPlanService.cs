using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;
using Sati.Models;
using System.Text.Json;

namespace Sati.Data;

public sealed class SafetyPlanService(IDbContextFactory<SatiContext> factory, ISessionService session,
    SafetyPlanPdfGenerator renderer) : ISafetyPlanService
{
    private User Actor => session.CurrentUser ?? throw new UnauthorizedAccessException("Sign in first.");
    public async Task<SafetyPlanDto?> GetAsync(int personId, DateTime cycleStart)
    {
        var actor = Actor;
        await using var db = await factory.CreateDbContextAsync();
        await RequirePerson(db, actor, personId, cycleStart);
        var plan = await db.SafetyPlans.AsNoTracking().Where(x => x.PersonId == personId && x.CycleStart == cycleStart.Date)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        return plan is null ? null : ToDto(plan);
    }
    public async Task<SafetyPlanDto> StartAsync(int personId, DateTime cycleStart)
    {
        var actor = Actor;
        await using var db = await factory.CreateDbContextAsync();
        var person = await RequirePerson(db, actor, personId, cycleStart);
        if (!SafetyPlanRules.CanAuthor(actor.Id, actor.Permissions, person.UserId)) throw new UnauthorizedAccessException();
        var prior = await db.SafetyPlans.AsNoTracking().Where(x => x.PersonId == personId && x.CycleStart == cycleStart.Date)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        if (prior?.Status is "Draft" or "ReadyForReview") return ToDto(prior);
        if (!SafetyPlanRules.CanStartRevision(prior?.Status)) throw new InvalidOperationException("This version cannot be revised.");
        var now = DateTime.UtcNow;
        var plan = new SafetyPlan { PersonId = personId, AuthorUserId = actor.Id, CycleStart = cycleStart.Date,
            Version = (prior?.Version ?? 0) + 1, CreatedAtUtc = now, UpdatedAtUtc = now,
            DocumentJson = prior?.DocumentJson ?? SafetyPlanRules.EmptyDocumentJson() };
        db.SafetyPlans.Add(plan);
        LocalAuditTrail.Record(db, actor, "safety-plan.created", "Person", personId);
        await db.SaveChangesAsync();
        return ToDto(plan);
    }
    public async Task<SafetyPlanDto> ChangeAsync(SafetyPlanDto requested, string action, string? document = null, string? reason = null)
    {
        var actor = Actor;
        await using var db = await factory.CreateDbContextAsync();
        var plan = await db.SafetyPlans.SingleOrDefaultAsync(x => x.Id == requested.Id) ?? throw new UnauthorizedAccessException();
        var person = await RequirePerson(db, actor, plan.PersonId, plan.CycleStart);
        if (action is not ("approve" or "return") && !SafetyPlanRules.CanAuthor(actor.Id, actor.Permissions, person.UserId))
            throw new UnauthorizedAccessException();
        var next = SafetyPlanRules.Change(ToDto(plan), action, requested.Revision, actor.Id, actor.Permissions, DateTime.UtcNow, document, reason);
        plan.Status = next.Status; plan.Revision = next.Revision; plan.DocumentJson = next.DocumentJson;
        plan.UpdatedAtUtc = next.UpdatedAtUtc; plan.SubmittedAtUtc = next.SubmittedAtUtc;
        plan.ApprovedAtUtc = next.ApprovedAtUtc; plan.ApprovedByUserId = next.ApprovedByUserId; plan.ReturnReason = next.ReturnReason;
        var auditAction = action switch { "save" => "updated", "submit" => "submitted", "approve" => "approved", _ => "returned" };
        LocalAuditTrail.Record(db, actor, $"safety-plan.{auditAction}", "SafetyPlan", plan.Id);
        await db.SaveChangesAsync();
        return next;
    }
    public async Task<AgencyReleaseResult> GenerateAsync(int personId, DateTime cycleStart)
    {
        var actor = Actor;
        await using var db = await factory.CreateDbContextAsync();
        var person = await RequirePerson(db, actor, personId, cycleStart);
        var plan = await db.SafetyPlans.AsNoTracking().Where(x => x.PersonId == personId && x.CycleStart == cycleStart.Date)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync() ?? throw new InvalidOperationException("Start the safety plan first.");
        var errors = SafetyPlanRules.Validate(plan.DocumentJson, plan.Status == "Approved");
        if (errors.Count > 0) throw new InvalidOperationException("The saved plan content is invalid.");
        var content = JsonSerializer.Deserialize<SafetyPlanDocument>(plan.DocumentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var now = DateTime.UtcNow;
        var pdf = renderer.Generate(person.FullName, plan.CycleStart, content, plan.Status, now);
        var origin = plan.Status == "Approved" ? DocumentArtifactOrigin.GeneratedInSati : DocumentArtifactOrigin.Draft;
        var name = $"Safety-Plan-{(origin == DocumentArtifactOrigin.Draft ? "DRAFT-" : "")}{personId}.pdf";
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DocumentArtifactStore.StageGeneratedAsync(db, personId, actor.AgencyId, AnnualDocumentKind.SafetyPlan, plan.CycleStart,
            origin, now, actor.Id, pdf, name, content.Sections.Where(x => string.IsNullOrWhiteSpace(x.Text)).Select(x => x.Id).ToArray(),
            default, sourceContentId: plan.Id, sourceContentVersion: plan.Version);
        LocalAuditTrail.Record(db, actor, LocalAuditActions.DocumentGenerated, "SafetyPlan", plan.Id);
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return new AgencyReleaseResult(pdf, name);
    }
    private static async Task<Person> RequirePerson(SatiContext db, User actor, int id, DateTime cycle)
    {
        if (!await LocalTenantAccess.CanAccessPersonAsync(db, actor, id)) throw new UnauthorizedAccessException();
        var person = await db.People.AsNoTracking().SingleAsync(x => x.Id == id);
        if (person.EffectiveDate is not DateTime effective || cycle.Year is < 2 or > 9997 ||
            cycle.Date < effective.Date || AnnualDocumentCycle.CurrentStart(effective, cycle) != cycle.Date)
            throw new ArgumentException("Choose an effective-date anniversary on or after enrollment.");
        return person;
    }
    private static SafetyPlanDto ToDto(SafetyPlan plan) => new(plan.Id, plan.PersonId, plan.AuthorUserId, plan.CycleStart,
        plan.Status, plan.Version, plan.Revision, plan.CreatedAtUtc, plan.UpdatedAtUtc, plan.SubmittedAtUtc,
        plan.ApprovedAtUtc, plan.ApprovedByUserId, plan.ReturnReason, plan.DocumentJson);
}
