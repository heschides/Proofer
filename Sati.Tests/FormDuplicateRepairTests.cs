using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Covers the duplicate-compliance-form defect reported 2026-09-01: a Q1R that reads
/// complete on every screen while the billing gate keeps blocking on it, because the
/// person holds three Q1R rows at the same due date and the two readers reach
/// different ones.
///
/// The DB-level tests drop IX_Forms_PersonId_Type_DueDate before seeding, because the
/// index is exactly what makes the bad shape unreachable now — which is the point,
/// and is asserted directly in TheUniqueIndexRefusesASecondCopy. Dropping it
/// reproduces the state a real database was already in when the index shipped.
/// </summary>
public sealed class FormDuplicateRepairTests
{
    private static readonly DateTime DueDate = new(2026, 8, 28);
    private static readonly DateTime Completed = new(2026, 8, 28);

    // ------------------------------------------------------------------
    // The reported bug, as a single assertion.
    // ------------------------------------------------------------------

    [Fact]
    public void ACompletedFormStillBlocksBillingWhenDuplicatesHideBehindIt()
    {
        var person = PersonWithTripledQ1R();

        // What every IsCompliant-based reader sees: GetCurrentCycleForm resolves the
        // due-date tie to one row, and that row is complete.
        Assert.True(person.GetCurrentCycleForm(FormType.Q1R, Today)!.IsCompliant);

        // What the gate sees: every row in Person.Forms, including the two nobody can
        // reach from any screen.
        var gate = person.EvaluateComplianceGate(Today);

        Assert.False(gate.Passed);
        Assert.Contains(gate.Reasons, reason => reason.Contains("Q1 Review"));
    }

    [Fact]
    public void RepairingTheDuplicatesClearsTheBlockWithoutTouchingTheCompletionDate()
    {
        var person = PersonWithTripledQ1R();

        ApplyPlanInMemory(person);

        var gate = person.EvaluateComplianceGate(Today);
        Assert.True(gate.Passed);
        Assert.Empty(gate.Reasons);

        var survivor = Assert.Single(person.Forms.Where(form => form.Type == FormType.Q1R));
        Assert.Equal(Completed, survivor.CompletedDate);
        Assert.True(survivor.IsCompliant);
    }

    // ------------------------------------------------------------------
    // Classification — the pure rules.
    // ------------------------------------------------------------------

    [Fact]
    public void OneCopyHoldingTheCompletionIsNotAConflict()
    {
        // The ordinary shape: one copy was edited, the rest are untouched generation
        // defaults. The union holds exactly one completion fact, so there is nothing
        // for a human to choose.
        var plan = FormDuplicateRepair.Plan([
            Form(1, FormType.Q1R, DueDate),
            Form(2, FormType.Q1R, DueDate),
            Form(3, FormType.Q1R, DueDate, Completed)
        ]);

        var group = Assert.Single(plan.Groups);
        Assert.False(group.IsConflicted);
        Assert.Equal(2, group.SurplusRows);
        Assert.False(plan.LeavesDuplicates);
    }

    [Fact]
    public void TwoDifferentCompletionDatesAreAConflictAndAreLeftAlone()
    {
        // Merging would have to pick one, and CompletedDate is date-keyed into
        // BillingComplianceGate.IsBillingWindowBlocked, so the choice decides whether
        // service dates in between were billable.
        var plan = FormDuplicateRepair.Plan([
            Form(1, FormType.Q1R, DueDate, new DateTime(2026, 8, 28)),
            Form(2, FormType.Q1R, DueDate, new DateTime(2026, 9, 15)),
            Form(3, FormType.Q1R, DueDate)
        ]);

        var group = Assert.Single(plan.Groups);
        Assert.True(group.IsConflicted);
        Assert.Empty(plan.Mergeable);
        Assert.True(plan.LeavesDuplicates);
        Assert.False(plan.HasWork);
    }

    [Fact]
    public void FormsOfTheSameTypeOnDifferentDueDatesAreNotDuplicates()
    {
        // Consecutive cycles produce a Q1R per year. Those are distinct records.
        var plan = FormDuplicateRepair.Plan([
            Form(1, FormType.Q1R, new DateTime(2026, 8, 28)),
            Form(2, FormType.Q1R, new DateTime(2027, 8, 28))
        ]);

        Assert.Empty(plan.Groups);
    }

    // ------------------------------------------------------------------
    // Applying the repair against a database.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ApplyMergesTheCompletionOntoOneSurvivorAndAuditsEveryRemoval()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await DropUniqueFormIndexAsync(fixture);
        var ids = await SeedTripledQ1RAsync(fixture);

        FormDuplicateRepair.RepairResult result;
        await using (var db = fixture.Factory.CreateDbContext())
            result = await FormDuplicateRepair.ApplyAsync(db);

        Assert.Equal(1, result.GroupsMerged);
        Assert.Equal(2, result.RowsRemoved);
        Assert.Equal(0, result.GroupsLeftConflicted);

        await using var verify = fixture.Factory.CreateDbContext();
        var survivor = Assert.Single(await verify.Forms.AsNoTracking()
            .Where(form => form.Type == FormType.Q1R)
            .ToListAsync());
        Assert.Equal(Completed, survivor.CompletedDate);
        Assert.True(survivor.IsCompliant);

        // The surviving row is the one that already carried the completion, so no
        // state was manufactured for it.
        Assert.Equal(ids.completed, survivor.Id);

        var audits = await verify.AuditEvents.AsNoTracking()
            .Where(entry => entry.Action == LocalAuditActions.FormDuplicateRemoved)
            .ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, entry => Assert.Equal(FormDuplicateRepair.SystemActorUserId, entry.ActorUserId));
        Assert.Contains(audits, entry => entry.ResourceId == ids.blankOne.ToString());
        Assert.Contains(audits, entry => entry.ResourceId == ids.blankTwo.ToString());
    }

    [Fact]
    public async Task ApplyLeavesAConflictedGroupIntactAndReportsIt()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await DropUniqueFormIndexAsync(fixture);

        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Forms.AddRange(
                Form(0, FormType.Q1R, DueDate, new DateTime(2026, 8, 28), fixture.PersonOneId),
                Form(0, FormType.Q1R, DueDate, new DateTime(2026, 9, 15), fixture.PersonOneId));
            await db.SaveChangesAsync();
        }

        FormDuplicateRepair.RepairResult result;
        await using (var db = fixture.Factory.CreateDbContext())
            result = await FormDuplicateRepair.ApplyAsync(db);

        Assert.Equal(0, result.GroupsMerged);
        Assert.Equal(0, result.RowsRemoved);
        Assert.Equal(1, result.GroupsLeftConflicted);
        Assert.Single(result.Conflicts);

        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Equal(2, await verify.Forms.CountAsync(form => form.Type == FormType.Q1R));
        Assert.Empty(await verify.AuditEvents
            .Where(entry => entry.Action == LocalAuditActions.FormDuplicateRemoved)
            .ToListAsync());
    }

    [Fact]
    public async Task ApplyIsIdempotentAndWritesNothingOnCleanData()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await DropUniqueFormIndexAsync(fixture);
        await SeedTripledQ1RAsync(fixture);

        await using (var db = fixture.Factory.CreateDbContext())
            await FormDuplicateRepair.ApplyAsync(db);

        FormDuplicateRepair.RepairResult second;
        await using (var db = fixture.Factory.CreateDbContext())
            second = await FormDuplicateRepair.ApplyAsync(db);

        Assert.Equal(0, second.GroupsMerged);
        Assert.Equal(0, second.RowsRemoved);

        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Equal(2, await verify.AuditEvents
            .CountAsync(entry => entry.Action == LocalAuditActions.FormDuplicateRemoved));
    }

    [Fact]
    public async Task ApplyKeepsTheEarliestOpenedDateAcrossTheCopies()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await DropUniqueFormIndexAsync(fixture);

        await using (var db = fixture.Factory.CreateDbContext())
        {
            var completed = Form(0, FormType.Q1R, DueDate, Completed, fixture.PersonOneId);
            var opened = Form(0, FormType.Q1R, DueDate, null, fixture.PersonOneId);
            opened.OpenedDate = new DateTime(2026, 7, 1);
            db.Forms.AddRange(completed, opened);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.Factory.CreateDbContext())
            await FormDuplicateRepair.ApplyAsync(db);

        await using var verify = fixture.Factory.CreateDbContext();
        var survivor = Assert.Single(await verify.Forms.AsNoTracking()
            .Where(form => form.Type == FormType.Q1R).ToListAsync());
        Assert.Equal(new DateTime(2026, 7, 1), survivor.OpenedDate);
        Assert.Equal(Completed, survivor.CompletedDate);
    }

    // ------------------------------------------------------------------
    // The constraint that makes recurrence impossible.
    // ------------------------------------------------------------------

    [Fact]
    public async Task TheUniqueIndexRefusesASecondCopy()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();

        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Forms.Add(Form(0, FormType.Q1R, DueDate, null, fixture.PersonOneId));
            await db.SaveChangesAsync();
        }

        await using var second = fixture.Factory.CreateDbContext();
        second.Forms.Add(Form(0, FormType.Q1R, DueDate, null, fixture.PersonOneId));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task TheUniqueIndexCanBeCreatedOnceTheRepairHasRun()
    {
        // The whole startup sequence in one test: duplicates present, index absent,
        // repair, then the index binds. This is the ordering LocalDatabaseUpdater
        // depends on and the migration's guard enforces.
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await DropUniqueFormIndexAsync(fixture);
        await SeedTripledQ1RAsync(fixture);

        await using (var db = fixture.Factory.CreateDbContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => CreateUniqueFormIndexAsync(db));
            await FormDuplicateRepair.ApplyAsync(db);
        }

        await using var after = fixture.Factory.CreateDbContext();
        await CreateUniqueFormIndexAsync(after);
    }

    // ------------------------------------------------------------------
    // The second duplication path: adding a waiver to an existing client.
    // ------------------------------------------------------------------

    [Fact]
    public void AddingFormsToAClientThatAlreadyHasThemDoesNotCreateASecondSet()
    {
        var person = Person.CreatePerson(
            1, "Test", "Client", "bio", new DateTime(1990, 1, 1),
            new DateTime(2026, 5, 30), WaiverType.Section21, new Settings());
        var before = person.Forms.Count;
        Assert.True(before > 0);

        // What the client editor does when a waiver is added: generate the set again
        // for the same effective date. Every generated form carries Id == 0, so
        // assigning them over Forms would have inserted a full second set.
        var added = person.AddMissingForms(
            Person.GenerateFormList(new DateTime(2026, 5, 30), new Settings()));

        Assert.Equal(0, added);
        Assert.Equal(before, person.Forms.Count);
        Assert.Empty(FormDuplicateRepair.Plan(person.Forms).Groups);
    }

    [Fact]
    public void AddingFormsKeepsAnExistingCompletionRatherThanTheGeneratedBlank()
    {
        var person = Person.CreatePerson(
            1, "Test", "Client", "bio", new DateTime(1990, 1, 1), null,
            WaiverType.Section21, new Settings());
        person.EffectiveDate = new DateTime(2026, 5, 30);
        person.Forms.Add(Form(1, FormType.Q1R, DueDate, Completed));

        person.AddMissingForms(
            Person.GenerateFormList(new DateTime(2026, 5, 30), new Settings()));

        var q1r = Assert.Single(person.Forms.Where(form => form.Type == FormType.Q1R));
        Assert.Equal(Completed, q1r.CompletedDate);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static DateTime Today => new(2026, 9, 1);

    private static Form Form(
        int id, FormType type, DateTime dueDate, DateTime? completed = null, int personId = 1)
    {
        var form = new Form(type, dueDate, completed.HasValue) { PersonId = personId };
        if (id > 0)
            form.Id = id;
        if (completed.HasValue)
            form.MarkComplete(completed.Value);
        return form;
    }

    private static Person PersonWithTripledQ1R()
    {
        // effective: null so CreatePerson generates no forms — this test needs only
        // the three Q1R rows, not a whole cycle. The cycle date is set after, because
        // GetCurrentCycleForm and EvaluateComplianceGate both derive their window
        // from it.
        var person = Person.CreatePerson(
            1, "Test", "Client", "bio", new DateTime(1990, 1, 1), null,
            WaiverType.Section21, new Settings());
        person.EffectiveDate = new DateTime(2026, 5, 30);

        // The completed copy is added first deliberately. GetCurrentCycleForm breaks
        // the due-date tie with OrderByDescending(DueDate).FirstOrDefault(), and that
        // sort is stable, so the winner is simply whichever copy came first — from
        // the database, whichever EF happened to materialise. On the reported record
        // that was the completed one, which is why the checkbox read complete while
        // the gate kept blocking. Nothing chooses it; that is the defect.
        person.Forms.Add(Form(3, FormType.Q1R, DueDate, Completed));
        person.Forms.Add(Form(1, FormType.Q1R, DueDate));
        person.Forms.Add(Form(2, FormType.Q1R, DueDate));
        return person;
    }

    // Mirrors what FormDuplicateRepair.ApplyAsync does to the entity graph, for the
    // tests that work against an unattached Person.
    private static void ApplyPlanInMemory(Person person)
    {
        foreach (var group in FormDuplicateRepair.Plan(person.Forms).Mergeable)
        {
            var copies = person.Forms.Where(form => group.FormIds.Contains(form.Id)).ToList();
            var survivor = copies
                .OrderByDescending(copy => copy.CompletedDate.HasValue)
                .ThenByDescending(copy => copy.IsCompliant)
                .ThenBy(copy => copy.Id)
                .First();
            foreach (var duplicate in copies.Where(copy => copy.Id != survivor.Id))
                person.Forms.Remove(duplicate);
        }
    }

    private static async Task<(int completed, int blankOne, int blankTwo)> SeedTripledQ1RAsync(
        NoteEntryFixture fixture)
    {
        await using var db = fixture.Factory.CreateDbContext();
        var blankOne = Form(0, FormType.Q1R, DueDate, null, fixture.PersonOneId);
        var blankTwo = Form(0, FormType.Q1R, DueDate, null, fixture.PersonOneId);
        var completed = Form(0, FormType.Q1R, DueDate, Completed, fixture.PersonOneId);
        db.Forms.AddRange(blankOne, blankTwo, completed);
        await db.SaveChangesAsync();
        return (completed.Id, blankOne.Id, blankTwo.Id);
    }

    private static async Task DropUniqueFormIndexAsync(NoteEntryFixture fixture)
    {
        await using var db = fixture.Factory.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(
            "DROP INDEX IF EXISTS \"IX_Forms_PersonId_Type_DueDate\";");
    }

    private static Task CreateUniqueFormIndexAsync(SatiContext db) =>
        db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX \"IX_Forms_PersonId_Type_DueDate\" " +
            "ON \"Forms\" (\"PersonId\", \"Type\", \"DueDate\");");
}
