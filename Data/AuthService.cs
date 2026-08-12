using Microsoft.EntityFrameworkCore;
using Sati.Models;
using System.Security;

namespace Sati.Data
{
    public class AuthService : IAuthService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public AuthService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<User?> AuthenticateAsync(string username, SecureString password)
        {
            await using var context = _contextFactory.CreateDbContext();

            var userEntity = await context.Users
                .SingleOrDefaultAsync(u => u.Username == username);

            if (userEntity is null)
                return null;

            var passwordHasher = new PasswordHasher();
            var isValid = passwordHasher.Verify(password, userEntity.PasswordHash, userEntity.Salt);

            if (!isValid)
                return null;

            LocalAuditTrail.Record(
                context,
                userEntity,
                LocalAuditActions.AuthenticationSucceeded,
                "User",
                userEntity.Id);
            await context.SaveChangesAsync();

            return User.Create(
                userEntity.Id,
                userEntity.Username,
                userEntity.DisplayName,
                userEntity.PasswordHash,
                userEntity.Salt,
                userEntity.Role,
                userEntity.SupervisorId,
                userEntity.AgencyId
            );
        }
    }
}
