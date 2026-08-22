using Microsoft.EntityFrameworkCore;
using Sati.Data;

namespace Sati.Services.LocalAi;

/// <summary>
/// Confirms that the selected person belongs to the signed-in case manager and returns only the
/// minimum identity needed for a local draft. Historical notes, assessments, Bio, deadlines,
/// contact details, billing fields, and the rough narrative never cross this boundary.
/// </summary>
public sealed class ClientAiContextService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IClientAiContextService
{
    public async Task<ClientAiContext> BuildAsync(
        int personId,
        CancellationToken cancellationToken = default)
    {
        if (personId <= 0)
            throw new ArgumentOutOfRangeException(nameof(personId));

        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("A signed-in user is required to validate the selected client.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var selected = await db.People
            .AsNoTracking()
            .Where(person => person.Id == personId &&
                             person.UserId == actor.Id &&
                             person.AgencyId == actor.AgencyId)
            .Select(person => new { person.Id, person.FirstName })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException(
                "The selected client is not assigned to the signed-in user, so Sati did not provide client information to the AI assistant.");

        return new ClientAiContext(
            selected.Id,
            selected.FirstName,
            [new ClientAiContextSource("Scope", "Selected client identity only; no prior records")]);
    }
}
