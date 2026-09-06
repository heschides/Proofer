using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services.LocalAi;
using Sati.ViewModels;
using Sati.ViewModels.Children;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The two Reminder modes. An undated reminder is a stamped journal entry and
/// creates no note. Giving the Reminder a future date creates a Scheduled
/// Reminder row so the calendar can retrieve it. Other future-dated note types
/// remain their selected planned-work type.
/// </summary>
public sealed class JournalReminderTests
{
    private static readonly DateTime Stamp = new(2026, 8, 18, 15, 42, 0);

    // -------------------------------------------------------------------------
    // The contract that owns the stamp and the placement
    // -------------------------------------------------------------------------

    [Fact]
    public void AnEntryCarriesTheDateAndTimeItWasWritten()
    {
        var entry = JournalEntry.ComposeReminder(Stamp, "Call the guardian back.");

        Assert.StartsWith($"August 18, 2026 3:42 PM — {JournalEntry.ReminderLabel}", entry);
        Assert.Contains("Call the guardian back.", entry);
    }

    [Fact]
    public void TheNewestEntryIsAtTheTopAndTheOlderOneIsStillThere()
    {
        var first = JournalEntry.PrependReminder(null, Stamp.AddDays(-1), "Older reminder");
        var both = JournalEntry.PrependReminder(first, Stamp, "Newer reminder");

        Assert.StartsWith($"August 18, 2026 3:42 PM — {JournalEntry.ReminderLabel}\r\nNewer reminder", both);
        Assert.Contains("Older reminder", both);
        Assert.True(
            both.IndexOf("Newer reminder", StringComparison.Ordinal) <
            both.IndexOf("Older reminder", StringComparison.Ordinal),
            "The newest entry must be above the older one.");
    }

    [Fact]
    public void JournalTextTheCaseManagerWroteSurvivesUnderneath()
    {
        const string handwritten = "Guardian prefers afternoon calls.\r\nUses a communication device.";

        var result = JournalEntry.PrependReminder(handwritten, Stamp, "Reminder text");

        Assert.EndsWith(handwritten, result);
        Assert.Contains("Reminder text", result);
    }

    [Fact]
    public void BlankLinesDoNotAccumulateAtTheSeamAsEntriesArrive()
    {
        var journal = JournalEntry.PrependReminder("Existing note.", Stamp.AddDays(-2), "One");
        journal = JournalEntry.PrependReminder(journal, Stamp.AddDays(-1), "Two");
        journal = JournalEntry.PrependReminder(journal, Stamp, "Three");

        Assert.DoesNotContain("\r\n\r\n\r\n", journal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void AnEmptyReminderIsRefused(string text) =>
        Assert.Throws<ArgumentException>(() => JournalEntry.ComposeReminder(Stamp, text));

    [Fact]
    public void AReminderLongerThanTheContractAllowsIsRefused()
    {
        var tooLong = new string('x', JournalEntry.MaxTextLength + 1);

        Assert.Throws<ArgumentException>(() => JournalEntry.ComposeReminder(Stamp, tooLong));
    }

    // -------------------------------------------------------------------------
    // The desktop-local write path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TheEntryIsPrependedToTheStoredJournalAndTheNewTextIsReturned()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var people = fixture.PeopleAs(fixture.CaseManagerOne);
        await people.SaveJournalAsync(fixture.PersonOneId, "Handwritten line.");

        var result = await people.AddJournalReminderAsync(fixture.PersonOneId, "Send the release form.");

        var stored = await people.GetJournalAsync(fixture.PersonOneId);
        Assert.Equal(stored, result.Journal);
        Assert.Contains(JournalEntry.ReminderLabel, stored);
        Assert.Contains("Send the release form.", stored);
        Assert.EndsWith("Handwritten line.", stored);
    }

    [Fact]
    public async Task AReminderCannotBeWrittenToAnotherAgencysClient()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var outsider = fixture.PeopleAs(fixture.CaseManagerTwo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => outsider.AddJournalReminderAsync(fixture.PersonOneId, "Not yours."));

        var untouched = await fixture.PeopleAs(fixture.CaseManagerOne)
            .GetJournalAsync(fixture.PersonOneId);
        Assert.True(string.IsNullOrEmpty(untouched), "The journal must not have been written.");
    }

    [Fact]
    public async Task TheWriteIsRecordedAsAReminderRatherThanAPlainJournalEdit()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();

        await fixture.PeopleAs(fixture.CaseManagerOne)
            .AddJournalReminderAsync(fixture.PersonOneId, "Confirm transportation.");

        await using var db = fixture.Factory.CreateDbContext();
        var actions = await db.AuditEvents.AsNoTracking()
            .Where(x => x.ResourceType == "Person")
            .Select(x => x.Action)
            .ToListAsync();
        Assert.Contains("person.journal-reminder-added", actions);
        Assert.DoesNotContain("person.journal-updated", actions);
    }

    // -------------------------------------------------------------------------
    // What the note screen does with the type selected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ChoosingAFutureDatePreservesTheWorkTypeAndMakesItScheduled()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var viewModel = fixture.NoteEntry();
        viewModel.SelectedPerson = await fixture.PersonOneAsync();
        viewModel.SelectedNoteType = NoteType.Email;
        viewModel.Status = NoteStatus.Logged;
        viewModel.Minutes = 45;
        var reminderDate = DateTime.Today.AddDays(7);

        viewModel.EventDate = reminderDate;

        Assert.Equal(NoteType.Email, viewModel.SelectedNoteType);
        Assert.Equal(NoteStatus.Scheduled, viewModel.Status);
        Assert.Equal(reminderDate.Date, viewModel.EventDate);
        Assert.Equal(45, viewModel.Minutes);
        Assert.Null(viewModel.SelectedStartTime);

        // The future-date rule is authoritative about workflow status while the
        // planned work type remains editable.
        viewModel.SelectedNoteType = NoteType.Visit;
        Assert.Equal(NoteType.Visit, viewModel.SelectedNoteType);
        Assert.Equal(NoteStatus.Scheduled, viewModel.Status);
    }

    [Fact]
    public async Task SavingAFutureEmailKeepsItsTypeAndEstimateOnThatCalendarDay()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var viewModel = fixture.NoteEntry();
        viewModel.SelectedPerson = await fixture.PersonOneAsync();
        viewModel.SelectedNoteType = NoteType.Email;
        viewModel.Status = NoteStatus.Pending;
        viewModel.Minutes = 30;
        viewModel.Narrative = "Call the guardian about transportation.";
        var reminderDate = DateTime.Today.AddDays(7);
        viewModel.EventDate = reminderDate;

        await viewModel.SubmitNoteCommand.ExecuteAsync(null);

        await using (var db = fixture.Factory.CreateDbContext())
        {
            var stored = Assert.Single(await db.Notes.AsNoTracking().ToListAsync());
            Assert.Equal(NoteType.Email, stored.NoteType);
            Assert.Equal(NoteStatus.Scheduled, stored.Status);
            Assert.Equal(reminderDate.Date, stored.EventDate);
            Assert.Equal(30, stored.Minutes);
            Assert.Null(stored.StartTime);
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var calendar = new CalendarViewModel(
            new ExemptDateService(fixture.Factory, session),
            new NoteService(fixture.Factory, session),
            session)
        {
            CurrentYear = reminderDate.Year
        };
        await calendar.RefreshCommand.ExecuteAsync(null);

        var day = calendar.Months
            .SelectMany(month => month.Cells)
            .Single(candidate => candidate?.Date == reminderDate.Date)!;
        var reminder = Assert.Single(day.Notes);
        Assert.Equal("Email", reminder.NoteTypeLabel);
        Assert.Equal("Call the guardian about transportation.", reminder.Narrative);

        var journal = await fixture.PeopleAs(fixture.CaseManagerOne)
            .GetJournalAsync(fixture.PersonOneId);
        Assert.True(string.IsNullOrEmpty(journal));
    }

    [Fact]
    public async Task ExplicitFutureReminderStillCarriesNoServiceTime()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var viewModel = fixture.NoteEntry();
        viewModel.SelectedPerson = await fixture.PersonOneAsync();
        viewModel.SelectedNoteType = NoteType.Reminder;
        viewModel.Narrative = "Call after the planning meeting.";
        viewModel.EventDate = DateTime.Today.AddDays(3);

        await viewModel.SubmitNoteCommand.ExecuteAsync(null);

        await using var db = fixture.Factory.CreateDbContext();
        var stored = Assert.Single(await db.Notes.AsNoTracking().ToListAsync());
        Assert.Equal(NoteType.Reminder, stored.NoteType);
        Assert.Equal(NoteStatus.Scheduled, stored.Status);
        Assert.Null(stored.Minutes);
        Assert.Null(stored.StartTime);
    }

    [Fact]
    public async Task SelectingReminderDisablesTheServiceFieldsAndClearsWhatWasInThem()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var viewModel = fixture.NoteEntry();
        viewModel.SelectedPerson = await fixture.PersonOneAsync();

        // A half-finished service note first: these values must not ride along.
        viewModel.SelectedNoteType = NoteType.Visit;
        viewModel.Status = NoteStatus.Logged;
        viewModel.Minutes = 45;
        viewModel.EventDate = DateTime.Today;
        Assert.True(viewModel.AreNoteFieldsEnabled);

        viewModel.SelectedNoteType = NoteType.Reminder;

        Assert.True(viewModel.IsReminderNote);
        Assert.False(viewModel.AreNoteFieldsEnabled);
        Assert.Null(viewModel.Status);
        Assert.Null(viewModel.Minutes);
        Assert.Null(viewModel.EventDate);
        Assert.Null(viewModel.SelectedStartTime);
        Assert.False(viewModel.IsFormNote);
        Assert.False(viewModel.IsVisitNote);
        Assert.Equal("Add Reminder", viewModel.SaveActionLabel);
        Assert.Equal("REMINDER", viewModel.NarrativeLabel);
        Assert.Contains("journal", viewModel.StatusGuidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChoosingAServiceNoteAgainRestoresTheFields()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var viewModel = fixture.NoteEntry();
        viewModel.SelectedPerson = await fixture.PersonOneAsync();

        viewModel.SelectedNoteType = NoteType.Reminder;
        viewModel.SelectedNoteType = NoteType.Contact;

        Assert.False(viewModel.IsReminderNote);
        Assert.True(viewModel.AreNoteFieldsEnabled);
        Assert.Equal("NARRATIVE", viewModel.NarrativeLabel);
    }

    [Fact]
    public async Task ExistingNoteChangesTheEditorHeadingUntilTheEditIsCleared()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var viewModel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        viewModel.SetPeople([person]);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        Assert.Equal("New Note", viewModel.EditorHeading);

        var existing = Note.Create(
            "Existing narrative",
            new DateTime(2026, 8, 20),
            NoteStatus.Pending,
            15,
            person.Id,
            null,
            NoteType.Contact);
        viewModel.EnterEditMode(existing);

        Assert.True(viewModel.IsEditing);
        Assert.Equal("Edit Note", viewModel.EditorHeading);
        Assert.Contains(nameof(NoteEntryViewModel.EditorHeading), changedProperties);

        changedProperties.Clear();
        viewModel.ClearCommand.Execute(null);

        Assert.False(viewModel.IsEditing);
        Assert.Equal("New Note", viewModel.EditorHeading);
        Assert.Contains(nameof(NoteEntryViewModel.EditorHeading), changedProperties);
    }

    /// <summary>
    /// Dropping the grid's highlight — by Ctrl-clicking the row, or by anything
    /// else that nulls the selection — must not reach into an edit the case
    /// manager has open. Returning to a blank panel is New Note's job, and it asks
    /// first; this path is not that and must stay quiet.
    /// </summary>
    [Fact]
    public async Task ClearingTheGridSelectionDoesNotDisturbAnOpenEdit()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var viewModel = fixture.NotesWindow();
        var person = await fixture.PersonOneAsync();
        viewModel.NoteEntry.SetPeople([person]);
        var existing = Note.Create(
            "Keep this editor draft",
            new DateTime(2026, 8, 21),
            NoteStatus.Pending,
            15,
            person.Id,
            null,
            NoteType.Contact);
        viewModel.SelectedNote = existing;
        viewModel.NoteEntry.EnterEditMode(existing);

        viewModel.SelectedNote = null;

        Assert.Null(viewModel.SelectedNote);
        Assert.True(viewModel.NoteEntry.IsEditing);
        Assert.Equal("Keep this editor draft", viewModel.NoteEntry.Narrative);
    }

    [Fact]
    public async Task SavingAReminderWritesTheJournalAndCreatesNoNote()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var viewModel = fixture.NoteEntry();
        viewModel.SelectedPerson = await fixture.PersonOneAsync();
        viewModel.SelectedNoteType = NoteType.Reminder;
        viewModel.Narrative = "Guardian is expecting a call Thursday.";

        JournalReminderAddedEventArgs? announced = null;
        viewModel.ReminderAdded += (s, e) => announced = e;

        await viewModel.SubmitNoteCommand.ExecuteAsync(null);

        var journal = await fixture.PeopleAs(fixture.CaseManagerOne)
            .GetJournalAsync(fixture.PersonOneId);
        Assert.Contains("Guardian is expecting a call Thursday.", journal);
        Assert.Contains(JournalEntry.ReminderLabel, journal);

        await using var db = fixture.Factory.CreateDbContext();
        Assert.Empty(await db.Notes.AsNoTracking().ToListAsync());

        Assert.NotNull(announced);
        Assert.Equal(fixture.PersonOneId, announced!.PersonId);
        Assert.Equal(journal, announced.Journal);

        // Fields reset for the next entry, client deliberately still selected.
        Assert.Null(viewModel.SelectedNoteType);
        Assert.Equal(string.Empty, viewModel.Narrative);
        Assert.NotNull(viewModel.SelectedPerson);
    }

    [Fact]
    public async Task ThePendingJournalEditIsFlushedBeforeTheEntryIsWritten()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var viewModel = fixture.NoteEntry();
        viewModel.SelectedPerson = await fixture.PersonOneAsync();
        viewModel.SelectedNoteType = NoteType.Reminder;
        viewModel.Narrative = "Reminder text";

        // Stands in for the client page: its unsaved journal text reaches the
        // database only because the reminder path awaits this first.
        var people = fixture.PeopleAs(fixture.CaseManagerOne);
        viewModel.JournalWriteStartingAsync = async personId =>
            await people.SaveJournalAsync(personId, "Typed but not yet saved.");

        await viewModel.SubmitNoteCommand.ExecuteAsync(null);

        var journal = await people.GetJournalAsync(fixture.PersonOneId);
        Assert.Contains("Reminder text", journal);
        Assert.EndsWith("Typed but not yet saved.", journal);
    }

    [Fact]
    public async Task AReminderWithNoTextWritesNothing()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var viewModel = fixture.NoteEntry();
        viewModel.SelectedPerson = await fixture.PersonOneAsync();
        viewModel.SelectedNoteType = NoteType.Reminder;
        viewModel.Narrative = "   ";

        var announced = false;
        viewModel.ReminderAdded += (s, e) => announced = true;

        // The validation dialog is a WPF window, so this asserts the write is
        // refused BEFORE any dialog is reached: the factory would throw otherwise.
        await Assert.ThrowsAsync<NotSupportedException>(
            () => viewModel.SubmitNoteCommand.ExecuteAsync(null));

        Assert.False(announced);
        var journal = await fixture.PeopleAs(fixture.CaseManagerOne)
            .GetJournalAsync(fixture.PersonOneId);
        Assert.True(string.IsNullOrEmpty(journal));
    }

    [Fact]
    public async Task TheLocalAiDraftingCommandIsUnavailableForAReminder()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var viewModel = fixture.NoteEntry(aiEnabled: true);
        viewModel.SelectedPerson = await fixture.PersonOneAsync();
        viewModel.Narrative = "rough text";

        viewModel.SelectedNoteType = NoteType.Contact;
        Assert.True(viewModel.FormatNarrativeWithAiCommand.CanExecute(null));

        viewModel.SelectedNoteType = NoteType.Reminder;
        Assert.False(viewModel.FormatNarrativeWithAiCommand.CanExecute(null));
    }

    [Fact]
    public async Task AClientSwitchWhileLocalAiIsRunningCannotPublishTheOldClientsDraft()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var formatter = new BlockingCaseNoteFormatter();
        var viewModel = fixture.NoteEntry(
            aiContext: new StubClientAiContextService(),
            formatter: formatter);
        var first = await fixture.PersonOneAsync();
        var second = await fixture.PersonTwoAsync();
        viewModel.SelectedPerson = first;
        viewModel.SelectedNoteType = NoteType.Contact;
        viewModel.Narrative = "Current facts for the first client.";

        var formatting = viewModel.FormatNarrativeWithAiCommand.ExecuteAsync(null);
        await formatter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.SelectedPerson = second;
        formatter.Release.TrySetResult();
        await formatting;

        Assert.Equal(second.Id, viewModel.SelectedPerson!.Id);
        Assert.False(viewModel.IsAiReviewVisible);
        Assert.True(string.IsNullOrEmpty(viewModel.AiDraftNarrative));
    }

    /// <summary>Stands in for a Demo server that predates the journal-entries route.</summary>
    private sealed class BlockingCaseNoteFormatter : ICaseNoteFormatter
    {
        public bool IsEnabled => true;
        public int MaxInputWords => 500;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<CaseNoteFormattingResult> FormatAsync(
            CaseNoteFormattingRequest request,
            IProgress<CaseNoteFormattingProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task;
            return new CaseNoteFormattingResult(
                "Draft for the first client.",
                [],
                request.SourceFingerprint,
                request.Facts.Where(fact => fact.Required).Select(fact => fact.Id).ToHashSet());
        }
    }
}
