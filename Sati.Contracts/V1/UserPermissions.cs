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
    AllAgencyPermissions = CaseManagement | Supervision | Administration | Billing
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

    public static bool IsSupported(UserPermissions permissions) =>
        (permissions & ~UserPermissions.AllAgencyPermissions) == UserPermissions.None;

    /// <summary>
    /// One-time compatibility mapping used by the migration, constructors, and old test
    /// fixtures. Runtime authorization reads the persisted permission set instead.
    /// </summary>
    public static UserPermissions FromLegacyRole(string? role) => role switch
    {
        "CaseManager" => UserPermissions.CaseManagement,
        "Supervisor" => UserPermissions.CaseManagement | UserPermissions.Supervision,
        "Director" => UserPermissions.CaseManagement | UserPermissions.Supervision | UserPermissions.Administration,
        "Admin" => UserPermissions.AllAgencyPermissions,
        _ => UserPermissions.None
    };

    /// <summary>
    /// Compatibility label for legacy records and signed-document snapshots. It is never
    /// consulted for authorization. Billing alone deliberately does not become Admin.
    /// </summary>
    public static string LegacyLabel(UserPermissions permissions) =>
        HasAdminPermissions(permissions) ? "Admin" :
        HasSupervisorPermissions(permissions) ? "Supervisor" :
        "CaseManager";

    public static string Describe(UserPermissions permissions)
    {
        var names = new List<string>(4);
        if (HasCaseManagerPermissions(permissions)) names.Add("Case management");
        if (HasSupervisorPermissions(permissions)) names.Add("Supervision");
        if (HasAdminPermissions(permissions)) names.Add("Administration");
        if (HasBillingPermissions(permissions)) names.Add("Billing");
        return names.Count == 0 ? "No agency permissions" : string.Join(", ", names);
    }
}
