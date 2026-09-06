using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class WorkAgendaNoteEntryTests
{
    [Fact]
    public async Task FuturePlannedWorkKeepsItsOwnedStatusAndStartTimeUnavailable()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();

        panel.SelectedNoteType = NoteType.Email;
        panel.Minutes = 30;
        panel.EventDate = DateTime.Today.AddDays(1);

        Assert.Equal(NoteStatus.Scheduled, panel.Status);
        Assert.True(panel.IsFutureScheduledWork);
        Assert.False(panel.IsStatusEnabled);
        Assert.False(panel.IsServiceTimeEnabled);
        Assert.Equal(30, panel.Minutes);
        Assert.Null(panel.SelectedStartTime);

        panel.EventDate = DateTime.Today;

        Assert.False(panel.IsFutureScheduledWork);
        Assert.True(panel.IsStatusEnabled);
        Assert.True(panel.IsServiceTimeEnabled);
    }

    [Fact]
    public async Task StartingScheduledEmailBuildsPendingDraftInEarliestFreeWindowAndUpdatesSameRow()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var notes = fixture.NotesFromAnotherSession();

        var occupied = Note.Create(
            "Morning call",
            DateTime.Today,
            NoteStatus.Pending,
            30,
            fixture.PersonOneId,
            null,
            NoteType.Phone);
        occupied.StartTime = 0; // 7:00–7:30 AM.
        await notes.AddNoteAsync(occupied);

        var scheduled = Note.Create(
            "Email the provider about the annual packet.",
            DateTime.Today,
            NoteStatus.Scheduled,
            45,
            fixture.PersonOneId,
            null,
            NoteType.Email);
        await notes.AddNoteAsync(scheduled);

        var panel = fixture.NoteEntry(notes: notes);
        panel.SetPeople([await fixture.PersonOneAsync(), await fixture.PersonTwoAsync()]);

        var opened = await panel.PrepareScheduledWorkAsync(scheduled);

        Assert.True(opened);
        Assert.Equal("Start Note", panel.EditorHeading);
        Assert.Equal(fixture.PersonOneId, panel.SelectedPerson!.Id);
        Assert.Equal(NoteStatus.Pending, panel.Status);
        Assert.Equal(NoteType.Email, panel.SelectedNoteType);
        Assert.Equal(DateTime.Today, panel.EventDate);
        Assert.Equal(45, panel.Minutes);
        Assert.Equal(30, panel.SelectedStartTime!.Minutes); // 7:30 AM.
        Assert.Equal("[Email the provider about the annual packet.]", panel.Narrative);

        await panel.SubmitNoteCommand.ExecuteAsync(null);

        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.Notes.AsNoTracking().OrderBy(note => note.Id).ToListAsync();
        Assert.Equal(2, stored.Count);
        var continued = Assert.Single(stored, note => note.Id == scheduled.Id);
        Assert.Equal(NoteStatus.Pending, continued.Status);
        Assert.Equal(NoteType.Email, continued.NoteType);
        Assert.Equal(30, continued.StartTime);
    }

    [Fact]
    public async Task StartingLegacyContactDefaultsToPhoneWithoutChangingStoredRowUntilSave()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var notes = fixture.NotesFromAnotherSession();
        var scheduled = Note.Create(
            "Contact guardian",
            DateTime.Today,
            NoteStatus.Scheduled,
            15,
            fixture.PersonOneId,
            null,
            NoteType.Contact);
        await notes.AddNoteAsync(scheduled);
        var panel = fixture.NoteEntry(notes: notes);
        panel.SetPeople([await fixture.PersonOneAsync()]);

        await panel.PrepareScheduledWorkAsync(scheduled);

        Assert.Equal(NoteType.Phone, panel.SelectedNoteType);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Equal(NoteType.Contact,
            (await db.Notes.AsNoTracking().SingleAsync()).NoteType);
    }
}
