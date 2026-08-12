using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Reporting;

namespace Sati.Data;

public sealed class AdminService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService,
    PersonAuditPdfExporter pdfExporter) : IAdminService
{
    public async Task<AdminOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        await using var context = contextFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        var today = now.Date;
        var thirtyDaysAgo = now.AddDays(-30);
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var agencyName = await context.Agencies.AsNoTracking()
            .Where(agency => agency.Id == actor.AgencyId)
            .Select(agency => agency.Name)
            .SingleAsync(cancellationToken);
        var userCount = await context.Users.AsNoTracking()
            .CountAsync(user => user.AgencyId == actor.AgencyId, cancellationToken);
        var caseManagerCount = await context.Users.AsNoTracking()
            .CountAsync(user => user.AgencyId == actor.AgencyId && user.Role == UserRole.CaseManager, cancellationToken);
        var personCount = await context.People.AsNoTracking()
            .CountAsync(person => person.AgencyId == actor.AgencyId &&
                context.Users.Any(user => user.Id == person.UserId && user.AgencyId == actor.AgencyId),
                cancellationToken);
        var notesThisMonth = await context.Notes.AsNoTracking()
            .CountAsync(note => note.EventDate >= monthStart &&
                context.People.Any(person => person.Id == note.PersonId && person.AgencyId == actor.AgencyId &&
                    context.Users.Any(user => user.Id == person.UserId && user.AgencyId == actor.AgencyId)),
                cancellationToken);
        var activeUsers = await context.AuditEvents.AsNoTracking()
            .Where(auditEvent => auditEvent.AgencyId == actor.AgencyId &&
                auditEvent.OccurredAtUtc >= thirtyDaysAgo)
            .Select(auditEvent => auditEvent.ActorUserId)
            .Distinct()
            .CountAsync(cancellationToken);
        var successfulSignIns = await context.AuditEvents.AsNoTracking()
            .CountAsync(auditEvent => auditEvent.AgencyId == actor.AgencyId &&
                auditEvent.OccurredAtUtc >= thirtyDaysAgo &&
                auditEvent.Action == LocalAuditActions.AuthenticationSucceeded,
                cancellationToken);
        var personChanges = await context.PersonVersions.AsNoTracking()
            .CountAsync(version => version.AgencyId == actor.AgencyId &&
                version.ChangedAtUtc >= thirtyDaysAgo && version.ChangeKind != "TrackingBaseline",
                cancellationToken);
        var auditEventsToday = await context.AuditEvents.AsNoTracking()
            .CountAsync(auditEvent => auditEvent.AgencyId == actor.AgencyId &&
                auditEvent.OccurredAtUtc >= today,
                cancellationToken);
        var lastActivity = await context.AuditEvents.AsNoTracking()
            .Where(auditEvent => auditEvent.AgencyId == actor.AgencyId)
            .MaxAsync(auditEvent => (DateTime?)auditEvent.OccurredAtUtc, cancellationToken);

        return new AdminOverviewDto(
            actor.AgencyId, agencyName, userCount, caseManagerCount, personCount,
            notesThisMonth, activeUsers, successfulSignIns, personChanges,
            auditEventsToday, lastActivity);
    }

    public async Task<List<AdminPersonListItemDto>> GetPeopleAsync(
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        await using var context = contextFactory.CreateDbContext();
        return await (
            from person in context.People.AsNoTracking()
            join user in context.Users.AsNoTracking() on person.UserId equals user.Id
            where person.AgencyId == actor.AgencyId && user.AgencyId == actor.AgencyId
            orderby person.LastName, person.FirstName, person.Id
            select new AdminPersonListItemDto(
                person.Id,
                ((person.LastName ?? string.Empty) + ", " + (person.FirstName ?? string.Empty)).Trim(' ', ','),
                person.Revision,
                user.Id,
                user.DisplayName))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AdminActivityDto>> GetActivityAsync(
        int days = 30,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        if (days is < 1 or > 366 || take is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(days), "Use 1-366 days and request 1-500 rows.");

        await using var context = contextFactory.CreateDbContext();
        var start = DateTime.UtcNow.AddDays(-days);
        return await (
            from auditEvent in context.AuditEvents.AsNoTracking()
            join user in context.Users.AsNoTracking() on auditEvent.ActorUserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            where auditEvent.AgencyId == actor.AgencyId && auditEvent.OccurredAtUtc >= start
            orderby auditEvent.OccurredAtUtc descending, auditEvent.Id descending
            select new AdminActivityDto(
                auditEvent.Id,
                auditEvent.ActorUserId,
                user == null ? $"User {auditEvent.ActorUserId}" : user.DisplayName,
                auditEvent.Action,
                auditEvent.ResourceType,
                auditEvent.ResourceId,
                auditEvent.OccurredAtUtc,
                auditEvent.CorrelationId))
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PersonVersionDto>> GetPersonHistoryAsync(
        int personId,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        await using var context = contextFactory.CreateDbContext();
        var person = await LoadPersonAsync(context, actor, personId, cancellationToken)
            ?? throw new InvalidOperationException("This Person was not found in your agency.");
        await PersonLifecycleLedger.EnsureBaselineAsync(context, person, cancellationToken);
        LocalAuditTrail.Record(
            context, actor, LocalAuditActions.PersonHistoryViewed, "Person", personId);
        await context.SaveChangesAsync(cancellationToken);
        return await LoadHistoryAsync(context, actor, personId, cancellationToken);
    }

    public async Task<byte[]> ExportPersonHistoryPdfAsync(
        int personId,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        await using var context = contextFactory.CreateDbContext();
        var person = await LoadPersonAsync(context, actor, personId, cancellationToken)
            ?? throw new InvalidOperationException("This Person was not found in your agency.");
        await PersonLifecycleLedger.EnsureBaselineAsync(context, person, cancellationToken);
        LocalAuditTrail.Record(
            context, actor, LocalAuditActions.PersonHistoryPdfGenerated, "Person", personId);
        await context.SaveChangesAsync(cancellationToken);
        var history = await LoadHistoryAsync(context, actor, personId, cancellationToken);
        var agency = await context.Agencies.AsNoTracking().SingleAsync(
            candidate => candidate.Id == actor.AgencyId,
            cancellationToken);
        return pdfExporter.Generate(person, history, agency, actor, DateTime.UtcNow);
    }

    private static Task<Person?> LoadPersonAsync(
        SatiContext context,
        User actor,
        int personId,
        CancellationToken cancellationToken) =>
        context.People.SingleOrDefaultAsync(person =>
            person.Id == personId && person.AgencyId == actor.AgencyId &&
            context.Users.Any(user => user.Id == person.UserId && user.AgencyId == actor.AgencyId),
            cancellationToken);

    private static async Task<List<PersonVersionDto>> LoadHistoryAsync(
        SatiContext context,
        User actor,
        int personId,
        CancellationToken cancellationToken) =>
        (await context.PersonVersions.AsNoTracking()
            .Where(version => version.PersonId == personId && version.AgencyId == actor.AgencyId)
            .OrderBy(version => version.Version)
            .ToListAsync(cancellationToken))
        .Select(PersonLifecycleLedger.ToDto)
        .ToList();

    private User CurrentAdmin()
    {
        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("A signed-in user is required.");
        if (actor.Role != UserRole.Admin)
            throw new UnauthorizedAccessException("Only an Admin can open this dashboard.");
        return actor;
    }
}
