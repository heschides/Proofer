using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The note panel copies a record's fields in when it loads rather than binding
/// through to a live instance, so a note changed by a supervisor or another
/// session goes on being displayed as it was. Concurrency is still caught
/// authoritatively on save; this is about finding out <em>before</em> writing a
/// narrative against a copy that is already out of date, rather than after.
/// </summary>
public sealed class StaleNoteTests
{
    private static async Task<Note> SaveNoteAsync(NoteEntryFixture fixture, Person person, string narrative)
    {
        var notes = fixture.NotesFromAnotherSession();
        var note = Note.Create(narrative, new DateTime(2026, 8, 20), NoteStatus.Pending, 15,
            person.Id, null, NoteType.Contact);
        await notes.AddNoteAsync(note);
        return (await notes.GetAllByPersonAsync(person.Id)).Single(candidate => candidate.Id == note.Id);
    }

    private static async Task<NoteEntryViewModel> PanelShowingAsync(
        NoteEntryFixture fixture, Person person, Note note, bool? discardAnswer = null)
    {
        var panel = fixture.NoteEntry(discardAnswer: discardAnswer);
        panel.SetPeople([person]);
        panel.EnterViewMode(note);
        return await Task.FromResult(panel);
    }

    // -------------------------------------------------------------------------
    // The check itself
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnUnchangedNoteRaisesNothing()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        var saved = await SaveNoteAsync(fixture, person, "As written.");
        var panel = await PanelShowingAsync(fixture, person, saved);

        await panel.VerifyLoadedNoteIsCurrentAsync();

        Assert.False(panel.HasStaleNoteMessage);
        Assert.Null(panel.StaleNoteMessage);
        Assert.Equal("As written.", panel.Narrative);
    }

    [Fact]
    public async Task ANoteChangedElsewhereIsAnnouncedAndThePanelShowsTheCurrentVersion()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        var saved = await SaveNoteAsync(fixture, person, "The original narrative.");
        var panel = await PanelShowingAsync(fixture, person, saved);

        // Someone else edits the same note while it sits on screen.
        var elsewhere = fixture.NotesFromAnotherSession();
        var theirCopy = (await elsewhere.GetAllByPersonAsync(person.Id))
            .Single(candidate => candidate.Id == saved.Id);
        theirCopy.Narrative = "Corrected by a supervisor.";
        await elsewhere.UpdateNoteAsync(theirCopy);

        await panel.VerifyLoadedNoteIsCurrentAsync();

        Assert.True(panel.HasStaleNoteMessage);
        Assert.Contains("changed after you opened it", panel.StaleNoteMessage);
        Assert.Contains("narrative", panel.StaleNoteMessage);

        // Nothing had been typed, so showing the current version costs nothing and
        // the edit starts from the record as it now stands.
        Assert.Equal("Corrected by a supervisor.", panel.Narrative);
        Assert.False(panel.HasUnsavedChanges);
    }

    [Fact]
    public async Task UnsavedTypingIsNeverReplacedByTheServersCopy()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        var saved = await SaveNoteAsync(fixture, person, "The original narrative.");
        var panel = await PanelShowingAsync(fixture, person, saved);
        panel.ToggleLockCommand.Execute(null);
        panel.Narrative = "What the case manager is part-way through writing.";

        var elsewhere = fixture.NotesFromAnotherSession();
        var theirCopy = (await elsewhere.GetAllByPersonAsync(person.Id))
            .Single(candidate => candidate.Id == saved.Id);
        theirCopy.Narrative = "Corrected by a supervisor.";
        await elsewhere.UpdateNoteAsync(theirCopy);

        await panel.VerifyLoadedNoteIsCurrentAsync();

        Assert.True(panel.HasStaleNoteMessage);
        Assert.Contains("were not replaced", panel.StaleNoteMessage);
        Assert.Equal("What the case manager is part-way through writing.", panel.Narrative);
        Assert.True(panel.HasUnsavedChanges);
    }

    [Fact]
    public async Task ANoteDeletedElsewhereSaysSoRatherThanFailingQuietly()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        var saved = await SaveNoteAsync(fixture, person, "About to disappear.");
        var panel = await PanelShowingAsync(fixture, person, saved);

        var elsewhere = fixture.NotesFromAnotherSession();
        var theirCopy = (await elsewhere.GetAllByPersonAsync(person.Id))
            .Single(candidate => candidate.Id == saved.Id);
        await elsewhere.DeleteNoteAsync(theirCopy);

        await panel.VerifyLoadedNoteIsCurrentAsync();

        Assert.True(panel.HasStaleNoteMessage);
        Assert.Contains("no longer on the server", panel.StaleNoteMessage);
    }

    /// <summary>
    /// A background check that cannot reach the server must not interrupt the edit,
    /// and must not put anything about the note or the failure into the message the
    /// case manager reads.
    /// </summary>
    [Fact]
    public async Task AFailedCheckSaysSoWithoutLeakingTheFailure()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        var saved = await SaveNoteAsync(fixture, person, "Still readable.");
        var panel = fixture.NoteEntry(notes: new UnreachableNoteService(
            fixture.NotesFromAnotherSession(),
            new InvalidOperationException("host sati-demo.example unreachable")));
        panel.SetPeople([person]);
        panel.EnterViewMode(saved);

        await panel.VerifyLoadedNoteIsCurrentAsync();

        Assert.True(panel.HasStaleNoteMessage);
        Assert.Contains("could not check", panel.StaleNoteMessage);
        Assert.Contains("checked again when you save", panel.StaleNoteMessage);
        Assert.DoesNotContain("unreachable", panel.StaleNoteMessage);
        Assert.DoesNotContain("sati-demo", panel.StaleNoteMessage);

        // The note stays on screen and editable — a check that could not run is not
        // a reason to take the record away.
        Assert.Equal("Still readable.", panel.Narrative);
    }

    // -------------------------------------------------------------------------
    // Wiring
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnlockingRunsTheCheck()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        var saved = await SaveNoteAsync(fixture, person, "The original narrative.");
        var panel = await PanelShowingAsync(fixture, person, saved);

        var elsewhere = fixture.NotesFromAnotherSession();
        var theirCopy = (await elsewhere.GetAllByPersonAsync(person.Id))
            .Single(candidate => candidate.Id == saved.Id);
        theirCopy.Narrative = "Corrected by a supervisor.";
        await elsewhere.UpdateNoteAsync(theirCopy);

        // The unlock itself is what triggers the read, and it does not wait for it.
        panel.ToggleLockCommand.Execute(null);
        Assert.False(panel.IsLocked);

        await WaitForAsync(() => panel.HasStaleNoteMessage);
        Assert.Contains("changed after you opened it", panel.StaleNoteMessage);
    }

    [Fact]
    public async Task LoadingAnotherNoteDropsAWarningThatBelongedToThePreviousOne()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        var saved = await SaveNoteAsync(fixture, person, "The original narrative.");
        var panel = await PanelShowingAsync(fixture, person, saved);

        var elsewhere = fixture.NotesFromAnotherSession();
        var theirCopy = (await elsewhere.GetAllByPersonAsync(person.Id))
            .Single(candidate => candidate.Id == saved.Id);
        theirCopy.Narrative = "Corrected by a supervisor.";
        await elsewhere.UpdateNoteAsync(theirCopy);
        await panel.VerifyLoadedNoteIsCurrentAsync();
        Assert.True(panel.HasStaleNoteMessage);

        var other = await SaveNoteAsync(fixture, person, "A different note entirely.");
        panel.EnterViewMode(other);

        Assert.False(panel.HasStaleNoteMessage);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(20);

        Assert.True(condition(), "The awaited view-model state never arrived.");
    }

    /// <summary>Reads fail; everything else behaves normally.</summary>
    private sealed class UnreachableNoteService(INoteService inner, Exception failure) : INoteService
    {
        public Task<List<Note>> GetAllByPersonAsync(int personId) =>
            Task.FromException<List<Note>>(failure);

        public Task<Note> AddNoteAsync(Note note) => inner.AddNoteAsync(note);
        public Task UpdateNoteAsync(Note note) => inner.UpdateNoteAsync(note);
        public Task DeleteNoteAsync(Note note) => inner.DeleteNoteAsync(note);
        public Task UpdateAbandonedNotesAsync(int abandonedAfterDays) =>
            inner.UpdateAbandonedNotesAsync(abandonedAfterDays);
        public Task<List<Note>> GetMonthlyNotesAsync(int userId) => inner.GetMonthlyNotesAsync(userId);
        public Task<List<Note>> GetByYearAsync(int userId, int year) => inner.GetByYearAsync(userId, year);
        public Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date) =>
            inner.GetDayScheduleAsync(userId, date);
    }
}
