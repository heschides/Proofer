using Sati.Models;
using System.Security;

namespace Sati.Data
{
    public interface IUserService
    {
        Task<User> CreateAsync(User user);
        Task<List<User>> GetAllAsync();
        Task UpdateAsync(User user);
        Task ResetPasswordAsync(User user, string newPassword);

        // Self-service password change. Distinct from ResetPasswordAsync: takes a
        // SecureString (a user-chosen secret worth protecting) and uses the
        // SecureString hash overload. Identity is verified by the caller before
        // this runs — this method only hashes and saves.
        Task ChangePasswordAsync(User user, SecureString newPassword);
        Task<List<User>> GetSuperviseesAsync(int supervisorId);

        // Sati must never run without an administrator. The only role editor lives behind
        // a supervisor-gated tab, so a database with no Admin can never grow one from
        // inside the app. This backs both guards on that invariant: the first-run gate in
        // App.OnStartup, and the block on demoting the last Admin in user management.
        Task<int> AdminCountAsync();

        // Creates the initial administrator and assigns it the lowest-numbered agency.
        // Refuses when an Admin already exists, so the first-run window cannot be reached
        // or replayed as a back door for minting admins later.
        Task<User> CreateFirstAdminAsync(string username, string displayName, SecureString password);
    }
}