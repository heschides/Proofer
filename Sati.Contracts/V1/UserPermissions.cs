namespace Sati.Contracts.V1;

/// <summary>
/// Independent capabilities granted to one agency user. These are authorization facts,
/// not job titles; a case manager may also bill without becoming an administrator.
/// </summary>
[Flags]
public enum UserPermissions
{
    None = 0,
    CaseManagement = 1 << 0,
    Supervision = 1 << 1,
    Administration = 1 << 2,
    Billing = 1 << 3,

    /// <summary>
    /// Supervisory reach across the whole agency rather than only directly assigned case
    /// managers. Separate from <see cref="Administration"/> on purpose: reviewing everyone's
    /// notes and holding the audit export, settings, and destructive test-data routes are
    /// different powers, and the legacy Director label held the first without the second.
    /// Folding them together forced the backfill to over-grant.
    /// </summary>
    AgencyWideSupervision = 1 << 4,

    AllAgencyPermissions =
        CaseManagement | Supervision | Administration | Billing | AgencyWideSupervision
}

/// <summary>
/// The minimum caller identity a service needs for authorization. API callers must
/// construct it from validated server state, never from request-supplied values.
/// </summary>
public readonly record struct AgencyActor(
    int UserId,
    int AgencyId,
    UserPermissions Permissions);

/// <summary>Sole owner of permission interpretation for the desktop and API.</summary>
public static class UserPermissionRules
{
    public static bool HasCaseManagerPermissions(UserPermissions permissions) =>
        IsSupported(permissions) && permissions.HasFlag(UserPermissions.CaseManagement);

    public static bool HasSupervisorPermissions(UserPermissions permissions) =>
        IsSupported(permissions) && permissions.HasFlag(UserPermissions.Supervision);

    public static bool HasAdminPermissions(UserPermissions permissions) =>
        IsSupported(permissions) && permissions.HasFlag(UserPermissions.Administration);

    public static bool HasBillingPermissions(UserPermissions permissions) =>
        IsSupported(permissions) && permissions.HasFlag(UserPermissions.Billing);

    /// <summary>
    /// Whether supervisory queries reach every case manager in the agency rather than only
    /// the actor's own assignees. Administration implies it, because an administrator who
    /// can export the whole agency's audit trail is not meaningfully restrained from
    /// reading its notes.
    /// </summary>
    public static bool HasAgencyWideSupervisionPermissions(UserPermissions permissions) =>
        IsSupported(permissions) &&
        (permissions.HasFlag(UserPermissions.AgencyWideSupervision) ||
         permissions.HasFlag(UserPermissions.Administration));

    public static bool IsSupported(UserPermissions permissions) =>
        (permissions & ~UserPermissions.AllAgencyPermissions) == UserPermissions.None;

    /// <summary>
    /// One-time compatibility mapping used by the migration, constructors, and old test
    /// fixtures. Runtime authorization reads the persisted permission set instead.
    /// </summary>
    /// <remarks>
    /// Director maps to agency-wide supervision, NOT administration. Under the old role
    /// string every administration gate read <c>Role != "Admin"</c>, which denied Director;
    /// what Director did hold was agency-wide note review. Granting administration here
    /// would hand every existing Director the audit export, settings writes, destructive
    /// test-data deletion, and provider merge on upgrade. See the 2026-08-30 entry in
    /// DECISIONS.md and finding 3 of the third pass in API_SECURITY_AUDIT.md.
    /// </remarks>
    public static UserPermissions FromLegacyRole(string? role) => role switch
    {
        "CaseManager" => UserPermissions.CaseManagement,
        "Supervisor" => UserPermissions.CaseManagement | UserPermissions.Supervision,
        "Director" => UserPermissions.CaseManagement | UserPermissions.Supervision |
                      UserPermissions.AgencyWideSupervision,
        "Admin" => UserPermissions.AllAgencyPermissions,
        _ => UserPermissions.None
    };

    /// <summary>
    /// Compatibility label for legacy records and signed-document snapshots. It is never
    /// consulted for authorization. Billing alone deliberately does not become Admin.
    /// </summary>
    public static string LegacyLabel(UserPermissions permissions) =>
        HasAdminPermissions(permissions) ? "Admin" :
        IsSupported(permissions) && permissions.HasFlag(UserPermissions.AgencyWideSupervision)
            ? "Director" :
        HasSupervisorPermissions(permissions) ? "Supervisor" :
        "CaseManager";

    public static string Describe(UserPermissions permissions)
    {
        var names = new List<string>(5);
        if (HasCaseManagerPermissions(permissions)) names.Add("Case management");
        if (HasSupervisorPermissions(permissions)) names.Add("Supervision");
        if (IsSupported(permissions) && permissions.HasFlag(UserPermissions.AgencyWideSupervision))
            names.Add("Agency-wide supervision");
        if (HasAdminPermissions(permissions)) names.Add("Administration");
        if (HasBillingPermissions(permissions)) names.Add("Billing");
        return names.Count == 0 ? "No agency permissions" : string.Join(", ", names);
    }
}
