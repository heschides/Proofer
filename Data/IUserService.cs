using Sati.Models;
using System.Security;

namespace Sati.Data
{
    public interface IUserService
    {
        Task<User> CreateAsync(User user, SecureString initialPassword);
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
