using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;

namespace Sati.Data;

public sealed class LocalPlatformHealthService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IPlatformHealthService
{
    public async Task<PlatformIncidentDashboardDto> GetDashboardAsync(
        int days = 30,
        int take = 500,
        CancellationToken cancellationToken = default)
    {
        if (sessionService.CurrentUser?.Role != UserRole.PlatformOperator)
            throw new UnauthorizedAccessException("Only the platform operator can open cross-agency health.");
        if (days is < 1 or > 90 || take is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(days));

        await using var context = contextFactory.CreateDbContext();
        var observedAt = DateTime.UtcNow;
        var start = observedAt.AddDays(-days);
        var incidents = await context.IncidentGroups.AsNoTracking()
            .Where(candidate => candidate.LastSeenUtc >= start)
            .OrderByDescending(candidate => candidate.LastSeenUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Take(take)
            .Select(candidate => new IncidentGroupDto(
                candidate.Id, candidate.AgencyId, candidate.Scope, candidate.Source, candidate.Severity,
                candidate.Operation, candidate.FirstRelease, candidate.LastRelease,
                candidate.ExceptionFingerprint, candidate.Status, candidate.OccurrenceCount,
                candidate.FirstSeenUtc, candidate.LastSeenUtc, candidate.LastReference,
                candidate.LastActorRole))
            .ToListAsync(cancellationToken);
        var agencies = await context.Agencies.AsNoTracking()
            .OrderBy(candidate => candidate.Name)
            .Select(candidate => new { candidate.Id, candidate.Name })
            .ToListAsync(cancellationToken);
        return new PlatformIncidentDashboardDto(
            observedAt,
            IncidentHealthScoring.Calculate(incidents, observedAt, days),
            agencies.Select(agency => new PlatformAgencyHealthDto(
                agency.Id,
                agency.Name,
                IncidentHealthScoring.Calculate(
                    incidents.Where(item => item.AgencyId == agency.Id &&
                        item.Scope == IncidentScopes.Agency), observedAt, days))).ToList(),
            incidents);
    }
}
