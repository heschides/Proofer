using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Contracts.V1;

namespace Sati.Api.Security;

/// <summary>
/// Real, minimal legal-hold registry: an unreleased <c>ServerLegalHold</c> row for the person
/// blocks rule-3 deletion. Any query failure is caught and reported as
/// <see cref="LegalHoldStatus.Unavailable"/> rather than allowed to propagate — the deletion gate
/// must fail closed, never treat "could not check" as "confirmed clear".
/// </summary>
internal sealed class ApiLegalHoldRegistry(ApiDbContext db) : ILegalHoldRegistry
{
    public async Task<LegalHoldStatus> GetStatusAsync(
        int agencyId, int personId, CancellationToken cancellationToken = default)
    {
        try
        {
            var hasActiveHold = await db.LegalHolds.AsNoTracking().AnyAsync(
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
