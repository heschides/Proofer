using Sati.Models;

namespace Sati.Services;

public static class SettingsAccessPolicy
{
    public static bool CanManageAgencySettings(UserRole? role) => role == UserRole.Admin;
}
