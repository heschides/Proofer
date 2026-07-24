using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class PersonService : IPersonService
    {

        private const bool EnableEnsureCycleFormsOnLoad = false;

        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISettingsService _settingsService;

        public PersonService(IDbContextFactory<SatiContext> contextFactory, ISettingsService settingsService)
        {
            _contextFactory = contextFactory;
            _settingsService = settingsService;
        }

        public async Task<Person> AddPersonAsync(Person person)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.People.Add(person);
            await context.SaveChangesAsync();
            return person;
        }

        public async Task DeletePersonAsync(Person person)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.People.Remove(person);
            await context.SaveChangesAsync();
        }

        public async Task<Person> EditPersonAsync(Person person)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.People.Update(person);
            await context.SaveChangesAsync();
            return person;
        }

        public async Task<List<Person>> GetAllPeopleAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var people = await context.People
                .Where(p => p.UserId == userId)
                .Include(p => p.Notes)
                .Include(p => p.Forms)
                .OrderBy(p => p.LastName)
                .AsSplitQuery()
                .ToListAsync();

            if (EnableEnsureCycleFormsOnLoad)
            {
                var settings = await _settingsService.LoadAsync();
                var today = DateTime.Today;
                var anyChanges = false;

                foreach (var person in people)
                {
                    if (person.EnsureCurrentCycleForms(today, settings))
                        anyChanges = true;
                }

                if (anyChanges)
                    await context.SaveChangesAsync();
            }

            return people;
        }
    }
}