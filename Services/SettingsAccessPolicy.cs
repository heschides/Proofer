using Sati.Contracts.V1;

namespace Sati.Services;

public static class SettingsAccessPolicy
{
    public static bool CanManageAgencySettings(UserPermissions permissions) =>
        UserPermissionRules.HasAdminPermissions(permissions);
}
