using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;

namespace Sati.Api.Security;

internal static class TenantAccess
{
    public static bool IsSupervisorRole(string role) =>
        role is "Supervisor" or "Director" or "Admin";

    public static Task<bool> IsCurrentActorAsync(
        ApiDbContext db,
        Actor actor,
        CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(
            user => user.Id == actor.UserId &&
                    user.AgencyId == actor.AgencyId &&
                    user.Role == actor.Role,
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
            return true;
        if (!IsSupervisorRole(actor.Role))
            return false;

        return await db.Users.AsNoTracking().AnyAsync(
            user => user.Id == targetUserId &&
                    user.AgencyId == actor.AgencyId &&
                    user.Role == "CaseManager" &&
                    (actor.Role == "Director" ||
                     actor.Role == "Admin" ||
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
               owner.Id == actor.UserId &&
               owner.AgencyId == actor.AgencyId &&
               owner.Role == actor.Role &&
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
        var actor = Actor.From(context.HttpContext.User);
        if (!await TenantAccess.IsCurrentActorAsync(db, actor, context.HttpContext.RequestAborted))
            return Results.Unauthorized();

        // The cross-tenant support identity is deliberately not an agency user. Keep
        // it on the narrow platform surface even though its token carries an agency
        // anchor for authentication-integrity checks.
        var path = context.HttpContext.Request.Path;
        if (actor.Role == "PlatformOperator" &&
            !path.StartsWithSegments("/api/v1/platform") &&
            path != "/api/v1/me")
            return Results.Forbid();

        return await next(context);
    }
}
