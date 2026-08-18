using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services.LocalAi;
using Sati.ViewModels.Children;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The Reminder note type. A reminder is a stamped entry at the top of a client's
/// journal, NOT service documentation: it creates no note, carries no status,
/// minutes, or service date, and so cannot reach supervisory review or billing.
/// The API mirrors the write itself; see JournalReminderApiTests.
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
        await using var fixture = await ReminderFixture.CreateAsync();
        var people = fixture.PeopleAs(fixture.CaseManagerOne);
        await people.SaveJournalAsync(fixture.PersonOneId, "Handwritten line.");

        var result = await people.AddJournalReminderAsync(fixture.PersonOneId, "Send the release form.");

        var stored = await people.GetJournalAsync(fixture.PersonOneId);
        Assert.Equal(stored, result.Journal);
        // The local path is the writer itself, so nothing fell back.
        Assert.False(result.UsedLegacyJournalWrite);
        Assert.Contains(JournalEntry.ReminderLabel, stored);
        Assert.Contains("Send the release form.", stored);
        Assert.EndsWith("Handwritten line.", stored);
    }

    [Fact]
    public async Task AReminderCannotBeWrittenToAnotherAgencysClient()
    {
        await using var fixture = await ReminderFixture.CreateAsync();
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
        await using var fixture = await ReminderFixture.CreateAsync();

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
    public async Task SelectingReminderDisablesTheServiceFieldsAndClearsWhatWasInThem()
    {
        await using var fixture = await ReminderFixture.CreateAsync();
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
        await using var fixture = await ReminderFixture.CreateAsync();
        var viewModel = fixture.NoteEntry();
        viewModel.SelectedPerson = await fixture.PersonOneAsync();

        viewModel.SelectedNoteType = NoteType.Reminder;
        viewModel.SelectedNoteType = NoteType.Contact;

        Assert.False(viewModel.IsReminderNote);
        Assert.True(viewModel.AreNoteFieldsEnabled);
        Assert.Equal("NARRATIVE", viewModel.NarrativeLabel);
    }

    [Fact]
    public async Task SavingAReminderWritesTheJournalAndCreatesNoNote()
    {
        await using var fixture = await ReminderFixture.CreateAsync();
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
        await using var fixture = await ReminderFixture.CreateAsync();
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
        await using var fixture = await ReminderFixture.CreateAsync();
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

    /// <summary>
    /// When the write had to fall back because the server is older than this
    /// client, that fact reaches the host, which is what puts it in front of the
    /// user instead of leaving a silent downgrade.
    /// </summary>
    [Fact]
    public async Task AFallbackWriteIsReportedToTheHost()
    {
        await using var fixture = await ReminderFixture.CreateAsync();
        var viewModel = fixture.NoteEntry(people: new LegacyWritePersonService());
        viewModel.SelectedPerson = await fixture.PersonOneAsync();
        viewModel.SelectedNoteType = NoteType.Reminder;
        viewModel.Narrative = "Reminder text";

        JournalReminderAddedEventArgs? announced = null;
        viewModel.ReminderAdded += (s, e) => announced = e;

        await viewModel.SubmitNoteCommand.ExecuteAsync(null);

        Assert.NotNull(announced);
        Assert.True(announced!.UsedLegacyJournalWrite);
        Assert.Equal("written the older way", announced.Journal);
    }

    [Fact]
    public async Task TheLocalAiDraftingCommandIsUnavailableForAReminder()
    {
        await using var fixture = await ReminderFixture.CreateAsync();
        var viewModel = fixture.NoteEntry(aiEnabled: true);
        viewModel.SelectedPerson = await fixture.PersonOneAsync();
        viewModel.Narrative = "rough text";

        viewModel.SelectedNoteType = NoteType.Contact;
        Assert.True(viewModel.FormatNarrativeWithAiCommand.CanExecute(null));

        viewModel.SelectedNoteType = NoteType.Reminder;
        Assert.False(viewModel.FormatNarrativeWithAiCommand.CanExecute(null));
    }

    // -------------------------------------------------------------------------
    // Fixture
    // -------------------------------------------------------------------------

    private sealed class ReminderFixture : IAsyncDisposable
    {
        private const int AgencyOne = 201;
        private const int AgencyTwo = 202;

        private readonly SqliteConnection _connection;

        private ReminderFixture(SqliteConnection connection, DbContextOptions<SatiContext> options) =>
            (_connection, Factory) = (connection, new ReminderContextFactory(options));

        public IDbContextFactory<SatiContext> Factory { get; }
        public User CaseManagerOne { get; private set; } = null!;
        public User CaseManagerTwo { get; private set; } = null!;
        public int PersonOneId { get; private set; }

        public static async Task<ReminderFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
            var fixture = new ReminderFixture(connection, options);
            await fixture.SeedAsync();
            return fixture;
        }

        public IPersonService PeopleAs(User user) =>
            new PersonService(Factory, new StubSettingsService(), SessionFor(user));

        public async Task<Person> PersonOneAsync()
        {
            await using var db = Factory.CreateDbContext();
            return await db.People.AsNoTracking().SingleAsync(x => x.Id == PersonOneId);
        }

        /// <summary>
        /// The note-entry module wired to this fixture's database as Case Manager
        /// One. The validation dialog factory throws by design — a test that
        /// reaches it is asserting about validation, and the throw proves the write
        /// never happened.
        /// </summary>
        public NoteEntryViewModel NoteEntry(bool aiEnabled = false, IPersonService? people = null) => new(
            new NoteService(Factory, SessionFor(CaseManagerOne)),
            people ?? PeopleAs(CaseManagerOne),
            new StubSettingsService(),
            SessionFor(CaseManagerOne),
            new StubPersonContactService(),
            new StubClientAiContextService(),
            new StubCaseNoteFormatter(aiEnabled),
            _ => throw new NotSupportedException("No dialog is expected in this test."));

        private static ISessionService SessionFor(User user)
        {
            var session = new SessionService();
            session.SetUser(user);
            return session;
        }

        private async Task SeedAsync()
        {
            await using var db = Factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();

            db.Agencies.AddRange(
                new Agency { Id = AgencyOne, Name = "Agency One" },
                new Agency { Id = AgencyTwo, Name = "Agency Two" });

            CaseManagerOne = User.Create(31, "cm-one", "Case Manager One", "hash", "salt",
                UserRole.CaseManager, null, AgencyOne);
            CaseManagerTwo = User.Create(32, "cm-two", "Case Manager Two", "hash", "salt",
                UserRole.CaseManager, null, AgencyTwo);
            db.Users.AddRange(CaseManagerOne, CaseManagerTwo);

            var person = Person.CreatePerson(CaseManagerOne.Id, "Journal", "Person", string.Empty,
                new DateTime(1990, 1, 1), null, WaiverType.Section21, new Settings());
            person.AgencyId = AgencyOne;
            person.Gender = Gender.Unknown;
            db.People.Add(person);

            await db.SaveChangesAsync();
            PersonOneId = person.Id;
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

        private sealed class ReminderContextFactory(DbContextOptions<SatiContext> options)
            : IDbContextFactory<SatiContext>
        {
            public SatiContext CreateDbContext() => new(options);
        }
    }

    /// <summary>Stands in for a Demo server that predates the journal-entries route.</summary>
    private sealed class LegacyWritePersonService : IPersonService
    {
        public Task<JournalReminderResult> AddJournalReminderAsync(int personId, string text) =>
            Task.FromResult(new JournalReminderResult("written the older way", true));

        public Task<string?> GetJournalAsync(int personId) => Task.FromResult<string?>(null);
        public Task SaveJournalAsync(int personId, string? journal) => Task.CompletedTask;
        public Task<Person> AddPersonAsync(Person person) => throw new NotSupportedException();
        public Task<Person> EditPersonAsync(Person person) => throw new NotSupportedException();
        public Task<List<Person>> GetAllPeopleAsync(int userId) => throw new NotSupportedException();
        public Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId) => throw new NotSupportedException();
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(new Settings());
        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }

    private sealed class StubPersonContactService : IPersonContactService
    {
        public Task<List<PersonContact>> GetActiveByPersonAsync(int personId) =>
            Task.FromResult(new List<PersonContact>());
        public Task<PersonContact> SaveAsync(PersonContact contact) => throw new NotSupportedException();
        public Task ArchiveAsync(int contactId) => throw new NotSupportedException();
    }

    private sealed class StubClientAiContextService : IClientAiContextService
    {
        public Task<ClientAiContext> BuildAsync(
            int personId,
            int requestingUserId,
            string roughNarrative,
            int? excludedNoteId = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubCaseNoteFormatter(bool enabled) : ICaseNoteFormatter
    {
        public bool IsEnabled { get; } = enabled;
        public int MaxInputWords => 400;

        public Task<CaseNoteFormattingResult> FormatAsync(
            CaseNoteFormattingRequest request,
            IProgress<CaseNoteFormattingProgress>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
