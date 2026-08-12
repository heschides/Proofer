using System.Security.Claims;

namespace Sati.Api.Security;

internal readonly record struct Actor(int UserId, int AgencyId, string Role, string DisplayName)
{
    public static Actor From(ClaimsPrincipal principal)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !int.TryParse(principal.FindFirstValue("agency_id"), out var agencyId))
            throw new UnauthorizedAccessException("The authenticated session has no valid Sati identity.");

        return new Actor(
            userId,
            agencyId,
            principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty);
    }
}
