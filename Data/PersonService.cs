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

        // Reads a single column, not an entity. .Select projects Journal server-side
        // so only that string comes back — the nvarchar(max) never rides a full-row
        // materialization, and nothing is change-tracked. Returns null for a missing
        // person (FirstOrDefaultAsync on the projected string) as well as a genuinely
        // empty journal; the caller treats both as "nothing to show."
        public async Task<string?> GetJournalAsync(int personId)
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.People
                .Where(p => p.Id == personId)
                .Select(p => p.Journal)
                .FirstOrDefaultAsync();
        }

        // Writes a single column via a set-based UPDATE — no entity materialized, no
        // change tracker, no risk of round-tripping stale values on the row's other
        // columns (the reason we don't load-set-save here). ExecuteUpdateAsync bypasses
        // SaveChanges entirely; safe because this service has no interceptors or
        // SaveChanges override doing cross-cutting work. A no-match id updates zero
        // rows silently, which is the correct outcome for a deleted person.
        public async Task SaveJournalAsync(int personId, string? journal)
        {
            await using var context = _contextFactory.CreateDbContext();
            await context.People
                .Where(p => p.Id == personId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Journal, journal));
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

        // Read-only, blob-free caseload load for the supervisor sidebar. Projects straight
        // into PersonSummary/NoteSummary (public setters — no entity-construction wall) so
        // the nvarchar(max) columns (Person.Bio/Journal, Note.Narrative) are never selected.
        // Forms load whole — no blob columns on Form. AsNoTracking; does NOT run
        // EnsureCurrentCycleForms — this is a read path, not the write-bearing full load.
        public async Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();

            // Two flat queries stitched in memory, NOT one query joining both Forms and
            // Notes. A single query with both collections produces a Forms×Notes Cartesian
            // product per person (20 forms × 300 notes = 6,000 rows), which is what made the
            // projected version take ~29s. AsSplitQuery() does not reliably split a
            // Select-into-DTO projection, so we split it by hand.

            // Query 1: people + their forms (one-to-many, no second collection = no product).
            var summaries = await context.People
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .OrderBy(p => p.LastName)
                .Select(p => new PersonSummary
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    FirstName = p.FirstName,
                    LastName = p.LastName,
                    EffectiveDate = p.EffectiveDate,
                    Forms = p.Forms.ToList()
                })
                .ToListAsync();

            // Query 2: blob-free note summaries for this caseload, keyed by PersonId.
            var personIds = summaries.Select(s => s.Id).ToList();
            var notesByPerson = (await context.Notes
                    .AsNoTracking()
                    .Where(n => personIds.Contains(n.PersonId))
                    .Select(n => new { n.PersonId, n.Status, n.EventDate, n.NoteType })
                    .ToListAsync())
                .GroupBy(n => n.PersonId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(n => new NoteSummary
                    {
                        Status = n.Status,
                        EventDate = n.EventDate,
                        NoteType = n.NoteType
                    }).ToList());

            // Stitch: attach each person's notes; empty list if none.
            foreach (var summary in summaries)
                summary.NoteSummaries = notesByPerson.TryGetValue(summary.Id, out var notes)
                    ? notes
                    : [];

            return summaries;
        }
    }
}