using Sati.Models;

namespace Sati.Services;

public static class AccountSwitchPolicy
{
    public static bool RequiresDirectSignIn(UserRole currentRole) =>
        currentRole == UserRole.PlatformOperator;
}
