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

        public async Task<User> CreateAsync(User user, SecureString initialPassword)
        {
            await using var context = _contextFactory.CreateDbContext();
            var (hash, salt) = _hasher.HashPassword(initialPassword);
            user.SetPassword(hash, salt);
            context.Users.Add(user);
            await context.SaveChangesAsync();
            return user;
        }

        public async Task<bool> AnyAdministratorExistsAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.Users.AnyAsync(user => user.Role == UserRole.Admin);
        }

        // The bootstrap window, and the thing that closes it.
        //
        // The existence check happens HERE, against the database, immediately
        // before the insert — not in the view model that opened the window. A
        // caller that checked first and acted later would be acting on a fact that
        // may have changed, and the check that matters is the one the write itself
        // performs.
        //
        // The role is forced rather than trusted from the incoming user, so this
        // path can only ever produce an administrator. Producing something else
        // would leave the installation still without one and the window still open.
        public async Task<User> CreateFirstAdministratorAsync(User user, SecureString initialPassword)
        {
            ArgumentNullException.ThrowIfNull(user);

            await using var context = _contextFactory.CreateDbContext();
            if (await context.Users.AnyAsync(candidate => candidate.Role == UserRole.Admin))
                throw new AdministratorAlreadyExistsException();

            if (await context.Users.AnyAsync(candidate => candidate.Username == user.Username))
                throw new InvalidOperationException($"A user named '{user.Username}' already exists.");

            user.AgencyId = await ResolveBootstrapAgencyIdAsync(context);

            var (hash, salt) = _hasher.HashPassword(initialPassword);
            user.SetPassword(hash, salt);
            // Forced rather than trusted from the caller: this path exists to end
            // the state of having no administrator, and anything else would leave
            // that state intact with the window still open.
            user.Role = UserRole.Admin;
            context.Users.Add(user);
            await context.SaveChangesAsync();
            return user;
        }

        /// <summary>
        /// Which agency the first administrator belongs to.
        ///
        /// Every Sati database ships with two seeded agencies ("Internal" and
        /// "Sandbox Mode"), so simply picking the only one is not available. The
        /// answer that is actually right is THE AGENCY THE EXISTING PEOPLE ARE IN:
        /// an administrator exists to administer the agency that holds the work,
        /// and on the installation this feature was built for that is a single
        /// case manager sitting in one of them.
        ///
        /// PlatformOperator accounts are excluded because they are Sati's own
        /// cross-tenant identity and their agency says nothing about which tenant
        /// needs administering.
        ///
        /// Genuine ambiguity — real users spread across several agencies — is
        /// reported rather than guessed at. Attaching the only account that can
        /// administer the system to the wrong tenant is not a mistake that
        /// announces itself afterwards.
        /// </summary>
        private static async Task<int> ResolveBootstrapAgencyIdAsync(SatiContext context)
        {
            var occupiedAgencyIds = await context.Users
                .Where(user => user.Role != UserRole.PlatformOperator)
                .Select(user => user.AgencyId)
                .Distinct()
                .Take(2)
                .ToListAsync();

            if (occupiedAgencyIds.Count == 1)
                return occupiedAgencyIds[0];

            if (occupiedAgencyIds.Count > 1)
                throw new InvalidOperationException(
                    "This database has users in more than one agency, so the administrator's agency is ambiguous. " +
                    "Provision the administrator with the provisioning script, which takes the agency explicitly.");

            // A genuinely empty installation. Fall back to the lowest seeded
            // agency, which is the primary one rather than the sandbox.
            var agencyId = await context.Agencies
                .OrderBy(agency => agency.Id)
                .Select(agency => (int?)agency.Id)
                .FirstOrDefaultAsync();

            return agencyId ?? throw new InvalidOperationException(
                "This database has no agency, so an administrator cannot be attached to one.");
        }

        public async Task<List<User>> GetAllAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.Users
                .Where(user => user.Role != UserRole.PlatformOperator)
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

        public async Task ResetPasswordAsync(User user, SecureString newPassword)
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
        public async Task ChangePasswordAsync(User user, SecureString currentPassword, SecureString newPassword)
        {
            await using var context = _contextFactory.CreateDbContext();
            var tracked = await context.Users.FindAsync(user.Id)
                ?? throw new InvalidOperationException("The current user no longer exists.");
            if (!_hasher.Verify(currentPassword, tracked.PasswordHash, tracked.Salt))
                throw new UnauthorizedAccessException("The current password is incorrect.");
            var (hash, salt) = _hasher.HashPassword(newPassword);
            tracked.SetPassword(hash, salt);
            user.SetPassword(hash, salt);
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
