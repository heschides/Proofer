using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public sealed class PersonContactService : IPersonContactService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISessionService _sessionService;

        public PersonContactService(IDbContextFactory<SatiContext> contextFactory, ISessionService sessionService)
        {
            _contextFactory = contextFactory;
            _sessionService = sessionService;
        }

        public async Task<List<PersonContact>> GetActiveByPersonAsync(int personId)
        {
            await using var context = _contextFactory.CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            await EnsureOwnedAsync(context, personId);
            var contacts = await context.PersonContacts
                .AsNoTracking()
                .Where(contact => contact.PersonId == personId && contact.IsActive)
                .OrderBy(contact => contact.LastName)
                .ThenBy(contact => contact.FirstName)
                .ToListAsync();
            await transaction.CommitAsync();
            return contacts;
        }

        public async Task<PersonContact> SaveAsync(PersonContact contact)
        {
            await using var context = _contextFactory.CreateDbContext();
            await using var signatureChangeTransaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var actor = await EnsureOwnedAsync(context, contact.PersonId);

            contact.FirstName = contact.FirstName.Trim();
            contact.LastName = contact.LastName.Trim();
            contact.Relationship = Normalize(contact.Relationship);
            contact.Organization = Normalize(contact.Organization);
            contact.Phone = Normalize(contact.Phone);
            contact.Email = Normalize(contact.Email);

            if (contact.Id == 0)
                context.PersonContacts.Add(contact);
            else
            {
                var stored = await context.PersonContacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contact.Id)
                    ?? throw new InvalidOperationException("This contact no longer exists.");
                if (stored.PersonId != contact.PersonId)
                    throw new InvalidOperationException("A contact cannot be moved to another consumer.");
                if ((stored.FirstName, stored.LastName, stored.Email, stored.Kind, stored.IsActive) !=
                    (contact.FirstName, contact.LastName, contact.Email, contact.Kind, contact.IsActive))
                    await SignaturePersistenceMutations.RevokeOpenForSignerAsync(context, contact.PersonId, contact.Id, actor.Id, DateTime.UtcNow);
                context.PersonContacts.Update(contact);
            }

            await context.SaveChangesAsync();
            await signatureChangeTransaction.CommitAsync();
            return contact;
        }

        public async Task ArchiveAsync(int contactId)
        {
            await using var context = _contextFactory.CreateDbContext();
            await using var signatureChangeTransaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var contact = await context.PersonContacts.SingleOrDefaultAsync(x => x.Id == contactId);
            if (contact is null) return;
            var actor = await EnsureOwnedAsync(context, contact.PersonId);
            contact.IsActive = false;
            await SignaturePersistenceMutations.RevokeOpenForSignerAsync(context, contact.PersonId, contactId, actor.Id, DateTime.UtcNow);
            await context.SaveChangesAsync();
            await signatureChangeTransaction.CommitAsync();
        }

        private async Task<User> EnsureOwnedAsync(SatiContext context, int personId)
        {
            var actor = _sessionService.CurrentUser;
            if (actor is null || !await LocalTenantAccess.OwnsPersonAsync(context, actor, personId))
                throw new InvalidOperationException("This consumer is not available in your current caseload.");
            return actor;
        }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
