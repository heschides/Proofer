using Microsoft.EntityFrameworkCore;
using Sati.Models;
using Sati.Contracts.V1;
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

        // Authorization lives here, in the write, not in the view model that opened the
        // window. On local Production there is no API behind this method, so a permission
        // checkbox bound to a view-model boolean is the whole enforcement if this does not
        // check — which is what the 2026-08-30 audit found. The rule itself is owned by
        // Sati.Contracts.V1.UserManagementRules and shared with Sati.Api, so the two cannot
        // drift apart.
        public async Task<User> CreateAsync(AgencyActor suppliedActor, User user, SecureString initialPassword)
        {
            ArgumentNullException.ThrowIfNull(user);

            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateActorAsync(context, suppliedActor);

            // Assigned server-side rather than trusted, matching POST /api/v1/users.
            user.AgencyId = actor.AgencyId;
            Refuse(UserManagementRules.DescribeGrantRefusal(
                actor.ToAgencyActor(), user.Permissions, user.SupervisorId, user.AgencyId));

            // The label is derived, never taken from the caller, so no user-management
            // path can mint the cross-tenant PlatformOperator identity.
            user.Role = Enum.Parse<UserRole>(UserPermissionRules.LegacyLabel(user.Permissions));

            if (await context.Users.AnyAsync(candidate => candidate.Username == user.Username))
                throw new InvalidOperationException($"A user named '{user.Username}' already exists.");
            await RequireValidSupervisorAsync(context, actor, user.SupervisorId);

            var (hash, salt) = _hasher.HashPassword(initialPassword);
            user.SetPassword(hash, salt);
            context.Users.Add(user);
            await context.SaveChangesAsync();
            return user;
        }

        public async Task<bool> AnyAdministratorExistsAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.Users.AnyAsync(user =>
                (user.Permissions & UserPermissions.Administration) != 0);
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
            if (await context.Users.AnyAsync(candidate =>
                    (candidate.Permissions & UserPermissions.Administration) != 0))
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
            user.Permissions = UserPermissions.AllAgencyPermissions;
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

        public async Task UpdateAsync(AgencyActor suppliedActor, User user)
        {
            ArgumentNullException.ThrowIfNull(user);

            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateActorAsync(context, suppliedActor);

            var tracked = await context.Users.FindAsync(user.Id);
            if (tracked is null)
                return;
            RequireManageable(actor, tracked);
            Refuse(UserManagementRules.DescribeGrantRefusal(
                actor.ToAgencyActor(), user.Permissions, user.SupervisorId, tracked.AgencyId));
            await RequireValidSupervisorAsync(context, actor, user.SupervisorId);

            // Agency and derived label are set here rather than accepted, so neither a stale
            // in-memory object nor a caller can move a user across tenants or relabel one.
            user.AgencyId = tracked.AgencyId;
            user.Role = Enum.Parse<UserRole>(UserPermissionRules.LegacyLabel(user.Permissions));

            // CurrentValues.SetValues copies scalar + FK properties only,
            // never navigations — so a stale self-referencing Supervisor nav
            // can't override the new SupervisorId during fixup.
            context.Entry(tracked).CurrentValues.SetValues(user);
            await context.SaveChangesAsync();
        }

        // Self-service profile edit. Deliberately not UpdateAsync with a relaxed rule: this
        // takes the two fields a user may change about themselves and cannot express a
        // permission, agency, supervisor, or label change at all.
        public async Task UpdateOwnContactDetailsAsync(AgencyActor suppliedActor, User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            if (user.Id != suppliedActor.UserId)
                throw new UnauthorizedAccessException("You may edit only your own profile.");
            if (user.Email?.Length > 254)
                throw new InvalidOperationException("Email must not exceed 254 characters.");
            if (user.Phone?.Length > 30)
                throw new InvalidOperationException("Phone must not exceed 30 characters.");

            await using var context = _contextFactory.CreateDbContext();
            // Not ValidateActorAsync: editing your own contact details is not a user-management
            // action and must not require supervision or administration.
            var tracked = await context.Users.SingleOrDefaultAsync(candidate =>
                              candidate.Id == suppliedActor.UserId &&
                              candidate.AgencyId == suppliedActor.AgencyId &&
                              candidate.Permissions == suppliedActor.Permissions)
                          ?? throw new UnauthorizedAccessException(
                              "The actor no longer matches the current user record.");

            // Only these two fields are copied. Permissions, Role, SupervisorId, and AgencyId
            // on the incoming object are ignored rather than trusted.
            tracked.Email = user.Email;
            tracked.Phone = user.Phone;
            await context.SaveChangesAsync();
        }

        public async Task ResetPasswordAsync(AgencyActor suppliedActor, User user, SecureString newPassword)
        {
            ArgumentNullException.ThrowIfNull(user);

            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateActorAsync(context, suppliedActor);

            var tracked = await context.Users.FindAsync(user.Id)
                ?? throw new InvalidOperationException("The user no longer exists.");
            RequireManageable(actor, tracked);

            var (hash, salt) = _hasher.HashPassword(newPassword);
            tracked.SetPassword(hash, salt);
            user.SetPassword(hash, salt);
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// Re-confirms a caller-supplied actor against the database. Mirrors
        /// <c>ValidateBillingActorAsync</c> and the API's <c>ValidatedActorFilter</c>: a
        /// supplied permission set is never trusted, only matched.
        /// </summary>
        private static async Task<User> ValidateActorAsync(SatiContext context, AgencyActor suppliedActor)
        {
            if (!UserPermissionRules.IsSupported(suppliedActor.Permissions) ||
                !UserManagementRules.CanManageUsers(suppliedActor.Permissions))
                throw new UnauthorizedAccessException(UserManagementRules.RequiresUserManagement);

            return await context.Users.SingleOrDefaultAsync(candidate =>
                       candidate.Id == suppliedActor.UserId &&
                       candidate.AgencyId == suppliedActor.AgencyId &&
                       candidate.Permissions == suppliedActor.Permissions)
                   ?? throw new UnauthorizedAccessException(
                       "The actor no longer matches the current user record.");
        }

        // What the actor may act ON, as opposed to what they may grant. Mirrors the
        // same-agency, no-PlatformOperator, assigned-case-manager checks on
        // PUT /api/v1/users/{userId}.
        private static void RequireManageable(User actor, User target)
        {
            if (target.AgencyId != actor.AgencyId)
                throw new UnauthorizedAccessException(UserManagementRules.ForeignAgency);
            if (target.Role == UserRole.PlatformOperator)
                throw new UnauthorizedAccessException(UserManagementRules.PlatformOperatorNotManageable);
            if (!actor.HasAdminPermissions &&
                (!UserPermissionRules.HasCaseManagerPermissions(target.Permissions) ||
                 target.SupervisorId != actor.Id))
                throw new UnauthorizedAccessException(UserManagementRules.SupervisorScope);
        }

        private static async Task RequireValidSupervisorAsync(SatiContext context, User actor, int? supervisorId)
        {
            if (!supervisorId.HasValue)
                return;
            if (!await context.Users.AsNoTracking().AnyAsync(candidate =>
                    candidate.Id == supervisorId && candidate.AgencyId == actor.AgencyId &&
                    (candidate.Permissions & UserPermissions.Supervision) != 0))
                throw new InvalidOperationException("The selected supervisor is invalid.");
        }

        private static void Refuse(UserManagementRules.Refusal? refusal)
        {
            if (refusal is not null)
                throw new UnauthorizedAccessException(refusal.Message);
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
                .Where(u => u.SupervisorId == supervisorId &&
                    (u.Permissions & UserPermissions.CaseManagement) != 0)
                .ToListAsync();
        }
    }
}
