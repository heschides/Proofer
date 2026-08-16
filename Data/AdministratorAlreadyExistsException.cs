namespace Sati.Data;

/// <summary>
/// Thrown when first-run administrator setup is attempted on an installation that
/// already has an administrator.
///
/// This is the closing of the bootstrap window, and it is deliberately an
/// exception rather than a silently ignored no-op: reaching it means something
/// tried to mint a privileged account through a path that is only supposed to be
/// open once, and that should be loud.
/// </summary>
public sealed class AdministratorAlreadyExistsException : Exception
{
    public AdministratorAlreadyExistsException()
        : base("This installation already has an administrator. Sign in as that administrator to create further accounts.")
    {
    }
}
