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

    }
}