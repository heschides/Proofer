using Microsoft.EntityFrameworkCore;
using Sati.Models;
using System.Security;

namespace Sati.Data
{
    public class UserService : IUserService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly IPasswordHasher _hasher;

        public UserService(IDbContextFactory<SatiContext> contextFactory, IPasswordHasher hasher)
        {
            _contextFactory = contextFactory;
            _hasher = hasher;
        }

        public async Task<User> CreateAsync(User user)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.Users.Add(user);
            await context.SaveChangesAsync();
            return user;
        }

        public async Task<List<User>> GetAllAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.Users
                .Include(u => u.Supervisees)
                .ToListAsync();
        }

        public async Task UpdateAsync(User user)
        {
            await using var context = _contextFactory.CreateDbContext();

            var tracked = await context.Users.FindAsync(user.Id);
            if (tracked is null)
                return;

            // CurrentValues.SetValues copies scalar + FK properties only,
            // never navigations — so a stale self-referencing Supervisor nav
            // can't override the new SupervisorId during fixup.
            context.Entry(tracked).CurrentValues.SetValues(user);
            await context.SaveChangesAsync();
        }

        public async Task ResetPasswordAsync(User user, string newPassword)
        {
            await using var context = _contextFactory.CreateDbContext();
            var (hash, salt) = _hasher.HashPassword(newPassword);
            user.SetPassword(hash, salt);
            context.Users.Update(user);
            await context.SaveChangesAsync();
        }

        // Self-service change. Mirrors ResetPasswordAsync's persistence exactly,
        // but hashes a SecureString via the secure HashPassword overload — a
        // user's chosen password is a secret worth protecting in transit, unlike
        // the reset's known literal. Assumes the caller has already verified
        // identity (via AuthenticateAsync); this only hashes and saves.
        public async Task ChangePasswordAsync(User user, SecureString newPassword)
        {
            await using var context = _contextFactory.CreateDbContext();
            var (hash, salt) = _hasher.HashPassword(newPassword);
            user.SetPassword(hash, salt);
            context.Users.Update(user);
            await context.SaveChangesAsync();
        }

        public async Task<List<User>> GetSuperviseesAsync(int supervisorId)
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.Users
                .Where(u => u.SupervisorId == supervisorId && u.Role == UserRole.CaseManager)
                .ToListAsync();
        }
    }
}