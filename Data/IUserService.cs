using Sati.Models;
using System.Security;

namespace Sati.Data
{
    public interface IUserService
    {
        Task<User> CreateAsync(User user, SecureString initialPassword);

        // True when the installation already has somebody who can administer it.
        // Used to decide whether to OFFER first-run setup; it is not the guard.
        // The guard is inside CreateFirstAdministratorAsync, because anything the
        // caller checks first can be stale by the time it acts.
        Task<bool> AnyAdministratorExistsAsync();

        // Creates the first administrator, and only the first. Re-checks inside
        // the write that no administrator exists and throws
        // AdministratorAlreadyExistsException if one does, so this cannot be used
        // as an unauthenticated way to mint privileged accounts on an installation
        // that already has one.
        //
        // Local implementations only. The cloud implementation refuses: a hosted
        // deployment provisions its first administrator through an operator-run
        // script, not through an endpoint reachable without credentials.
        Task<User> CreateFirstAdministratorAsync(User user, SecureString initialPassword);

        Task<List<User>> GetAllAsync();
        Task UpdateAsync(User user);
        Task ResetPasswordAsync(User user, SecureString newPassword);

        // Self-service password change. Distinct from ResetPasswordAsync: the
        // current password must be verified before the replacement is persisted.
        // Cloud implementations send both values only over the authenticated HTTPS
        // API; local implementations verify and hash directly.
        Task ChangePasswordAsync(User user, SecureString currentPassword, SecureString newPassword);
        Task<List<User>> GetSuperviseesAsync(int supervisorId);

    }
}
