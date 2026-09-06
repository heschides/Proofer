using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using System.Text.Json;

namespace Sati.Data
{
    public class PersonService : IPersonService
    {
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
            AddInitialFormAttestations(context, actor, person, person.Forms);
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
            await using var signatureChangeTransaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            if (!await LocalTenantAccess.OwnsPersonAsync(context, actor, person.Id))
                throw new InvalidOperationException("This consumer is not available in your current caseload.");
            var stored = await context.People.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == person.Id &&
                    candidate.AgencyId == actor.AgencyId);
            if (stored is null)
                throw new InvalidOperationException("This Person was not found in your agency.");
            if (person.UserId != stored.UserId)
                throw new InvalidOperationException("Use the caseload transfer workflow to change the consumer's owner.");
            person.AgencyId = stored.AgencyId;
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
            AddInitialFormAttestations(
                context,
                actor,
                person,
                person.Forms.Where(form => form.Id == 0));
            context.People.Update(person);
            context.Entry(person).Property(candidate => candidate.Revision).OriginalValue = stored.Revision;
            // CreatedAtUtc has no public setter, so an edit-built Person can only ever carry the
            // CLR default here rather than the real value — excluding it from the update keeps
            // that default from overwriting the real column rather than relying on the caller to
            // have round-tripped it correctly.
            context.Entry(person).Property(candidate => candidate.CreatedAtUtc).IsModified = false;
            if (PersonLifecycleLedger.RecordChanged(context, actor, person, before, "Updated"))
                LocalAuditTrail.Record(context, actor, LocalAuditActions.PersonUpdated, "Person", person.Id);
            if ((stored.FirstName, stored.LastName, stored.Email) != (person.FirstName, person.LastName, person.Email))
                await SignaturePersistenceMutations.RevokeOpenForSignerAsync(context, person.Id, null, actor.Id, DateTime.UtcNow);
            await context.SaveChangesAsync();
            await signatureChangeTransaction.CommitAsync();
            return person;
        }

        private static void AddInitialFormAttestations(
            SatiContext context,
            User actor,
            Person person,
            IEnumerable<Form> forms)
        {
            if (person.EffectiveDate is not DateTime effectiveDate)
                return;

            foreach (var form in forms.Where(candidate =>
                         candidate.CompletedDate is not null && candidate.Attestations.Count == 0))
            {
                var completedOn = form.CompletedDate!.Value.Date;
                var cycle = FormAttestationRules.ResolveCycle(effectiveDate, form.DueDate)
                    ?? throw new PersonValidationException(new Dictionary<string, string[]>
                    {
                        ["forms"] = ["A completed form is not attached to a valid compliance cycle."]
                    });
                var decision = FormAttestationRules.Evaluate(
                    form.Type.ToString(), completedOn, cycle.CycleStart, DateTime.Today,
                    AttestationActorKind.CaseManager, [],
                    person.Forms.Select(candidate => new FormFact(
                        candidate.Id, person.Id, candidate.Type.ToString(),
                        candidate.DueDate, candidate.CompletedDate)).ToList());
                if (!decision.Accepted)
                {
                    throw new PersonValidationException(new Dictionary<string, string[]>
                    {
                        ["forms"] = [decision.DateError ?? string.Join(" ",
                            decision.UnmetPrerequisites.Select(item => item.Message))]
                    });
                }

                form.Attest(FormAttestation.Attested(
                    completedOn,
                    AttestationActorKind.CaseManager,
                    actor.Id,
                    DateTime.UtcNow,
                    prerequisiteStateJson: FormAttestationRules.NoPrerequisitesStateJson));
                LocalAuditTrail.Record(
                    context,
                    actor,
                    LocalAuditActions.FormAttested,
                    "Form",
                    metadataJson: JsonSerializer.Serialize(new
                    {
                        formType = form.Type.ToString(),
                        cycleStart = cycle.CycleStart.ToString("yyyy-MM-dd"),
                        completedOn = completedOn.ToString("yyyy-MM-dd"),
                        actorKind = AttestationActorKind.CaseManager.ToString(),
                        prerequisiteArtifactIds = Array.Empty<int>()
                    }));
            }
        }

        /// <summary>
        /// Moves a consumer to another case manager's caseload in local Production.
        ///
        /// <para>
        /// This repeats every restriction the API applies rather than assuming a server is in
        /// front of it, because in local Production there is not one: this class writes straight
        /// to SQL Server from the desktop. The authorization decision itself is not repeated —
        /// it is <see cref="CaseloadTransferRules"/>, the same function the API calls — but the
        /// loading of the facts it decides over happens here, from this database, and never from
        /// values a caller passed in.
        /// </para>
        /// </summary>
        public async Task<CaseloadOwnershipDto> TransferOwnershipAsync(
            int personId,
            int targetUserId,
            int expectedRevision)
        {
            var actor = CurrentActor();
            await using var context = _contextFactory.CreateDbContext();

            var person = await context.People.SingleOrDefaultAsync(candidate =>
                candidate.Id == personId && candidate.AgencyId == actor.AgencyId);
            if (person is null)
                throw new InvalidOperationException("This Person was not found in your agency.");

            var currentOwner = await LoadParticipantAsync(context, person.UserId);
            var target = await LoadParticipantAsync(context, targetUserId);
            if (currentOwner is null || target is null)
            {
                throw new PersonValidationException(new Dictionary<string, string[]>
                {
                    ["targetUserId"] = ["That consumer or case manager is not on your team."]
                });
            }

            var denial = CaseloadTransferRules.Evaluate(
                new AgencyActor(actor.Id, actor.AgencyId, actor.Permissions),
                currentOwner.Value,
                target.Value);
            if (denial is not CaseloadTransferDenial.None)
            {
                throw new PersonValidationException(new Dictionary<string, string[]>
                {
                    ["targetUserId"] = [CaseloadTransferRules.Describe(denial)]
                });
            }

            // Checked after authorization, so a caller who may not move this consumer learns
            // that and not whether their revision token happened to be current.
            if (person.Revision != expectedRevision)
            {
                throw new InvalidOperationException(
                    "This Person was changed after you opened it. Reload the Person before saving.");
            }

            var before = PersonLifecycleLedger.Capture(person);
            await PersonLifecycleLedger.EnsureBaselineAsync(context, person);
            var previousUserId = person.UserId;
            person.TransferTo(targetUserId);
            if (PersonLifecycleLedger.RecordChanged(context, actor, person, before, "Reassigned"))
            {
                LocalAuditTrail.Record(
                    context,
                    actor,
                    LocalAuditActions.PersonReassigned,
                    "Person",
                    personId,
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        previousUserId,
                        newUserId = targetUserId
                    }));
            }

            await context.SaveChangesAsync();
            return new CaseloadOwnershipDto(person.Id, person.UserId, person.Revision);
        }

        /// <summary>
        /// Archives or restores a consumer, in local Production.
        ///
        /// <para>
        /// Non-destructive: this changes visibility and work generation, never data.
        /// <see cref="PersonStatusRules"/> owns who may set which status; this method loads the
        /// facts it decides over (admin permission, caseload ownership) itself rather than
        /// trusting a caller-supplied claim, the same pattern <see cref="TransferOwnershipAsync"/>
        /// uses. See HANDOFF_CLIENT_DELETION_POLICY.md's archive semantics.
        /// </para>
        /// </summary>
        public async Task<PersonStatusDto> SetPersonStatusAsync(
            int personId,
            string status,
            string? note,
            int expectedRevision)
        {
            var actor = CurrentActor();
            await using var context = _contextFactory.CreateDbContext();

            var person = await context.People.SingleOrDefaultAsync(candidate =>
                candidate.Id == personId && candidate.AgencyId == actor.AgencyId);
            if (person is null)
                throw new InvalidOperationException("This Person was not found in your agency.");

            var actorIsAdmin = (actor.Permissions & UserPermissions.Administration) != 0;
            var refusal = PersonStatusRules.Describe(actorIsAdmin, person.UserId == actor.Id, status);
            if (refusal is not null)
            {
                throw new PersonValidationException(new Dictionary<string, string[]>
                {
                    ["status"] = [refusal]
                });
            }

            // Checked after authorization, so a caller who may not make this change learns that
            // and not whether their revision token happened to be current.
            if (person.Revision != expectedRevision)
            {
                throw new InvalidOperationException(
                    "This Person was changed after you opened it. Reload the Person before saving.");
            }

            var before = PersonLifecycleLedger.Capture(person);
            await PersonLifecycleLedger.EnsureBaselineAsync(context, person);
            var previousStatus = person.Status;
            var newStatus = Enum.Parse<PersonStatus>(status);
            person.Status = newStatus;
            person.StatusNote = note;
            person.StatusChangedAtUtc = DateTime.UtcNow;
            person.StatusChangedByUserId = actor.Id;

            // RecordChanged bumps Revision and writes history whenever status or its note
            // differ; the audit event is narrower and fires only on an actual status
            // transition, so a note-only edit is not misreported as an archive action.
            if (PersonLifecycleLedger.RecordChanged(context, actor, person, before, "StatusChanged") &&
                previousStatus != newStatus)
            {
                LocalAuditTrail.Record(
                    context,
                    actor,
                    newStatus == PersonStatus.Active
                        ? LocalAuditActions.ConsumerUnarchived
                        : LocalAuditActions.ConsumerArchived,
                    "Person",
                    personId,
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        previousStatus = previousStatus.ToString(),
                        newStatus = newStatus.ToString()
                    }));
            }

            await context.SaveChangesAsync();
            return new PersonStatusDto(person.Id, person.Status.ToString(), person.StatusNote, person.Revision);
        }

        /// <summary>
        /// Which of these Credible ids this agency already holds, in local Production.
        ///
        /// <para>
        /// Repeats the API's scoping rather than assuming a server enforces it, because here
        /// there is not one. The owner's name is disclosed only where
        /// <see cref="CaseloadTransferRules"/> says the actor could already see that caseload —
        /// the same predicate the route uses, not a second reading of it.
        /// </para>
        /// </summary>
        public async Task<CredibleMatchLookupResult> FindCredibleMatchesAsync(
            IReadOnlyList<string> credibleClientIds,
            IReadOnlyList<string>? maineCareIds = null,
            IReadOnlyList<PersonNameBirthDate>? nameBirthDates = null)
        {
            ArgumentNullException.ThrowIfNull(credibleClientIds);
            var actor = CurrentActor();
            if (!actor.HasCaseManagerPermissions)
                return CredibleMatchLookupResult.Empty;

            var ids = Clean(credibleClientIds);
            var mcIds = Clean(maineCareIds ?? []);
            var names = (nameBirthDates ?? []).Distinct().ToList();
            if (ids.Count == 0 && mcIds.Count == 0 && names.Count == 0)
                return CredibleMatchLookupResult.Empty;

            await using var context = _contextFactory.CreateDbContext();
            var agencyActor = new AgencyActor(actor.Id, actor.AgencyId, actor.Permissions);

            var credibleMatches = new List<CredibleClientOwnerRow>();
            if (ids.Count > 0)
            {
                credibleMatches = await (
                    from person in context.People.AsNoTracking()
                    join owner in context.Users.AsNoTracking() on person.UserId equals owner.Id
                    where person.AgencyId == actor.AgencyId &&
                          person.CredibleClientId != null &&
                          ids.Contains(person.CredibleClientId)
                    select new CredibleClientOwnerRow(
                        person.CredibleClientId!, owner.Id, owner.DisplayName,
                        owner.AgencyId, owner.Permissions, owner.SupervisorId)).ToListAsync();
            }

            var maineCareMatches = new List<MaineCareOwnerRow>();
            if (mcIds.Count > 0)
            {
                maineCareMatches = await (
                    from person in context.People.AsNoTracking()
                    join owner in context.Users.AsNoTracking() on person.UserId equals owner.Id
                    where person.AgencyId == actor.AgencyId &&
                          person.MaineCareId != null &&
                          mcIds.Contains(person.MaineCareId)
                    select new MaineCareOwnerRow(
                        person.MaineCareId!, owner.Id, owner.DisplayName,
                        owner.AgencyId, owner.Permissions, owner.SupervisorId)).ToListAsync();
            }

            // Name+DOB cannot be pushed to SQL as a normalized comparison, so it loads the
            // agency's identity columns and matches in memory. Agency scale is 300-400 consumers
            // (CREDIBLE_IMPORT_DESIGN.md), so this is cheap and only runs once per bulk dry run.
            var nameMatches = new List<NameBirthDateOwnerRow>();
            if (names.Count > 0)
            {
                nameMatches = await (
                    from person in context.People.AsNoTracking()
                    join owner in context.Users.AsNoTracking() on person.UserId equals owner.Id
                    where person.AgencyId == actor.AgencyId &&
                          person.LastName != null && person.FirstName != null
                    select new NameBirthDateOwnerRow(
                        person.LastName!, person.FirstName!, person.BirthDate,
                        owner.Id, owner.DisplayName,
                        owner.AgencyId, owner.Permissions, owner.SupervisorId)).ToListAsync();
            }

            bool CanDisclose(int ownerId, int ownerAgencyId, UserPermissions permissions, int? supervisorId) =>
                CaseloadTransferRules.CanReachOwnOrSupervisedCaseload(
                    agencyActor, new CaseloadParticipant(ownerId, ownerAgencyId, permissions, supervisorId));

            return new CredibleMatchLookupResult(
                credibleMatches
                    .Select(match => new CredibleClientMatchDto(
                        match.CredibleClientId!,
                        CanDisclose(match.OwnerId, match.AgencyId, match.Permissions, match.SupervisorId)
                            ? match.OwnerName : null))
                    .DistinctBy(match => match.CredibleClientId, StringComparer.Ordinal)
                    .ToList(),
                maineCareMatches
                    .Select(match => new MaineCareIdMatchDto(
                        match.MaineCareId!,
                        CanDisclose(match.OwnerId, match.AgencyId, match.Permissions, match.SupervisorId)
                            ? match.OwnerName : null))
                    .DistinctBy(match => match.MaineCareId, StringComparer.Ordinal)
                    .ToList(),
                nameMatches
                    .Where(match => names.Any(candidate =>
                        ProviderDirectoryRules.IsSameName(candidate.LastName, match.LastName) &&
                        ProviderDirectoryRules.IsSameName(candidate.FirstName, match.FirstName) &&
                        candidate.BirthDate.Date == match.BirthDate.Date))
                    .Select(match => new NameBirthDateMatchDto(
                        new PersonNameBirthDate(match.LastName!, match.FirstName!, match.BirthDate),
                        CanDisclose(match.OwnerId, match.AgencyId, match.Permissions, match.SupervisorId)
                            ? match.OwnerName : null))
                    .DistinctBy(match => (match.NameBirthDate.LastName.ToUpperInvariant(),
                        match.NameBirthDate.FirstName.ToUpperInvariant(), match.NameBirthDate.BirthDate.Date))
                    .ToList());
        }

        private static List<string> Clean(IReadOnlyList<string> values) =>
            values
                .Select(id => id?.Trim())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .Select(id => id!)
                .ToList();

        // EF projection targets for the three FindCredibleMatchesAsync tiers. Named record types
        // rather than anonymous ones so the query can be skipped entirely (empty list, no round
        // trip) when a tier has nothing to look up, without an anonymous-type mismatch between
        // branches.
        private sealed record CredibleClientOwnerRow(
            string CredibleClientId, int OwnerId, string? OwnerName,
            int AgencyId, UserPermissions Permissions, int? SupervisorId);

        private sealed record MaineCareOwnerRow(
            string MaineCareId, int OwnerId, string? OwnerName,
            int AgencyId, UserPermissions Permissions, int? SupervisorId);

        private sealed record NameBirthDateOwnerRow(
            string LastName, string FirstName, DateTime BirthDate, int OwnerId, string? OwnerName,
            int AgencyId, UserPermissions Permissions, int? SupervisorId);

        /// <summary>
        /// One user's caseload-authorization facts. Projected rather than loaded whole so a
        /// password hash and salt never enter memory for a question that does not need them.
        /// </summary>
        private static async Task<CaseloadParticipant?> LoadParticipantAsync(
            SatiContext context,
            int userId)
        {
            var rows = await context.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => new CaseloadParticipant(
                    user.Id, user.AgencyId, user.Permissions, user.SupervisorId))
                .ToListAsync();

            return rows.Count == 1 ? rows[0] : null;
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

        // Only a collision on the forms this call was inserting is a lost race worth
        // swallowing. Any other constraint failure is a real error and must surface.
        private static bool IsDuplicateFormViolation(SatiContext context) =>
            context.ChangeTracker
                .Entries<Form>()
                .Any(entry => entry.State == EntityState.Added);

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
                .Where(p => p.UserId == userId && p.Status == PersonStatus.Active)
                .Include(p => p.Notes)
                .Include(p => p.Forms)
                .OrderBy(p => p.LastName)
                .AsSplitQuery()
                .ToListAsync();

            // Generating missing cycle forms on load is the only thing keeping an
            // ongoing caseload supplied with compliance records; without it clients
            // silently run out once their pre-created cycles lapse. It was gated off
            // from 57af6fa until the unique index existed, because two concurrent
            // loads could both pass EnsureCurrentCycleForms' membership check and both
            // insert — which is exactly how every form ended up triplicated.
            // IX_Forms_PersonId_Type_DueDate now decides that race, and the catch
            // below turns losing it into a re-read rather than a crash.
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
                {
                    try
                    {
                        await context.SaveChangesAsync();
                    }
                    catch (DbUpdateException) when (IsDuplicateFormViolation(context))
                    {
                        // Another loader inserted the same cycle forms first.
                        // IX_Forms_PersonId_Type_DueDate is what makes that a lost
                        // race instead of a second copy — before it existed, both
                        // writers succeeded and every form ended up triplicated.
                        //
                        // Losing is not a failure: the rows this call wanted are in
                        // the database, just written by someone else. Discard the
                        // losing inserts and re-read so the caller gets the stored
                        // set rather than a crash on a benign collision.
                        foreach (var entry in context.ChangeTracker
                                     .Entries<Form>()
                                     .Where(entry => entry.State == EntityState.Added)
                                     .ToList())
                        {
                            entry.State = EntityState.Detached;
                        }

                        await using var reread = _contextFactory.CreateDbContext();
                        return await reread.People
                            .Where(p => p.UserId == userId && p.Status == PersonStatus.Active)
                            .Include(p => p.Notes)
                            .Include(p => p.Forms)
                            .OrderBy(p => p.LastName)
                            .AsSplitQuery()
                            .ToListAsync();
                    }
                }
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
                .Where(p => p.UserId == userId && p.Status == PersonStatus.Active)
                .OrderBy(p => p.LastName)
                .Select(p => new PersonSummary
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    Revision = p.Revision,
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
