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

        public async Task<int> AdminCountAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.Users.CountAsync(u => u.Role == UserRole.Admin);
        }

        // The agency is resolved here rather than chosen in the UI: at first run the
        // database holds only the seeded agencies, and asking someone to pick one before
        // they have seen the app is noise. Admins can reassign afterwards.
        public async Task<User> CreateFirstAdminAsync(
            string username, string displayName, SecureString password)
        {
            await using var context = _contextFactory.CreateDbContext();

            if (await context.Users.AnyAsync(u => u.Role == UserRole.Admin))
                throw new InvalidOperationException(
                    "An administrator already exists. Use user management to change roles.");

            if (await context.Users.AnyAsync(u => u.Username == username))
                throw new InvalidOperationException("A user with that username already exists.");

            var agencyId = await context.Agencies
                .OrderBy(a => a.Id)
                .Select(a => a.Id)
                .FirstOrDefaultAsync();

            if (agencyId == 0)
                throw new InvalidOperationException(
                    "No agency exists to assign the administrator to.");

            var (hash, salt) = _hasher.HashPassword(password);
            var admin = User.Create(0, username, displayName, hash, salt, UserRole.Admin, null, agencyId);

            context.Users.Add(admin);
            await context.SaveChangesAsync();
            return admin;
        }
    }
}