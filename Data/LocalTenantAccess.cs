using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// Desktop-local mirror of <c>Sati.Api.Security.TenantAccess</c>. The transitional
/// local services must not assume the API is their only caller, so a caller-supplied
/// user or client id is re-scoped here with the same rules the server applies:
/// yourself always, your assigned case managers if you supervise, anyone in your
/// agency if you are a director or an administrator.
/// </summary>
/// <remarks>
/// The two implementations query different entity types against different contexts,
/// so they cannot share one method today. They must stay in step; a change to either
/// belongs in both.
/// </remarks>
internal static class LocalTenantAccess
{
    public static bool IsReviewer(UserPermissions permissions) =>
        UserPermissionRules.HasSupervisorPermissions(permissions);

    public static async Task<bool> CanAccessUserAsync(SatiContext context, User actor, int targetUserId)
    {
        if (targetUserId == actor.Id)
            return actor.HasCaseManagerPermissions;
        if (!IsReviewer(actor.Permissions))
            return false;

        var canReviewAgency = actor.HasAdminPermissions;
        return await context.Users.AsNoTracking().AnyAsync(user =>
            user.Id == targetUserId &&
            user.AgencyId == actor.AgencyId &&
            (user.Permissions & UserPermissions.CaseManagement) != 0 &&
            (canReviewAgency || user.SupervisorId == actor.Id));
    }

    public static async Task<bool> CanAccessPersonAsync(SatiContext context, User actor, int personId)
    {
        var ownerId = await context.People.AsNoTracking()
            .Where(person => person.Id == personId && person.AgencyId == actor.AgencyId)
            .Select(person => (int?)person.UserId)
            .SingleOrDefaultAsync();
        return ownerId is int owner && await CanAccessUserAsync(context, actor, owner);
    }
}
