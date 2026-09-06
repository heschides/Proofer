using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services.LocalAi;
using Xunit;

namespace Sati.Tests;

public sealed class SuggestedFollowUpTests
{
    [Fact]
    public async Task AcceptAppendsOneExplicitFollowUpThatTheDraftCompilerRecognizes()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var dueDate = new DateTime(2026, 9, 12);
        var panel = fixture.NoteEntry(upcomingEvents: new StubUpcomingEventService(
            new UpcomingEvent
            {
                ClientName = "Journal Person",
                Title = "Q3 Review — Journal Person",
                Date = dueDate,
                Kind = UpcomingEventKind.OpenReview
            }));
        await panel.InitializeAsync();

        panel.SelectedPerson = await fixture.PersonOneAsync();

        Assert.True(panel.IsSuggestedFollowUpVisible);
        Assert.Equal("Q3 Review — Journal Person, due 9/12/26", panel.SuggestedFollowUpText);
        Assert.True(string.IsNullOrEmpty(panel.Narrative));

        panel.Narrative = "CCM discussed transportation options.";
        panel.AcceptSuggestedFollowUpCommand.Execute(null);
        panel.AcceptSuggestedFollowUpCommand.Execute(null);

        var expectedLine = "Follow-up: Q3 Review — Journal Person due 9/12/26.";
        Assert.Equal(
            $"CCM discussed transportation options.{Environment.NewLine}{expectedLine}",
            panel.Narrative);
        Assert.False(panel.AcceptSuggestedFollowUpCommand.CanExecute(null));

        var snapshot = CaseNoteFactCompiler.Build(
            fixture.PersonOneId,
            panel.Narrative!,
            NoteType.Contact,
            null,
            "Case Manager One",
            "Journal",
            null);

        Assert.Contains(snapshot.Facts, fact =>
            fact.Text == expectedLine &&
            fact.Usage.HasFlag(CaseNoteFactUsage.FollowUp));
    }

    [Fact]
    public async Task ExistingFollowUpDisablesSuggestionInsteadOfAddingASecondOne()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry(upcomingEvents: EventsDueOn(new DateTime(2026, 9, 12)));
        await panel.InitializeAsync();
        panel.SelectedPerson = await fixture.PersonOneAsync();
        panel.Narrative = "Follow-up: CCM will call Friday.";

        Assert.False(panel.AcceptSuggestedFollowUpCommand.CanExecute(null));
        Assert.Equal("A follow-up is already documented in this note.", panel.SuggestedFollowUpToolTip);

        panel.AcceptSuggestedFollowUpCommand.Execute(null);

        Assert.Equal("Follow-up: CCM will call Friday.", panel.Narrative);
    }

    [Fact]
    public async Task ReminderAndMissingSuggestionHideTheRowWithoutChangingNarrative()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry(upcomingEvents: new StubUpcomingEventService());
        await panel.InitializeAsync();
        panel.SelectedPerson = await fixture.PersonOneAsync();

        Assert.False(panel.IsSuggestedFollowUpVisible);

        var reminderPanel = fixture.NoteEntry(
            upcomingEvents: EventsDueOn(new DateTime(2026, 9, 12)));
        await reminderPanel.InitializeAsync();
        reminderPanel.SelectedPerson = await fixture.PersonOneAsync();
        reminderPanel.SelectedNoteType = NoteType.Reminder;
        reminderPanel.Narrative = "Call the guardian.";

        Assert.False(reminderPanel.IsSuggestedFollowUpVisible);
        Assert.False(reminderPanel.AcceptSuggestedFollowUpCommand.CanExecute(null));
        Assert.Equal("Call the guardian.", reminderPanel.Narrative);
    }

    [Fact]
    public async Task UpcomingEventFailureLeavesTheNotePanelUsable()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry(upcomingEvents: new ThrowingUpcomingEventService());
        await panel.InitializeAsync();

        panel.SelectedPerson = await fixture.PersonOneAsync();
        panel.Narrative = "The note remains editable.";

        Assert.False(panel.IsSuggestedFollowUpVisible);
        Assert.False(panel.AcceptSuggestedFollowUpCommand.CanExecute(null));
        Assert.Equal("The note remains editable.", panel.Narrative);
    }

    [Fact]
    public async Task StartingAnotherNoteReEnablesTheAcceptedSuggestion()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry(upcomingEvents: EventsDueOn(new DateTime(2026, 9, 12)));
        await panel.InitializeAsync();
        panel.SelectedPerson = await fixture.PersonOneAsync();
        panel.Narrative = "First note.";
        panel.AcceptSuggestedFollowUpCommand.Execute(null);

        Assert.False(panel.AcceptSuggestedFollowUpCommand.CanExecute(null));

        panel.ReturnToNewNote();

        Assert.True(panel.IsSuggestedFollowUpVisible);
        Assert.True(panel.AcceptSuggestedFollowUpCommand.CanExecute(null));
        Assert.True(string.IsNullOrEmpty(panel.Narrative));
    }

    [Fact]
    public async Task CompactHeaderReportsOpenReadyAndOverdueFormWork()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;

        var openForm = new UpcomingEvent
        {
            Date = today.AddDays(12),
            Kind = UpcomingEventKind.OpenReview,
            FormType = FormType.PCP,
            OpenDate = today.AddDays(-4),
            OpenedDate = today.AddDays(-2)
        };
        var openEvents = new StubUpcomingEventService(openForm)
        {
            // The actionable open form should outrank an earlier form whose opening
            // window has not started yet.
            NextForm = new UpcomingEvent
            {
                Date = today.AddDays(5),
                Kind = UpcomingEventKind.UpcomingForm,
                FormType = FormType.Q1R,
                OpenDate = today.AddDays(3)
            }
        };
        var openPanel = fixture.NoteEntry(upcomingEvents: openEvents);
        await openPanel.InitializeAsync();
        openPanel.SelectedPerson = await fixture.PersonOneAsync();

        Assert.Equal(
            $"OPEN: PCP was opened {today.AddDays(-2):M/d/yy} and is due {today.AddDays(12):M/d/yy}.",
            openPanel.ClientWorkStatusText);

        var readyPanel = fixture.NoteEntry(upcomingEvents: FormSuggestion(new UpcomingEvent
        {
            Date = today.AddDays(7),
            Kind = UpcomingEventKind.OpenReview,
            FormType = FormType.ComprehensiveAssessment,
            OpenDate = today.AddDays(-1)
        }));
        await readyPanel.InitializeAsync();
        readyPanel.SelectedPerson = await fixture.PersonOneAsync();

        Assert.Equal(
            $"READY TO OPEN: Comprehensive Assessment is due {today.AddDays(7):M/d/yy}.",
            readyPanel.ClientWorkStatusText);

        var overduePanel = fixture.NoteEntry(upcomingEvents: FormSuggestion(new UpcomingEvent
        {
            Date = today.AddDays(-1),
            Kind = UpcomingEventKind.LateReview,
            FormType = FormType.Reclassification,
            OpenDate = today.AddDays(-30)
        }));
        await overduePanel.InitializeAsync();
        overduePanel.SelectedPerson = await fixture.PersonOneAsync();

        Assert.Equal(
            $"OVERDUE: Reclassification needs to be opened; it was due {today.AddDays(-1):M/d/yy}.",
            overduePanel.ClientWorkStatusText);
        Assert.Contains(overduePanel.ClientWorkStatusText, overduePanel.ClientWorkStatusAutomationName);
    }

    private static IUpcomingEventService EventsDueOn(DateTime date) =>
        new StubUpcomingEventService(new UpcomingEvent
        {
            ClientName = "Journal Person",
            Title = "Q3 Review — Journal Person",
            Date = date,
            Kind = UpcomingEventKind.OpenReview
        });

    private static IUpcomingEventService FormSuggestion(UpcomingEvent nextForm) =>
        new StubUpcomingEventService { NextForm = nextForm };

    private sealed class StubUpcomingEventService(params UpcomingEvent[] events)
        : IUpcomingEventService
    {
        public UpcomingEvent? NextForm { get; init; }

        public List<UpcomingEvent> GenerateEvents(
            IEnumerable<IEventSource> people,
            Settings settings,
            DateTime? asOf = null) => [.. events];

        public UpcomingEvent? NextFormSuggestion(
            IEventSource person,
            Settings settings,
            DateTime? asOf = null) => NextForm;
    }

    private sealed class ThrowingUpcomingEventService : IUpcomingEventService
    {
        public List<UpcomingEvent> GenerateEvents(
            IEnumerable<IEventSource> people,
            Settings settings,
            DateTime? asOf = null) =>
            throw new InvalidOperationException("Synthetic upcoming-event failure.");

        public UpcomingEvent? NextFormSuggestion(
            IEventSource person,
            Settings settings,
            DateTime? asOf = null) =>
            throw new InvalidOperationException("Synthetic upcoming-event failure.");
    }
}
