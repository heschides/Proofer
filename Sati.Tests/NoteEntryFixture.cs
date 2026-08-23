using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services.LocalAi;
using Sati.ViewModels;
using Sati.ViewModels.Children;

namespace Sati.Tests;

// Shared in-memory fixture for the note-entry module and its two hosts. Seeds two
// agencies, two case managers and two clients on a SQLite connection that lives
// only as long as the fixture, so every test starts from the same known caseload
// without touching a real database.

internal sealed class NoteEntryFixture : IAsyncDisposable
{
    private const int AgencyOne = 201;
    private const int AgencyTwo = 202;

    private readonly SqliteConnection _connection;

    private NoteEntryFixture(SqliteConnection connection, DbContextOptions<SatiContext> options) =>
        (_connection, Factory) = (connection, new NoteEntryContextFactory(options));

    public IDbContextFactory<SatiContext> Factory { get; }
    public User CaseManagerOne { get; private set; } = null!;
    public User CaseManagerTwo { get; private set; } = null!;
    public int PersonOneId { get; private set; }
    public int PersonTwoId { get; private set; }

    public static async Task<NoteEntryFixture> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
        var fixture = new NoteEntryFixture(connection, options);
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

    public async Task<Person> PersonTwoAsync()
    {
        await using var db = Factory.CreateDbContext();
        return await db.People.AsNoTracking().SingleAsync(x => x.Id == PersonTwoId);
    }

    /// <summary>
    /// The note-entry module wired to this fixture's database as Case Manager
    /// One. The validation dialog factory throws by design — a test that
    /// reaches it is asserting about validation, and the throw proves the write
    /// never happened. <paramref name="discardAnswer"/> stands in for the
    /// confirmation window: null means the test expects never to be asked.
    /// </summary>
    public NoteEntryViewModel NoteEntry(
        bool aiEnabled = false,
        IPersonService? people = null,
        IClientAiContextService? aiContext = null,
        ICaseNoteFormatter? formatter = null,
        bool? discardAnswer = null,
        IPersonContactService? contacts = null,
        INoteService? notes = null) => new(
        notes ?? new NoteService(Factory, SessionFor(CaseManagerOne)),
        people ?? PeopleAs(CaseManagerOne),
        new StubSettingsService(),
        SessionFor(CaseManagerOne),
        contacts ?? new StubPersonContactService(),
        aiContext ?? new StubClientAiContextService(),
        formatter ?? new StubCaseNoteFormatter(aiEnabled),
        _ => throw new NotSupportedException("No dialog is expected in this test."),
        (_, _) => discardAnswer
            ?? throw new NotSupportedException("No discard prompt is expected in this test."));

    public NotesWindowViewModel NotesWindow(bool? discardAnswer = null) => new(
        PeopleAs(CaseManagerOne),
        SessionFor(CaseManagerOne),
        new NoteService(Factory, SessionFor(CaseManagerOne)),
        NoteEntry(discardAnswer: discardAnswer));

    /// <summary>
    /// A second note service on the same database as Case Manager One — stands in
    /// for another session, or for a supervisor, changing a note out from under a
    /// panel that already has it on screen.
    /// </summary>
    public INoteService NotesFromAnotherSession() =>
        new NoteService(Factory, SessionFor(CaseManagerOne));

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
        var secondPerson = Person.CreatePerson(CaseManagerOne.Id, "Second", "Person", string.Empty,
            new DateTime(1991, 1, 1), null, WaiverType.Section21, new Settings());
        secondPerson.AgencyId = AgencyOne;
        secondPerson.Gender = Gender.Unknown;
        db.People.AddRange(person, secondPerson);

        await db.SaveChangesAsync();
        PersonOneId = person.Id;
        PersonTwoId = secondPerson.Id;
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    internal sealed class NoteEntryContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}

internal sealed class StubSettingsService : ISettingsService
{
    public Task<Settings> LoadAsync() => Task.FromResult(new Settings());
    public Task SaveAsync(Settings settings) => Task.CompletedTask;
}

// Returns a fixed roster. The default is empty; a test that needs the attendee
// checkboxes to actually render supplies names.
internal sealed class StubPersonContactService(params string[] contactNames) : IPersonContactService
{
    public Task<List<PersonContact>> GetActiveByPersonAsync(int personId) =>
        Task.FromResult(contactNames.Select((name, index) => new PersonContact
        {
            PersonId = personId,
            FirstName = name,
            LastName = "Contact",
            Relationship = "Guardian",
            Kind = PersonContactKind.Personal
        }).ToList());

    public Task<PersonContact> SaveAsync(PersonContact contact) => throw new NotSupportedException();
    public Task ArchiveAsync(int contactId) => throw new NotSupportedException();
}

internal sealed class StubClientAiContextService : IClientAiContextService
{
    public Task<ClientAiContext> BuildAsync(
        int personId,
        CancellationToken cancellationToken = default) => Task.FromResult(new ClientAiContext(
            personId,
            personId.ToString(),
            [new ClientAiContextSource("Scope", "Selected client identity only; no prior records")]));
}

internal sealed class StubCaseNoteFormatter(bool enabled) : ICaseNoteFormatter
{
    public bool IsEnabled { get; } = enabled;
    public int MaxInputWords => 400;

    public Task<CaseNoteFormattingResult> FormatAsync(
        CaseNoteFormattingRequest request,
        IProgress<CaseNoteFormattingProgress>? progress = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

