using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Helpers;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Compliance is the completion date. There is no second field to disagree with it.
///
/// Before AddDerivedFormCompliance, Form carried a stored IsCompliant column beside
/// CompletedDate. Every screen read the flag; BillingComplianceGate read only the
/// date. 147 rows in SatiProduction held the flag set with no date, so they rendered
/// complete and blocked billing at the same time — the same symptom as the duplicate
/// rows, from a completely different cause, and equally invisible to the person
/// looking at the screen.
///
/// These tests pin the three things that keep it from coming back: the state cannot be
/// constructed, the generator always supplies a date, and the readers that depend on
/// today ask the same question the gate asks.
/// </summary>
public sealed class FormComplianceIsDerivedTests
{
    private static readonly DateTime Today = new(2026, 9, 1);
    private static readonly DateTime Due = new(2026, 8, 28);

    // ------------------------------------------------------------------
    // The state is unconstructible.
    // ------------------------------------------------------------------

    [Fact]
    public void ComplianceIsNothingButTheCompletionDate()
    {
        var form = new Form(FormType.Q1R, Due);
        Assert.False(form.IsCompliant);
        Assert.Null(form.CompletedDate);

        form.MarkComplete(Due);
        Assert.True(form.IsCompliant);
        Assert.Equal(Due, form.CompletedDate);

        form.Reset();
        Assert.False(form.IsCompliant);
        Assert.Null(form.CompletedDate);
    }

    [Fact]
    public void NothingCanSetComplianceWithoutSettingADate()
    {
        // The structural claim, not an example of it: there is no writable path to
        // IsCompliant, and CompletedDate can only be reached through the constructor
        // or the two named transitions. A future refactor that reintroduces a setter
        // fails here rather than in production six months later.
        var isCompliant = typeof(Form).GetProperty(nameof(Form.IsCompliant));
        Assert.NotNull(isCompliant);
        Assert.False(isCompliant!.CanWrite);

        var completedDate = typeof(Form).GetProperty(nameof(Form.CompletedDate));
        Assert.NotNull(completedDate);
        Assert.Null(completedDate!.GetSetMethod(nonPublic: false));

        Assert.DoesNotContain(
            typeof(Form).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType == typeof(bool));
    }

    [Fact]
    public async Task ComplianceIsNotStoredAndSurvivesARoundTrip()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();

        int id;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var form = new Form(FormType.Q1R, Due, Due) { PersonId = fixture.PersonOneId };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            id = form.Id;
        }

        await using var verify = fixture.Factory.CreateDbContext();
        var stored = await verify.Forms.AsNoTracking().SingleAsync(form => form.Id == id);
        Assert.Equal(Due, stored.CompletedDate);
        Assert.True(stored.IsCompliant);

        Assert.DoesNotContain(
            verify.Model.FindEntityType(typeof(Form))!.GetProperties(),
            property => property.Name == nameof(Form.IsCompliant));
    }

    // ------------------------------------------------------------------
    // The gate and the screens answer the same question.
    // ------------------------------------------------------------------

    [Theory]
    // completedDate, expected: does every timing-sensitive reader call this satisfied?
    [InlineData(null, false)]              // outstanding
    [InlineData("2026-08-28", true)]       // completed on the due date
    [InlineData("2026-08-30", true)]       // completed late, but before today
    [InlineData("2026-09-01", true)]       // completed today
    [InlineData("2026-09-15", false)]      // recorded for a date that has not arrived
    public void EveryTimingSensitiveReaderAgreesWithTheBillingGate(string? completed, bool satisfied)
    {
        var completedDate = completed is null ? (DateTime?)null : DateTime.Parse(completed);
        var form = new Form(FormType.Q1R, Due, completedDate) { PersonId = 1 };

        var gatePasses = BillingComplianceGate.Evaluate(
            new DateTime(2026, 5, 30),
            [new ComplianceFormSnapshot(form.Type.ToString(), form.DueDate, form.CompletedDate)],
            Today).Passed;

        // The gate is the authority. Everything below must reach the same verdict for
        // this form, or a screen is contradicting a billing decision.
        Assert.Equal(satisfied, gatePasses);
        Assert.Equal(satisfied, form.IsSatisfiedAsOf(Today));
        Assert.Equal(satisfied, FormCellStatusCalculator.Compute(form, Today) == FormCellStatus.Complete);
        Assert.Equal(!satisfied, new Sati.ViewModels.FormTaskRow(
            form, "Test Client", "Q1 Review", Due.AddDays(-30), Today).IsOverdue);
    }

    [Fact]
    public void ARecordedButNotYetEffectiveCompletionIsStillRecorded()
    {
        // IsCompliant answers "is a completion recorded", which stays true for a date
        // that has not arrived. That is deliberate and is why IsSatisfiedAsOf exists
        // separately — the distinction is real, and collapsing it in either direction
        // is what produced the original disagreement.
        var form = new Form(FormType.PCP, Due, new DateTime(2026, 9, 15));

        Assert.True(form.IsCompliant);
        Assert.False(form.IsSatisfiedAsOf(Today));
        Assert.True(form.IsSatisfiedAsOf(new DateTime(2026, 9, 15)));
    }

    // ------------------------------------------------------------------
    // Generation always supplies a date, or leaves the form outstanding.
    // ------------------------------------------------------------------

    [Fact]
    public void GeneratedFormsAreNeverCompliantWithoutADate()
    {
        var forms = Person.GenerateFormList(new DateTime(2026, 5, 30), new Settings());

        Assert.NotEmpty(forms);
        Assert.All(forms, form =>
            Assert.Equal(form.IsCompliant, form.CompletedDate.HasValue));
    }

    [Fact]
    public void CycleGenerationDatesAnnualDocumentsFromTheCycleStartAndLeavesReviewsOpen()
    {
        var cycleStart = new DateTime(2026, 5, 30);
        var person = PersonWithNoForms(cycleStart);

        Assert.True(person.EnsureCurrentCycleForms(Today, new Settings()));

        // This is the call site that produced the 147 rows. Every annual document it
        // creates for a cycle already under way now carries the date that put it in
        // force; nothing it creates is compliant without one.
        Assert.All(person.Forms, form =>
            Assert.Equal(form.IsCompliant, form.CompletedDate.HasValue));

        var pcp = person.GetCurrentCycleForm(FormType.PCP, Today)!;
        Assert.Equal(cycleStart, pcp.CompletedDate);

        // A review is an attestation that work happened. No date can be inferred for
        // work nobody recorded, so it stays open.
        Assert.Null(person.GetCurrentCycleForm(FormType.Q1R, Today)!.CompletedDate);

        // Next cycle assumes nothing: it has not started, so nothing is in force yet,
        // and a cycle that rolls over with these still open flags the missed renewal.
        var nextCyclePcp = person.Forms
            .Where(form => form.Type == FormType.PCP)
            .OrderByDescending(form => form.DueDate)
            .First();
        Assert.Null(nextCyclePcp.CompletedDate);
    }

    [Fact]
    public void ABackdatedAdmissionGetsFormsForEveryYearInBetween()
    {
        // The gap: generation used to cover the first cycle (at creation) and the
        // current and next cycles (on load), and nothing else. A client admitted in
        // 2023 therefore had no forms at all for 2024 or 2025 — and a form that does
        // not exist cannot fail the gate, so those years silently carried no
        // compliance requirements.
        var person = PersonWithNoForms(new DateTime(2023, 5, 30));

        Assert.True(person.EnsureCurrentCycleForms(Today, new Settings()));

        var cycleStarts = new[]
        {
            new DateTime(2023, 5, 30), new DateTime(2024, 5, 30),
            new DateTime(2025, 5, 30), new DateTime(2026, 5, 30),
            new DateTime(2027, 5, 30)
        };

        foreach (var start in cycleStarts)
        {
            var pcp = person.Forms.SingleOrDefault(form =>
                form.Type == FormType.PCP &&
                form.DueDate > start &&
                form.DueDate <= start.AddYears(1));
            Assert.NotNull(pcp);
        }
    }

    [Fact]
    public void ClosedCyclesAreGeneratedOutstandingRatherThanAssumedSatisfied()
    {
        // Sati has no record of whether a closed year's documents were renewed, and a
        // later cycle beginning proves nothing — cycles turn over on the anniversary,
        // not because anything was signed. Marking them satisfied would assert
        // compliance nobody attested across every historical cycle at once.
        var person = PersonWithNoForms(new DateTime(2023, 5, 30));
        person.EnsureCurrentCycleForms(Today, new Settings());

        var closedCyclePcp = person.Forms.Single(form =>
            form.Type == FormType.PCP && form.DueDate == new DateTime(2024, 5, 30));
        Assert.Null(closedCyclePcp.CompletedDate);

        // Only the cycle we are in now carries the in-force assumption.
        var currentPcp = person.GetCurrentCycleForm(FormType.PCP, Today)!;
        Assert.Equal(new DateTime(2026, 5, 30), currentPcp.CompletedDate);
    }

    [Fact]
    public void AnImplausibleEffectiveDateStopsAtTheOldestEndAndStillCoversTodayAndNext()
    {
        // A mistyped effective date decades back would otherwise generate hundreds of
        // forms per client. What is dropped is the oldest end, so the cycles that can
        // actually be worked on are always present.
        var person = PersonWithNoForms(new DateTime(1925, 5, 30));

        person.EnsureCurrentCycleForms(Today, new Settings());

        Assert.NotNull(person.GetCurrentCycleForm(FormType.PCP, Today));
        Assert.Contains(person.Forms, form =>
            form.Type == FormType.PCP && form.DueDate == new DateTime(2028, 5, 30));
        Assert.DoesNotContain(person.Forms, form => form.DueDate.Year < 2001);
    }

    [Fact]
    public void CycleGenerationIsIdempotentAndAddsNoDuplicates()
    {
        var person = PersonWithNoForms(new DateTime(2026, 5, 30));

        Assert.True(person.EnsureCurrentCycleForms(Today, new Settings()));
        var afterFirst = person.Forms.Count;

        Assert.False(person.EnsureCurrentCycleForms(Today, new Settings()));
        Assert.Equal(afterFirst, person.Forms.Count);
        Assert.Empty(FormDuplicateRepair.Plan(person.Forms).Groups);
    }

    [Fact]
    public async Task ConcurrentCycleGenerationConvergesOnOneSetInsteadOfThree()
    {
        // The original defect, reproduced against a real database: two loaders each
        // read before either wrote, so both believed the forms were missing. Before
        // IX_Forms_PersonId_Type_DueDate both writes succeeded and the caseload ended
        // up with duplicate forms. Now the second one loses and the row count is
        // whatever the winner wrote.
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var settings = new Settings();

        // Start from a person with a cycle and no forms, so both loaders genuinely
        // have the same full set to insert. That is the state the caseload was in
        // when the original triplication happened.
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            var person = await setup.People.Include(p => p.Forms)
                .SingleAsync(p => p.Id == fixture.PersonOneId);
            setup.Forms.RemoveRange(person.Forms);
            person.EffectiveDate = new DateTime(2026, 5, 30);
            await setup.SaveChangesAsync();
        }

        await using var first = fixture.Factory.CreateDbContext();
        await using var second = fixture.Factory.CreateDbContext();

        var personOne = await first.People.Include(p => p.Forms)
            .SingleAsync(p => p.Id == fixture.PersonOneId);
        var personTwo = await second.People.Include(p => p.Forms)
            .SingleAsync(p => p.Id == fixture.PersonOneId);

        personOne.EnsureCurrentCycleForms(Today, settings);
        personTwo.EnsureCurrentCycleForms(Today, settings);

        await first.SaveChangesAsync();
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => second.SaveChangesAsync());

        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Empty(FormDuplicateRepair.Plan(await verify.Forms.AsNoTracking().ToListAsync()).Groups);
    }

    private static Person PersonWithNoForms(DateTime effective)
    {
        var person = Person.CreatePerson(
            1, "Test", "Client", "bio", new DateTime(1990, 1, 1), null,
            WaiverType.Section21, new Settings());
        person.EffectiveDate = effective;
        return person;
    }
}
