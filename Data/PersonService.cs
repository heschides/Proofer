using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
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
            if (!actor.HasCaseManagerPermissions || person.UserId != actor.Id)
            {
                throw new PersonValidationException(new Dictionary<string, string[]>
                {
                    ["owner"] = ["The new client must be assigned to the signed-in case manager."]
                });
            }

            person.AgencyId = actor.AgencyId;
            await using var context = _contextFactory.CreateDbContext();
            if (person.IsTestData)
            {
                var actorIsCurrentAdmin = await context.Users.AsNoTracking().AnyAsync(candidate =>
                    candidate.Id == actor.Id && candidate.AgencyId == actor.AgencyId &&
                    (candidate.Permissions & UserPermissions.Administration) != 0);
                if (!actorIsCurrentAdmin)
                {
                    throw new PersonValidationException(new Dictionary<string, string[]>
                    {
                        ["isTestData"] = ["Only a current Admin can create a consumer marked as Test."]
                    });
                }
            }

            ValidatePerson(person, requireNewForms: person.EffectiveDate.HasValue);
            person.Revision = 1;
            context.People.Add(person);
            PersonLifecycleLedger.RecordCreated(context, actor, person);
            LocalAuditTrail.Record(context, actor, LocalAuditActions.PersonCreated, "Person");
            try
            {
                // One SaveChanges call makes the client, generated forms, lifecycle
                // version, and audit event one transaction. A rejection rolls back
                // the whole graph; there is no partially-created client to repair.
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException exception)
            {
                throw new PersonPersistenceException(
                    "The database rejected the new client transaction.",
                    exception);
            }
            return person;
        }

        public async Task<Person> EditPersonAsync(Person person)
        {
            var actor = CurrentActor();
            ValidatePerson(person, requireNewForms: person.Forms.Any(form => form.Id == 0));
            await using var context = _contextFactory.CreateDbContext();
            var stored = await context.People.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == person.Id &&
                    candidate.AgencyId == actor.AgencyId);
            if (stored is null)
                throw new InvalidOperationException("This Person was not found in your agency.");
            if (person.Revision != stored.Revision)
                throw new InvalidOperationException(
                    "This Person was changed after you opened it. Reload the Person before saving.");
            if (person.IsTestData != stored.IsTestData)
            {
                throw new PersonValidationException(new Dictionary<string, string[]>
                {
                    ["isTestData"] = ["The Test designation is set only when a consumer is created and cannot be changed later."]
                });
            }

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

        // Read, prepend, and write inside ONE short-lived context so the entry is
        // placed against the journal as it exists at write time. The caller never
        // supplies the journal it thinks is current, and never supplies the stamp:
        // JournalEntry composes both. Mirrors the API's journal-entries route —
        // the same agency gate, the same ledger snapshot, the same audit action —
        // because this transitional local path must not enforce the rule its own way.
        public async Task<JournalReminderResult> AddJournalReminderAsync(int personId, string text)
        {
            var actor = CurrentActor();
            await using var context = _contextFactory.CreateDbContext();
            var person = await context.People.SingleOrDefaultAsync(candidate =>
                candidate.Id == personId && candidate.AgencyId == actor.AgencyId);
            if (person is null)
                throw new InvalidOperationException("This Person was not found in your agency.");

            var before = PersonLifecycleLedger.Capture(person);
            await PersonLifecycleLedger.EnsureBaselineAsync(context, person);
            person.Journal = JournalEntry.PrependReminder(person.Journal, DateTime.Now, text);
            if (PersonLifecycleLedger.RecordChanged(context, actor, person, before, "Journal reminder added"))
                LocalAuditTrail.Record(
                    context,
                    actor,
                    LocalAuditActions.PersonJournalReminderAdded,
                    "Person",
                    personId);
            await context.SaveChangesAsync();

            return new JournalReminderResult(person.Journal);
        }

        private User CurrentActor() => _sessionService.CurrentUser
            ?? throw new InvalidOperationException("A signed-in user is required for this operation.");

        private static void ValidatePerson(Person person, bool requireNewForms)
        {
            var errors = PersonSaveRules.Validate(
                PersonContractMapper.ToSaveRequest(person),
                DateTime.Today,
                requireNewForms);
            if (errors.Count == 0)
                return;

            throw new PersonValidationException(errors);
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
