using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;

namespace Sati.Data;

/// <summary>
/// Real, minimal legal-hold registry for local Production: an unreleased <c>LegalHold</c> row
/// for the person blocks rule-3 deletion. Any query failure is caught and reported as
/// <see cref="LegalHoldStatus.Unavailable"/> rather than allowed to propagate — the deletion gate
/// must fail closed, never treat "could not check" as "confirmed clear".
/// </summary>
public sealed class LocalLegalHoldRegistry(IDbContextFactory<SatiContext> contextFactory)
    : ILegalHoldRegistry
{
    public async Task<LegalHoldStatus> GetStatusAsync(
        int agencyId, int personId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = contextFactory.CreateDbContext();
            var hasActiveHold = await context.LegalHolds.AsNoTracking().AnyAsync(
                hold => hold.AgencyId == agencyId && hold.PersonId == personId && !hold.IsReleased,
                cancellationToken);
            return hasActiveHold ? LegalHoldStatus.Active : LegalHoldStatus.Clear;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return LegalHoldStatus.Unavailable;
        }
    }
}
