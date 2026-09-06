using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

// The other suggested-follow-up tests drive the panel with a stub event service,
// so they prove the panel reacts but never that the real generator produces
// anything. It did not: for most of every cycle GenerateEvents is silent, which
// is why the row never appeared in the running app.
public sealed class SuggestedFollowUpRealServiceTests
{
    [Fact]
    public async Task NextFormSuggestionFillsTheWindowGapThatLeftTheRowBlank()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var person = await EffectiveTodayAsync(fixture, today);
        var settings = new Settings();
        var service = new UpcomingEventService();

        // The gap this fixes: every form is months out, so nothing is inside its
        // open window and the dashboard generator reports nothing at all.
        Assert.Empty(service.GenerateEvents([person], settings, today));

        var next = service.NextFormSuggestion(person, settings, today);

        Assert.NotNull(next);
        var earliestOutstanding = person.Forms
            .Where(form => !form.IsSatisfiedAsOf(today))
            .Min(form => form.DueDate.Date);
        Assert.Equal(earliestOutstanding, next!.Date);
        Assert.Equal(UpcomingEventKind.UpcomingForm, next.Kind);
        Assert.NotNull(next.FormType);
        Assert.NotNull(next.OpenDate);
        Assert.True(next.OpenDate <= next.Date);
        Assert.Null(next.OpenedDate);
    }

    [Fact]
    public async Task NotePanelShowsTheNextFormForAnOrdinaryClient()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var person = await EffectiveTodayAsync(fixture, today);

        var panel = fixture.NoteEntry();
        await panel.InitializeAsync();
        panel.SelectedPerson = person;

        // Before the fallback existed this was false for every ordinary client.
        Assert.True(panel.IsSuggestedFollowUpVisible);
        Assert.Contains("Review", panel.SuggestedFollowUpText);
        Assert.True(panel.AcceptSuggestedFollowUpCommand.CanExecute(null));
        Assert.StartsWith("UPCOMING:", panel.ClientWorkStatusText);

        panel.AcceptSuggestedFollowUpCommand.Execute(null);

        Assert.StartsWith("Follow-up: ", panel.Narrative);
        Assert.False(panel.AcceptSuggestedFollowUpCommand.CanExecute(null));
    }

    [Fact]
    public async Task ASatisfiedFormIsNeverSuggested()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var person = await EffectiveTodayAsync(fixture, today);
        var settings = new Settings();
        var service = new UpcomingEventService();

        var first = service.NextFormSuggestion(person, settings, today);
        Assert.NotNull(first);

        foreach (var form in person.Forms.Where(f => f.DueDate.Date == first!.Date))
        {
            form.Attest(FormAttestation.Attested(
                today,
                AttestationActorKind.System,
                actorUserId: null,
                recordedAtUtc: DateTime.UtcNow,
                reason: "test setup"));
        }

        var second = service.NextFormSuggestion(person, settings, today);

        Assert.NotNull(second);
        Assert.True(second!.Date > first!.Date);
    }

    // A client whose coverage starts today: the ordinary case, and the one where
    // the open/late window reports nothing for months.
    private static async Task<Person> EffectiveTodayAsync(NoteEntryFixture fixture, DateTime today)
    {
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var stored = await db.People.Include(p => p.Forms)
                .SingleAsync(p => p.Id == fixture.PersonOneId);
            stored.EffectiveDate = today;
            stored.Forms = Person.CreatePerson(
                stored.UserId, "Journal", "Person", string.Empty,
                new DateTime(1990, 1, 1), today, WaiverType.Section21, new Settings()).Forms;
            await db.SaveChangesAsync();
        }

        await using var read = fixture.Factory.CreateDbContext();
        return await read.People.AsNoTracking()
            .Include(p => p.Forms)
            .Include(p => p.Notes)
            .SingleAsync(p => p.Id == fixture.PersonOneId);
    }
}
