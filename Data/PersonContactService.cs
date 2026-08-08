using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public sealed class PersonContactService : IPersonContactService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public PersonContactService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<List<PersonContact>> GetActiveByPersonAsync(int personId)
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.PersonContacts
                .AsNoTracking()
                .Where(contact => contact.PersonId == personId && contact.IsActive)
                .OrderBy(contact => contact.LastName)
                .ThenBy(contact => contact.FirstName)
                .ToListAsync();
        }

        public async Task<PersonContact> SaveAsync(PersonContact contact)
        {
            await using var context = _contextFactory.CreateDbContext();

            contact.FirstName = contact.FirstName.Trim();
            contact.LastName = contact.LastName.Trim();
            contact.Relationship = Normalize(contact.Relationship);
            contact.Organization = Normalize(contact.Organization);
            contact.Phone = Normalize(contact.Phone);
            contact.Email = Normalize(contact.Email);

            if (contact.Id == 0)
                context.PersonContacts.Add(contact);
            else
                context.PersonContacts.Update(contact);

            await context.SaveChangesAsync();
            return contact;
        }

        public async Task ArchiveAsync(int contactId)
        {
            await using var context = _contextFactory.CreateDbContext();
            await context.PersonContacts
                .Where(contact => contact.Id == contactId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(contact => contact.IsActive, false));
        }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
