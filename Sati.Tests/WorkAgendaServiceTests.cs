using Sati.Data;
using Sati.Models;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class WorkAgendaServiceTests
{
    private static readonly DateTime Today = new(2026, 9, 5);

    [Fact]
    public async Task ScheduledNotesAreGroupedByTheirActualType()
    {
        var notes = new RecordingNoteService(
            Scheduled(NoteType.Form, "Complete Q1 review", 11, FormType.Q1R),
            Scheduled(NoteType.Visit, "Home visit", 12),
            Scheduled(NoteType.Phone, "Call guardian", 13),
            Scheduled(NoteType.Email, "Email provider", 14),
            Scheduled(NoteType.Other, "Check portal", 15),
            Note.Create("Already started", Today, NoteStatus.Pending, 15, 11, null, NoteType.Phone));

        var result = await new WorkAgendaService(notes).LoadAsync(41, Today);

        Assert.Equal(5, result.Count);
        Assert.Contains(result, item => item.Section == WorkAgendaSection.Paperwork && item.TypeLabel == "Q1 Review");
        Assert.Contains(result, item => item.Section == WorkAgendaSection.Visits);
        Assert.Contains(result, item => item.Section == WorkAgendaSection.Calls);
        Assert.Contains(result, item => item.Section == WorkAgendaSection.Emails);
        Assert.Contains(result, item => item.Section == WorkAgendaSection.Freeform);
    }

    [Fact]
    public async Task LoginSelectionCreatesAnEditableScheduledFormWithoutTouchingFreeText()
    {
        var notes = new RecordingNoteService();
        var item = AgendaItem();

        var result = await new WorkAgendaService(notes).AddFromDailyAgendaAsync(
            41, Today, [item]);

        var saved = Assert.Single(notes.Notes);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(NoteStatus.Scheduled, saved.Status);
        Assert.Equal(NoteType.Form, saved.NoteType);
        Assert.Equal(FormType.Q1R, saved.FormType);
        Assert.Equal(WorkAgendaService.DefaultMinutes, saved.Minutes);
        Assert.Null(saved.StartTime);
        Assert.Equal(Today, saved.EventDate);
        Assert.Equal(DailyAgendaText.FormatItem(item), saved.Narrative);
    }

    [Fact]
    public async Task RetryingTheSameLoginSelectionDoesNotCreateADuplicate()
    {
        var notes = new RecordingNoteService();
        var service = new WorkAgendaService(notes);
        var item = AgendaItem();

        await service.AddFromDailyAgendaAsync(41, Today, [item]);
        var retry = await service.AddFromDailyAgendaAsync(41, Today, [item]);

        Assert.Single(notes.Notes);
        Assert.Equal(0, retry.AddedCount);
        Assert.Equal(1, retry.ExistingCount);
    }

    [Fact]
    public async Task MissingClientOrFormTypeIsRejectedBeforeAnyItemIsWritten()
    {
        var notes = new RecordingNoteService();
        var valid = AgendaItem();
        var invalid = valid with { PersonId = 0, FormType = null };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WorkAgendaService(notes).AddFromDailyAgendaAsync(
                41, Today, [valid, invalid]));

        Assert.Empty(notes.Notes);
    }

    private static DailyAgendaItem AgendaItem() => new(
        "form:11:7:Q1R",
        11,
        "Alex Person",
        "Q1 Review",
        Today.AddDays(-3),
        DailyAgendaItemKind.OverdueForm,
        true,
        FormType.Q1R);

    private static Note Scheduled(
        NoteType type,
        string narrative,
        int personId,
        FormType? formType = null)
    {
        var note = Note.Create(
            narrative, Today, NoteStatus.Scheduled, 15, personId, formType, type);
        var person = Person.Rehydrate(personId, 41);
        person.FirstName = $"Client {personId}";
        note.Person = person;
        return note;
    }

    private sealed class RecordingNoteService(params Note[] seed) : INoteService
    {
        public List<Note> Notes { get; } = [.. seed];

        public Task<Note> AddNoteAsync(Note note)
        {
            Notes.Add(note);
            return Task.FromResult(note);
        }

        public Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date) =>
            Task.FromResult(Notes.Where(note => note.EventDate?.Date == date.Date).ToList());

        public Task DeleteNoteAsync(Note note) => throw new NotSupportedException();
        public Task UpdateNoteAsync(Note note) => throw new NotSupportedException();
        public Task<List<Note>> GetAllByPersonAsync(int personId) => throw new NotSupportedException();
        public Task UpdateAbandonedNotesAsync(int abandonedAfterDays) => throw new NotSupportedException();
        public Task<List<Note>> GetMonthlyNotesAsync(int userId) => throw new NotSupportedException();
        public Task<List<Note>> GetByYearAsync(int userId, int year) => throw new NotSupportedException();
    }
}
