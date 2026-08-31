namespace Sati.Contracts.V1;

/// <summary>
/// Who may create or edit an agency user, and what permissions they may hand out.
///
/// <para>
/// Sole owner of that decision for both `Sati.Api` and the transitional desktop-local
/// `UserService`. Before this existed the API held the rule and the desktop held nothing,
/// so on local Production the only restraint was a view-model boolean — see finding 5 of
/// the 2026-08-30 pass in API_SECURITY_AUDIT.md. A second hand-written copy of this rule
/// is a defect, not a convenience.
/// </para>
/// </summary>
public static class UserManagementRules
{
    public const string RequiresUserManagement =
        "Managing users requires supervision or administration permission.";
    public const string EmptyOrUnsupported =
        "Choose at least one supported agency permission.";
    public const string ForeignAgency =
        "Users must belong to your agency.";
    public const string SupervisorScope =
        "Supervisors may manage only their assigned case managers.";
    public const string PlatformOperatorNotManageable =
        "The platform operator identity is not an agency user.";

    /// <summary>Field name a refusal belongs against, for callers building a validation problem.</summary>
    public sealed record Refusal(string Field, string Message);

    public static bool CanManageUsers(UserPermissions permissions) =>
        UserPermissionRules.HasSupervisorPermissions(permissions) ||
        UserPermissionRules.HasAdminPermissions(permissions);

    /// <summary>
    /// Null when <paramref name="actor"/> may write a user carrying
    /// <paramref name="requestedPermissions"/>; otherwise the reason it is refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Administration is the root capability: an administrator may grant anything, including
    /// permissions they do not personally hold. That is deliberate, and it is not a hole. A
    /// "you may not grant what you do not hold" subset test looks like a boundary and is not
    /// one — whoever may create a user also chooses that user's initial password, so an
    /// administrator without billing can already mint a billing user and sign in as it. The
    /// test would block an administrator from creating an ordinary case manager while
    /// stopping nothing, so it is deliberately absent.
    /// </para>
    /// <para>
    /// The rule that does carry weight is the non-administrator branch. It is the successor
    /// to the old ladder's "only an administrator may create or assign an administrator":
    /// anyone without administration may write only a case-management-only user assigned to
    /// themself, so supervision alone can never produce an administrator, a biller, or an
    /// agency-wide reviewer — nor edit its own record into one, since a user is not their own
    /// supervisee. The legacy Director label reaches that branch because it backfills to
    /// agency-wide supervision rather than administration; see
    /// <see cref="UserPermissionRules.FromLegacyRole"/>.
    /// </para>
    /// </remarks>
    public static Refusal? DescribeGrantRefusal(
        AgencyActor actor,
        UserPermissions requestedPermissions,
        int? requestedSupervisorId,
        int requestedAgencyId)
    {
        if (!UserPermissionRules.IsSupported(actor.Permissions) ||
            !CanManageUsers(actor.Permissions))
            return new Refusal("permissions", RequiresUserManagement);

        if (requestedPermissions == UserPermissions.None ||
            !UserPermissionRules.IsSupported(requestedPermissions))
            return new Refusal("permissions", EmptyOrUnsupported);

        if (requestedAgencyId != actor.AgencyId)
            return new Refusal("agencyId", ForeignAgency);

        if (!UserPermissionRules.HasAdminPermissions(actor.Permissions) &&
            (requestedPermissions != UserPermissions.CaseManagement ||
             requestedSupervisorId != actor.UserId))
            return new Refusal("permissions", SupervisorScope);

        return null;
    }
}
