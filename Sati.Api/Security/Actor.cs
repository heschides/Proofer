using System.Security.Claims;
using Sati.Contracts.V1;

namespace Sati.Api.Security;

internal readonly record struct Actor(
    int UserId,
    int AgencyId,
    string Role,
    string DisplayName,
    UserPermissions Permissions)
{
    internal const string ValidatedPermissionsClaim = "sati_validated_permissions";

    public static Actor From(ClaimsPrincipal principal)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !int.TryParse(principal.FindFirstValue("agency_id"), out var agencyId) ||
            !int.TryParse(principal.FindFirstValue(ValidatedPermissionsClaim), out var permissionsValue))
            throw new UnauthorizedAccessException("The authenticated session has no valid Sati identity.");

        return new Actor(
            userId,
            agencyId,
            principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            (UserPermissions)permissionsValue);
    }

    public static Actor FromUnvalidatedClaims(ClaimsPrincipal principal)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !int.TryParse(principal.FindFirstValue("agency_id"), out var agencyId))
            throw new UnauthorizedAccessException("The authenticated session has no valid Sati identity.");

        return new Actor(
            userId,
            agencyId,
            principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            UserPermissions.None);
    }

    public bool HasCaseManagerPermissions =>
        UserPermissionRules.HasCaseManagerPermissions(Permissions);
    public bool HasSupervisorPermissions =>
        UserPermissionRules.HasSupervisorPermissions(Permissions);
    public bool HasAdminPermissions =>
        UserPermissionRules.HasAdminPermissions(Permissions);
    public bool HasBillingPermissions =>
        UserPermissionRules.HasBillingPermissions(Permissions);
    public bool HasAgencyWideSupervisionPermissions =>
        UserPermissionRules.HasAgencyWideSupervisionPermissions(Permissions);

    public AgencyActor ToAgencyActor() => new(UserId, AgencyId, Permissions);
}
