using System.Text;
using System.Text.Json;
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
            .CountAsync(user => user.AgencyId == actor.AgencyId && user.Role != UserRole.PlatformOperator, cancellationToken);
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

    public async Task<AdminOperationsDto> GetOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        await using var context = contextFactory.CreateDbContext();
        var auditCount = await context.AuditEvents.AsNoTracking()
            .LongCountAsync(candidate => candidate.AgencyId == actor.AgencyId, cancellationToken);
        var ediCount = await context.EdiGenerations.AsNoTracking()
            .LongCountAsync(candidate => candidate.AgencyId == actor.AgencyId, cancellationToken);
        var ediCharacters = await context.EdiGenerations.AsNoTracking()
            .Where(candidate => candidate.AgencyId == actor.AgencyId)
            .SumAsync(candidate => (long?)candidate.Content.Length, cancellationToken) ?? 0;
        var oldestAudit = await context.AuditEvents.AsNoTracking()
            .Where(candidate => candidate.AgencyId == actor.AgencyId)
            .MinAsync(candidate => (DateTime?)candidate.OccurredAtUtc, cancellationToken);
        var oldestEdi = await context.EdiGenerations.AsNoTracking()
            .Where(candidate => candidate.AgencyId == actor.AgencyId)
            .MinAsync(candidate => (DateTime?)candidate.CreatedAtUtc, cancellationToken);

        return new AdminOperationsDto(
            DateTime.UtcNow,
            "Healthy",
            "PolicyOnly",
            OperationalPolicyDefaults.AuditRetentionDays,
            OperationalPolicyDefaults.EdiReplayRetentionDays,
            auditCount,
            ediCount,
            ediCharacters,
            oldestAudit,
            oldestEdi);
    }

    public async Task<AdminIncidentDashboardDto> GetIncidentsAsync(
        int days = 30,
        int take = 250,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        if (days is < 1 or > 90 || take is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(days));
        await using var context = contextFactory.CreateDbContext();
        var observedAt = DateTime.UtcNow;
        var start = observedAt.AddDays(-days);
        var incidents = await context.IncidentGroups.AsNoTracking()
            .Where(candidate => candidate.AgencyId == actor.AgencyId && candidate.LastSeenUtc >= start)
            .OrderByDescending(candidate => candidate.LastSeenUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Take(take)
            .Select(candidate => new IncidentGroupDto(
                candidate.Id,
                candidate.AgencyId,
                candidate.Source,
                candidate.Severity,
                candidate.Operation,
                candidate.FirstRelease,
                candidate.LastRelease,
                candidate.ExceptionFingerprint,
                candidate.Status,
                candidate.OccurrenceCount,
                candidate.FirstSeenUtc,
                candidate.LastSeenUtc,
                candidate.LastReference,
                candidate.LastActorRole))
            .ToListAsync(cancellationToken);
        return new AdminIncidentDashboardDto(
            observedAt,
            IncidentHealthScoring.Calculate(incidents, observedAt, days),
            incidents);
    }

    public async Task<IncidentGroupDto> UpdateIncidentStatusAsync(
        long incidentId,
        string status,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        if (status is not ("Open" or "Investigating" or "Resolved"))
            throw new ArgumentException("Status must be Open, Investigating, or Resolved.", nameof(status));
        await using var context = contextFactory.CreateDbContext();
        var incident = await context.IncidentGroups.SingleOrDefaultAsync(candidate =>
            candidate.Id == incidentId && candidate.AgencyId == actor.AgencyId,
            cancellationToken) ?? throw new InvalidOperationException("This incident was not found in your agency.");
        incident.Status = status;
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.IncidentStatusUpdated,
            "IncidentGroup",
            metadataJson: JsonSerializer.Serialize(new { incidentId, status }));
        await context.SaveChangesAsync(cancellationToken);
        return new IncidentGroupDto(
            incident.Id, incident.AgencyId, incident.Source, incident.Severity,
            incident.Operation, incident.FirstRelease, incident.LastRelease,
            incident.ExceptionFingerprint, incident.Status, incident.OccurrenceCount,
            incident.FirstSeenUtc, incident.LastSeenUtc, incident.LastReference,
            incident.LastActorRole);
    }

    public async Task<byte[]> ExportAuditCsvAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        var start = fromUtc.ToUniversalTime();
        var end = toUtc.ToUniversalTime();
        reason = reason?.Trim() ?? string.Empty;
        if (end < start || (end - start).TotalDays > 366 || end > DateTime.UtcNow.AddMinutes(5) ||
            reason.Length is < 10 or > 250)
        {
            throw new ArgumentException(
                "Use a window no longer than one year ending no later than now and provide a 10-250 character reason.",
                nameof(reason));
        }

        await using var context = contextFactory.CreateDbContext();
        var rows = await (
            from auditEvent in context.AuditEvents.AsNoTracking()
            join user in context.Users.AsNoTracking() on auditEvent.ActorUserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            where auditEvent.AgencyId == actor.AgencyId &&
                  auditEvent.OccurredAtUtc >= start && auditEvent.OccurredAtUtc <= end
            orderby auditEvent.OccurredAtUtc, auditEvent.Id
            select new LocalAuditExportRow(
                auditEvent.EventId,
                auditEvent.OccurredAtUtc,
                auditEvent.ActorUserId,
                user == null ? $"User {auditEvent.ActorUserId}" : user.DisplayName,
                auditEvent.Action,
                auditEvent.ResourceType,
                auditEvent.ResourceId,
                auditEvent.CorrelationId))
            .Take(10_000)
            .ToListAsync(cancellationToken);

        var exportedAt = DateTime.UtcNow;
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.AuditExported,
            "AuditEvent",
            metadataJson: JsonSerializer.Serialize(new
            {
                fromUtc = start,
                toUtc = end,
                rowCount = rows.Count
            }));
        await context.SaveChangesAsync(cancellationToken);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetBytes(BuildAuditCsv(rows, reason, exportedAt));
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

    private static string BuildAuditCsv(
        IReadOnlyList<LocalAuditExportRow> rows,
        string reason,
        DateTime exportedAtUtc)
    {
        var csv = new StringBuilder();
        csv.AppendLine("ExportReason,ExportedAtUtc,EventId,OccurredAtUtc,ActorUserId,ActorDisplayName,Action,ResourceType,ResourceId,CorrelationId");
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                Csv(reason),
                Csv(exportedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                Csv(row.EventId.ToString()),
                Csv(row.OccurredAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                Csv(row.ActorUserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Csv(row.ActorDisplayName),
                Csv(row.Action),
                Csv(row.ResourceType),
                Csv(row.ResourceId),
                Csv(row.CorrelationId)
            }));
        }
        return csv.ToString();
    }

    private static string Csv(string? value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private sealed record LocalAuditExportRow(
        Guid EventId,
        DateTime OccurredAtUtc,
        int ActorUserId,
        string ActorDisplayName,
        string Action,
        string ResourceType,
        string? ResourceId,
        string CorrelationId);
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
