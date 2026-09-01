using Sati.Models;
using Sati.ViewModels;
using Sati.ViewModels.Children;
using System.Linq;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The note panel has three modes and one set of fields. New Note is the resting
/// state; selecting a row in a host's grid shows that note as View Note, locked;
/// the padlock turns that into Edit Note. There is no second read-only copy of the
/// same fields anywhere, so there is nothing for a note's display and its editor
/// to disagree about.
/// </summary>
public sealed class NotePanelModeTests
{
    private static Note ExistingNote(int personId, string narrative = "Existing narrative") =>
        Note.Create(narrative, new DateTime(2026, 8, 20), NoteStatus.Pending, 15,
            personId, null, NoteType.Contact);

    // -------------------------------------------------------------------------
    // Modes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThePanelOpensAsANewNote()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();

        Assert.Equal("New Note", panel.EditorHeading);
        Assert.False(panel.IsEditing);
        Assert.False(panel.IsLocked);
        Assert.True(panel.SubmitNoteCommand.CanExecute(null));
    }

    [Fact]
    public async Task ViewingASavedNoteLocksEveryFieldThatWouldChangeTheRecord()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);

        panel.EnterViewMode(ExistingNote(person.Id));

        Assert.Equal("View Note", panel.EditorHeading);
        Assert.True(panel.IsEditing);
        Assert.True(panel.IsLocked);
        Assert.False(panel.IsUnlocked);
        Assert.False(panel.AreNoteFieldsEnabled);

        // The button is hidden in the view, but the view is not the gate.
        Assert.False(panel.SubmitNoteCommand.CanExecute(null));
        Assert.False(panel.FormatNarrativeWithAiCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheLockToggleMovesBetweenViewingAndEditingTheSameNote()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.EnterViewMode(ExistingNote(person.Id));

        panel.ToggleLockCommand.Execute(null);

        Assert.Equal("Edit Note", panel.EditorHeading);
        Assert.False(panel.IsLocked);
        Assert.True(panel.AreNoteFieldsEnabled);
        Assert.True(panel.SubmitNoteCommand.CanExecute(null));

        panel.ToggleLockCommand.Execute(null);

        Assert.Equal("View Note", panel.EditorHeading);
        Assert.True(panel.IsLocked);
    }

    [Fact]
    public async Task TheLockToggleIsUnavailableUntilASavedNoteIsLoaded()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);

        Assert.False(panel.ToggleLockCommand.CanExecute(null));

        panel.EnterViewMode(ExistingNote(person.Id));
        Assert.True(panel.ToggleLockCommand.CanExecute(null));

        panel.Clear();
        Assert.False(panel.ToggleLockCommand.CanExecute(null));
    }

    [Fact]
    public async Task ASupervisorsReturnReasonTravelsWithTheNoteIntoThePanel()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        var returned = ExistingNote(person.Id);
        returned.Status = NoteStatus.Returned;
        returned.ReturnReason = "Add the guardian contact you referenced.";

        panel.EnterViewMode(returned);

        Assert.True(panel.HasReturnReason);
        Assert.Equal("Add the guardian contact you referenced.", panel.ReturnReason);

        panel.Clear();
        Assert.False(panel.HasReturnReason);
    }

    // -------------------------------------------------------------------------
    // Unsaved work
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadingANoteIsAReadAndDoesNotCountAsUnsavedWork()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);

        panel.EnterEditMode(ExistingNote(person.Id));

        Assert.False(panel.HasUnsavedChanges);
        Assert.True(panel.TryReleaseDraft());
    }

    [Fact]
    public async Task TypingIntoANewNoteMakesItUnsavedWork()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry(discardAnswer: false);
        panel.SetPeople([await fixture.PersonOneAsync()]);

        panel.Narrative = "Half a visit note.";

        Assert.True(panel.HasUnsavedChanges);
        Assert.False(panel.TryReleaseDraft());
    }

    [Fact]
    public async Task LockingANoteWithUnsavedEditsAsksFirstAndKeepsThemOnRefusal()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry(discardAnswer: false);
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.EnterEditMode(ExistingNote(person.Id));
        panel.Narrative = "A correction the case manager has not saved.";

        panel.ToggleLockCommand.Execute(null);

        Assert.False(panel.IsLocked);
        Assert.Equal("A correction the case manager has not saved.", panel.Narrative);
        Assert.True(panel.HasUnsavedChanges);
    }

    [Fact]
    public async Task LockingAfterAgreeingToDiscardRestoresTheSavedNarrative()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry(discardAnswer: true);
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.EnterEditMode(ExistingNote(person.Id, "The saved narrative."));
        panel.Narrative = "An edit that is being abandoned.";

        panel.ToggleLockCommand.Execute(null);

        Assert.True(panel.IsLocked);
        Assert.Equal("The saved narrative.", panel.Narrative);
        Assert.False(panel.HasUnsavedChanges);
    }

    // -------------------------------------------------------------------------
    // The grid drives the panel
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SelectingARowShowsThatNoteInThePanelLocked()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = fixture.NotesWindow();
        var person = await fixture.PersonOneAsync();
        log.NoteEntry.SetPeople([person]);
        var note = ExistingNote(person.Id, "What the row says.");

        log.SelectedNote = note;

        Assert.True(log.NoteEntry.IsShowing(note));
        Assert.True(log.NoteEntry.IsLocked);
        Assert.Equal("View Note", log.NoteEntry.EditorHeading);
        Assert.Equal("What the row says.", log.NoteEntry.Narrative);
    }

    [Fact]
    public async Task DoubleClickingASelectedRowOpensItForEditing()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = fixture.NotesWindow();
        var person = await fixture.PersonOneAsync();
        log.NoteEntry.SetPeople([person]);
        log.SelectedNote = ExistingNote(person.Id);

        log.OpenSelectedNoteForEdit();

        Assert.False(log.NoteEntry.IsLocked);
        Assert.Equal("Edit Note", log.NoteEntry.EditorHeading);
    }

    /// <summary>
    /// The test gives the seeded client one explicitly overdue incomplete form, so
    /// Mark Note Logged opens the compliance dialog. Holding the note is the
    /// status change this test is about; which of the two hold statuses the gate
    /// picks is `NotesWindowViewModel`'s business and is asserted elsewhere.
    /// </summary>
    [Fact]
    public async Task AStatusChangedFromTheGridUpdatesWhatThePanelShows()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = fixture.NotesWindow();
        var person = await fixture.PersonOneAsync();
        log.NoteEntry.SetPeople([person]);
        log.NoteEntry.SelectedPerson = person;
        log.NoteEntry.SelectedNoteType = NoteType.Contact;
        log.NoteEntry.Status = NoteStatus.Pending;
        log.NoteEntry.EventDate = new DateTime(2026, 8, 20);
        log.NoteEntry.Minutes = 15;
        log.NoteEntry.Narrative = "A note whose status is about to change.";
        await log.NoteEntry.SubmitNoteCommand.ExecuteAsync(null);
        await log.ReloadAsync();

        var saved = Assert.Single(log.NotesView.Cast<Note>());
        saved.Person.Forms.Add(new Form(
            FormType.PCP, DateTime.Today.AddDays(-1)));
        log.SelectedNote = saved;
        Assert.Equal(NoteStatus.Pending, log.NoteEntry.Status);

        await log.MarkNoteLoggedCommand.ExecuteAsync(null);
        Assert.True(log.IsComplianceDialogVisible);
        await log.HoldForComplianceCommand.ExecuteAsync(null);

        // The panel copies fields in on load, so a status changed from the grid
        // has to reach it — a stale Pending here would misdescribe the record.
        Assert.NotEqual(NoteStatus.Pending, saved.Status);
        Assert.Equal(saved.Status, log.NoteEntry.Status);
        Assert.True(log.NoteEntry.IsLocked);
    }

    [Fact]
    public async Task ClearingTheGridSelectionReturnsALockedPanelToANewNote()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = fixture.NotesWindow();
        var person = await fixture.PersonOneAsync();
        log.NoteEntry.SetPeople([person]);
        log.SelectedNote = ExistingNote(person.Id);

        log.SelectedNote = null;

        Assert.Null(log.SelectedNote);
        Assert.False(log.NoteEntry.IsEditing);
        Assert.Equal("New Note", log.NoteEntry.EditorHeading);
    }

    // -------------------------------------------------------------------------
    // The way back to New Note
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NewNoteIsOfferedOnlyWhenItWouldDoSomething()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);

        // A blank panel is already where the command would take you.
        Assert.False(panel.StartNewNoteCommand.CanExecute(null));

        panel.SelectedPerson = person;
        panel.Narrative = "Something typed.";
        Assert.True(panel.StartNewNoteCommand.CanExecute(null));

        panel.EnterViewMode(ExistingNote(person.Id));
        Assert.True(panel.StartNewNoteCommand.CanExecute(null));

        // Not while a compliance decision is on screen waiting for an answer.
        panel.IsComplianceDialogVisible = true;
        Assert.False(panel.StartNewNoteCommand.CanExecute(null));
    }

    [Fact]
    public async Task NewNoteKeepsTheClientSoTheNextNoteNeedsNoReselection()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.EnterViewMode(ExistingNote(person.Id));

        panel.StartNewNoteCommand.Execute(null);

        Assert.Equal("New Note", panel.EditorHeading);
        Assert.False(panel.IsEditing);
        Assert.False(panel.IsLocked);
        Assert.True(string.IsNullOrEmpty(panel.Narrative));

        // The client survives. On the dashboard this same property scopes the
        // notes grid, the compliance checkboxes and the forms, so clearing a note
        // there must not blank the page around it.
        Assert.Equal(person.Id, panel.SelectedPerson?.Id);
    }

    [Fact]
    public async Task NewNoteAsksBeforeDroppingUnsavedWorkAndObeysARefusal()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry(discardAnswer: false);
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.SelectedPerson = person;
        panel.Narrative = "Half a note.";
        var cleared = 0;
        panel.EditorCleared += (_, _) => cleared++;

        panel.StartNewNoteCommand.Execute(null);

        Assert.Equal("Half a note.", panel.Narrative);
        Assert.Equal(0, cleared);
    }

    [Fact]
    public async Task NewNoteOnTheNotesLogAlsoDropsTheHighlightedRow()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = fixture.NotesWindow();
        var person = await fixture.PersonOneAsync();
        log.NoteEntry.SetPeople([person]);
        log.SelectedNote = ExistingNote(person.Id);

        log.NoteEntry.StartNewNoteCommand.Execute(null);

        Assert.Null(log.SelectedNote);
        Assert.Equal("New Note", log.NoteEntry.EditorHeading);
        Assert.Equal(person.Id, log.NoteEntry.SelectedPerson?.Id);
    }

    [Fact]
    public async Task SuccessfulSaveClearsEveryNotesLogFilterByDefault()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = fixture.NotesWindow();
        await log.ReloadAsync();
        log.SelectedFilterPerson = log.FilterPeople.Single(person =>
            person?.Id == fixture.PersonOneId);
        log.SelectedStatusOption = NotesWindowViewModel.StatusOptions.Single(option =>
            option.Value == NoteStatus.Pending);
        log.SearchText = "follow-up";
        log.RangeStart = new DateTime(2026, 8, 1);
        log.RangeEnd = new DateTime(2026, 8, 31);

        await log.HandleSuccessfulNoteSaveAsync();

        Assert.Equal("All Persons", log.SelectedFilterPerson?.FullName);
        Assert.Null(log.SelectedStatusOption.Value);
        Assert.Equal(string.Empty, log.SearchText);
        Assert.Null(log.RangeStart);
        Assert.Null(log.RangeEnd);
    }

    [Fact]
    public async Task KeepFiltersAfterSaveRetainsAllValuesAndRebindsTheClient()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = fixture.NotesWindow();
        await log.ReloadAsync();
        var originalPerson = log.FilterPeople.Single(person =>
            person?.Id == fixture.PersonOneId)!;
        log.SelectedFilterPerson = originalPerson;
        log.SelectedStatusOption = NotesWindowViewModel.StatusOptions.Single(option =>
            option.Value == NoteStatus.Pending);
        log.SearchText = "follow-up";
        log.RangeStart = new DateTime(2026, 8, 1);
        log.RangeEnd = new DateTime(2026, 8, 31);
        log.KeepFiltersAfterSave = true;

        await log.HandleSuccessfulNoteSaveAsync();

        Assert.Equal(fixture.PersonOneId, log.SelectedFilterPerson?.Id);
        Assert.NotSame(originalPerson, log.SelectedFilterPerson);
        Assert.Equal(NoteStatus.Pending, log.SelectedStatusOption.Value);
        Assert.Equal("follow-up", log.SearchText);
        Assert.Equal(new DateTime(2026, 8, 1), log.RangeStart);
        Assert.Equal(new DateTime(2026, 8, 31), log.RangeEnd);
    }

    [Fact]
    public async Task SelectingARowNeverDiscardsAnUnsavedDraftWithoutConsent()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = fixture.NotesWindow(discardAnswer: false);
        var person = await fixture.PersonOneAsync();
        log.NoteEntry.SetPeople([person]);
        log.NoteEntry.SelectedPerson = person;
        log.NoteEntry.Narrative = "A visit note that is only half written.";

        log.SelectedNote = ExistingNote(person.Id, "Some other note.");

        // The selection snapped back and the draft is untouched.
        Assert.Null(log.SelectedNote);
        Assert.Equal("A visit note that is only half written.", log.NoteEntry.Narrative);
        Assert.False(log.NoteEntry.IsEditing);
    }

    [Fact]
    public async Task SelectingARowReplacesTheDraftOnceTheCaseManagerAgrees()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = fixture.NotesWindow(discardAnswer: true);
        var person = await fixture.PersonOneAsync();
        log.NoteEntry.SetPeople([person]);
        log.NoteEntry.SelectedPerson = person;
        log.NoteEntry.Narrative = "A visit note that is only half written.";
        var note = ExistingNote(person.Id, "Some other note.");

        log.SelectedNote = note;

        Assert.Same(note, log.SelectedNote);
        Assert.Equal("Some other note.", log.NoteEntry.Narrative);
        Assert.True(log.NoteEntry.IsLocked);
    }

    // -------------------------------------------------------------------------
    // One decision, both hosts
    // -------------------------------------------------------------------------

    /// <summary>
    /// `OpenForEdit` is the decision both the notes log and the dashboard route
    /// their double-click through. The dashboard used to call `EnterEditMode`
    /// straight through and so skipped the guard entirely; these assertions are on
    /// the shared method rather than on either host, which is the point of having
    /// moved it.
    /// </summary>
    [Fact]
    public async Task OpenForEditUnlocksTheNoteThePanelIsAlreadyShowing()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        var note = ExistingNote(person.Id, "Already on screen.");
        panel.EnterViewMode(note);

        panel.OpenForEdit(note);

        Assert.False(panel.IsLocked);
        Assert.True(panel.IsShowing(note));
        Assert.Equal("Already on screen.", panel.Narrative);
    }

    [Fact]
    public async Task OpenForEditRefusesToReplaceAnUnsavedDraftWhenDeclined()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry(discardAnswer: false);
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.SelectedPerson = person;
        panel.Narrative = "A draft that must survive a double-click elsewhere.";

        panel.OpenForEdit(ExistingNote(person.Id, "Some other note."));

        Assert.False(panel.IsEditing);
        Assert.Equal("A draft that must survive a double-click elsewhere.", panel.Narrative);
    }

    [Fact]
    public async Task OpenForEditReplacesTheDraftOnceTheCaseManagerAgrees()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry(discardAnswer: true);
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.SelectedPerson = person;
        panel.Narrative = "A draft being given up.";
        var note = ExistingNote(person.Id, "Some other note.");

        panel.OpenForEdit(note);

        Assert.True(panel.IsShowing(note));
        Assert.False(panel.IsLocked);
        Assert.Equal("Some other note.", panel.Narrative);
    }

    [Fact]
    public async Task OpenForEditOnNothingLeavesThePanelAlone()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.SelectedPerson = person;
        panel.Narrative = "Untouched.";

        panel.OpenForEdit(null);

        Assert.False(panel.IsEditing);
        Assert.Equal("Untouched.", panel.Narrative);
    }

    // -------------------------------------------------------------------------
    // Regression: loading a second client's note must stay an update
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loading a note attaches it AFTER the client selection, because selecting a
    /// different client clears the panel and drops whatever note it was holding.
    /// With the old order the panel kept IsEditing while losing the note behind it,
    /// so saving wrote a brand new note instead of updating the one on screen — a
    /// silent duplicate in the clinical record. Reachable from the notes log, whose
    /// grid lists every client's notes.
    /// </summary>
    [Fact]
    public async Task LoadingANoteForADifferentClientStillUpdatesThatNote()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var personOne = await fixture.PersonOneAsync();
        var personTwo = await fixture.PersonTwoAsync();
        panel.SetPeople([personOne, personTwo]);

        panel.EnterEditMode(ExistingNote(personOne.Id, "First client's note."));
        var second = ExistingNote(personTwo.Id, "Second client's note.");
        panel.EnterEditMode(second);

        Assert.True(panel.IsEditing);
        Assert.True(panel.IsShowing(second));
        Assert.Equal(personTwo.Id, panel.SelectedPerson?.Id);
        Assert.Equal("Second client's note.", panel.Narrative);
    }

    [Fact]
    public async Task DecliningAClientChangeKeepsTheExistingNoteAndNamesBothClients()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var personOne = await fixture.PersonOneAsync();
        var personTwo = await fixture.PersonTwoAsync();
        panel.SetPeople([personOne, personTwo]);
        var note = ExistingNote(personOne.Id, "Keep this correction on screen.");
        panel.EnterEditMode(note);
        string? confirmationMessage = null;
        panel.NoteReassignmentConfirmationRequested += (_, confirmation) =>
        {
            confirmationMessage = confirmation.Message;
            confirmation.Confirmed = false;
        };

        panel.SelectedPerson = personTwo;

        Assert.Equal(
            "Are you sure you want to reassign this note from Journal Person to Second Person?",
            confirmationMessage);
        Assert.Equal(personOne.Id, panel.SelectedPerson?.Id);
        Assert.True(panel.IsShowing(note));
        Assert.True(panel.IsEditing);
        Assert.Equal("Keep this correction on screen.", panel.Narrative);
    }

    [Fact]
    public async Task AClientChangeWithoutAConfirmationHandlerFailsClosed()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var personOne = await fixture.PersonOneAsync();
        var personTwo = await fixture.PersonTwoAsync();
        panel.SetPeople([personOne, personTwo]);
        var note = ExistingNote(personOne.Id);
        panel.EnterEditMode(note);

        panel.SelectedPerson = personTwo;

        Assert.Equal(personOne.Id, panel.SelectedPerson?.Id);
        Assert.True(panel.IsShowing(note));
    }

    [Fact]
    public async Task ConfirmingAClientChangeMovesTheSavedNoteInsteadOfCreatingADuplicate()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var notes = fixture.NotesFromAnotherSession();
        var personOne = await fixture.PersonOneAsync();
        var personTwo = await fixture.PersonTwoAsync();
        var created = await notes.AddNoteAsync(Note.Create(
            "Entered for the wrong client.",
            DateTime.Today.AddDays(-1),
            NoteStatus.Pending,
            15,
            personOne.Id,
            null,
            NoteType.Contact));
        var loaded = Assert.Single(await notes.GetAllByPersonAsync(personOne.Id),
            candidate => candidate.Id == created.Id);
        var panel = fixture.NoteEntry(notes: notes);
        panel.SetPeople([personOne, personTwo]);
        panel.EnterEditMode(loaded);
        panel.NoteReassignmentConfirmationRequested += (_, confirmation) =>
            confirmation.Confirmed = true;

        panel.SelectedPerson = personTwo;
        await panel.SubmitNoteCommand.ExecuteAsync(null);

        Assert.DoesNotContain(await notes.GetAllByPersonAsync(personOne.Id),
            candidate => candidate.Id == created.Id);
        var moved = Assert.Single(await notes.GetAllByPersonAsync(personTwo.Id),
            candidate => candidate.Id == created.Id);
        Assert.Equal("Entered for the wrong client.", moved.Narrative);
        Assert.False(panel.IsEditing);
        Assert.Equal(personTwo.Id, panel.SelectedPerson?.Id);
    }
}
