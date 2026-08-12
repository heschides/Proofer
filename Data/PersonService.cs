using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class PersonService : IPersonService
    {

        private static readonly bool EnableEnsureCycleFormsOnLoad = false;

        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISettingsService _settingsService;
        private readonly ISessionService _sessionService;

        public PersonService(
            IDbContextFactory<SatiContext> contextFactory,
            ISettingsService settingsService,
            ISessionService sessionService)
        {
            _contextFactory = contextFactory;
            _settingsService = settingsService;
            _sessionService = sessionService;
        }

        public async Task<Person> AddPersonAsync(Person person)
        {
            var actor = CurrentActor();
            await using var context = _contextFactory.CreateDbContext();
            person.AgencyId ??= actor.AgencyId;
            person.Revision = 1;
            context.People.Add(person);
            PersonLifecycleLedger.RecordCreated(context, actor, person);
            LocalAuditTrail.Record(context, actor, LocalAuditActions.PersonCreated, "Person");
            await context.SaveChangesAsync();
            return person;
        }

        public async Task<Person> EditPersonAsync(Person person)
        {
            var actor = CurrentActor();
            await using var context = _contextFactory.CreateDbContext();
            var stored = await context.People.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == person.Id &&
                    candidate.AgencyId == actor.AgencyId);
            if (stored is null)
                throw new InvalidOperationException("This Person was not found in your agency.");
            if (person.Revision != stored.Revision)
                throw new InvalidOperationException(
                    "This Person was changed after you opened it. Reload the Person before saving.");

            var before = PersonLifecycleLedger.Capture(stored);
            await PersonLifecycleLedger.EnsureBaselineAsync(context, stored);
            context.People.Update(person);
            context.Entry(person).Property(candidate => candidate.Revision).OriginalValue = stored.Revision;
            if (PersonLifecycleLedger.RecordChanged(context, actor, person, before, "Updated"))
                LocalAuditTrail.Record(context, actor, LocalAuditActions.PersonUpdated, "Person", person.Id);
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

        // Journal edits now load the authoritative Person row so the revision token,
        // append-only lifecycle snapshot, and lightweight audit event commit together.
        // Only Journal and Revision are changed on this freshly loaded entity, so stale
        // values from the caller are never round-tripped onto the other profile fields.
        public async Task SaveJournalAsync(int personId, string? journal)
        {
            var actor = CurrentActor();
            await using var context = _contextFactory.CreateDbContext();
            var person = await context.People.SingleOrDefaultAsync(candidate =>
                candidate.Id == personId && candidate.AgencyId == actor.AgencyId);
            if (person is null)
                throw new InvalidOperationException("This Person was not found in your agency.");

            var before = PersonLifecycleLedger.Capture(person);
            await PersonLifecycleLedger.EnsureBaselineAsync(context, person);
            person.Journal = journal;
            if (PersonLifecycleLedger.RecordChanged(context, actor, person, before, "Journal updated"))
                LocalAuditTrail.Record(
                    context,
                    actor,
                    LocalAuditActions.PersonJournalUpdated,
                    "Person",
                    personId);
            await context.SaveChangesAsync();
        }

        private User CurrentActor() => _sessionService.CurrentUser
            ?? throw new InvalidOperationException("A signed-in user is required for this operation.");

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
