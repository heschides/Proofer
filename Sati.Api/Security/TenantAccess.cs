using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Contracts.V1;
using System.Security.Claims;

namespace Sati.Api.Security;

internal static class TenantAccess
{
    public static Task<bool> IsCurrentActorAsync(
        ApiDbContext db,
        Actor actor,
        CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(
            user => user.Id == actor.UserId &&
                    user.AgencyId == actor.AgencyId &&
                    user.Role == actor.Role &&
                    user.Permissions == actor.Permissions,
            cancellationToken);

    public static async Task<bool> CanAccessUserAsync(
        ApiDbContext db,
        Actor actor,
        int targetUserId,
        CancellationToken cancellationToken)
    {
        if (!await IsCurrentActorAsync(db, actor, cancellationToken))
            return false;

        if (targetUserId == actor.UserId)
            return actor.HasCaseManagerPermissions;
        if (!actor.HasSupervisorPermissions)
            return false;

        return await db.Users.AsNoTracking().AnyAsync(
            user => user.Id == targetUserId &&
                    user.AgencyId == actor.AgencyId &&
                    (user.Permissions & Sati.Contracts.V1.UserPermissions.CaseManagement) != 0 &&
                    (actor.HasAdminPermissions ||
                     user.SupervisorId == actor.UserId),
            cancellationToken);
    }

    public static Task<bool> OwnsPersonAsync(
        ApiDbContext db,
        Actor actor,
        int personId,
        CancellationToken cancellationToken) =>
        (from person in db.People.AsNoTracking()
         join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
         where person.Id == personId &&
               actor.HasCaseManagerPermissions &&
               owner.Id == actor.UserId &&
               owner.AgencyId == actor.AgencyId &&
               owner.Role == actor.Role &&
               owner.Permissions == actor.Permissions &&
               person.AgencyId == actor.AgencyId
         select person.Id).AnyAsync(cancellationToken);

    public static async Task<bool> CanAuthorAssessmentAsync(
        ApiDbContext db,
        Actor actor,
        ServerComprehensiveAssessment assessment,
        CancellationToken cancellationToken) =>
        assessment.AuthorUserId == actor.UserId &&
        await OwnsPersonAsync(db, actor, assessment.PersonId, cancellationToken);
}

internal sealed class ValidatedActorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<ApiDbContext>();
        var claimedActor = Actor.FromUnvalidatedClaims(context.HttpContext.User);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == claimedActor.UserId &&
            candidate.AgencyId == claimedActor.AgencyId &&
            candidate.Role == claimedActor.Role,
            context.HttpContext.RequestAborted);
        if (user is null || !UserPermissionRules.IsSupported(user.Permissions))
            return Results.Unauthorized();

        var identity = context.HttpContext.User.Identity as ClaimsIdentity;
        if (identity is null)
            return Results.Unauthorized();
        identity.AddClaim(new Claim(
            Actor.ValidatedPermissionsClaim,
            ((int)user.Permissions).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var actor = Actor.From(context.HttpContext.User);

        // The cross-tenant support identity is deliberately not an agency user. Keep
        // it on the narrow platform surface even though its token carries an agency
        // anchor for authentication-integrity checks.
        var path = context.HttpContext.Request.Path;
        if (actor.Role == "PlatformOperator" &&
            !path.StartsWithSegments("/api/v1/platform") &&
            path != "/api/v1/incidents" &&
            path != "/api/v1/users/me/password" &&
            path != "/api/v1/me")
            return Results.Forbid();

        return await next(context);
    }
}
