using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Models;
using Sati.Services.LocalAi;
using Xunit;

namespace Sati.Tests;

public sealed class LocalAiConsumerIsolationTests
{
    [Fact]
    public void ConsumerSessionBoundaryRequiresResetOnlyWhenTheConsumerChanges()
    {
        var boundary = new ConsumerSessionBoundary();

        Assert.False(boundary.RequiresReset(personId: 7));
        boundary.Record(7);

        Assert.False(boundary.RequiresReset(7));
        boundary.Record(7);

        Assert.True(boundary.RequiresReset(9));
        boundary.Record(9);

        Assert.False(boundary.RequiresReset(9));

        boundary.Invalidate();
        Assert.False(boundary.RequiresReset(9));
    }

    [Fact]
    public void ConsumerSessionBoundaryRejectsAnInvalidPersonId()
    {
        var boundary = new ConsumerSessionBoundary();
        Assert.Throws<ArgumentOutOfRangeException>(() => boundary.RequiresReset(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => boundary.RequiresReset(-1));
    }

    [Fact]
    public async Task ClientAiContextReturnsOnlyTheSelectedIdentityAndNoHistoricalRecordText()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var service = new ClientAiContextService(fixture.Factory, SessionFor(fixture.CaseManager));

        var contextForA = await service.BuildAsync(fixture.PersonAId);
        var contextForB = await service.BuildAsync(fixture.PersonBId);

        Assert.Equal(fixture.PersonAId, contextForA.PersonId);
        Assert.Equal("Alpha", contextForA.ConsumerFirstName);
        Assert.Equal(fixture.PersonBId, contextForB.PersonId);
        Assert.Equal("Beta", contextForB.ConsumerFirstName);
        Assert.All(contextForA.Sources, source =>
        {
            Assert.DoesNotContain(ContextFixture.MarkerA, source.Description);
            Assert.DoesNotContain(ContextFixture.MarkerB, source.Description);
        });
    }

    [Fact]
    public async Task ClientAiContextRefusesAConsumerNotAssignedToTheRequestingUser()
    {
        await using var fixture = await ContextFixture.CreateAsync();
        var service = new ClientAiContextService(fixture.Factory, SessionFor(fixture.CaseManager));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.BuildAsync(fixture.ForeignPersonId));
    }

    private static ISessionService SessionFor(User user)
    {
        var session = new SessionService();
        session.SetUser(user);
        return session;
    }

    private sealed class ContextFixture : IAsyncDisposable
    {
        public const string MarkerA = "XYZZY-MARKER-A-7f3c";
        public const string MarkerB = "PLUGH-MARKER-B-91ae";

        private readonly SqliteConnection _connection;

        private ContextFixture(SqliteConnection connection, DbContextOptions<SatiContext> options)
        {
            _connection = connection;
            Factory = new TestContextFactory(options);
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public User CaseManager { get; private set; } = null!;
        public int PersonAId { get; private set; }
        public int PersonBId { get; private set; }
        public int ForeignPersonId { get; private set; }

        public static async Task<ContextFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>()
                .UseSqlite(connection)
                .Options;
            var fixture = new ContextFixture(connection, options);
            await fixture.SeedAsync();
            return fixture;
        }

        private async Task SeedAsync()
        {
            await using var db = Factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            db.Agencies.Add(new Agency { Id = 201, Name = "Isolation Test Agency" });

            CaseManager = User.Create(2101, "isolation-case-manager", "Isolation Case Manager", "hash", "salt", UserRole.CaseManager, null, 201);
            var otherCaseManager = User.Create(2102, "isolation-other-case-manager", "Other Case Manager", "hash", "salt", UserRole.CaseManager, null, 201);
            db.Users.AddRange(CaseManager, otherCaseManager);

            var personA = CreatePerson(CaseManager.Id, 201, "Alpha");
            var personB = CreatePerson(CaseManager.Id, 201, "Beta");
            var foreignPerson = CreatePerson(otherCaseManager.Id, 201, "Gamma");
            db.People.AddRange(personA, personB, foreignPerson);
            await db.SaveChangesAsync();
            PersonAId = personA.Id;
            PersonBId = personB.Id;
            ForeignPersonId = foreignPerson.Id;

            db.Notes.AddRange(
                CreateNote(personA, 201, $"Visited the consumer at home. {MarkerA}"),
                CreateNote(personB, 201, $"Visited the consumer at home. {MarkerB}"));
            await db.SaveChangesAsync();
        }

        private static Person CreatePerson(int userId, int agencyId, string firstName)
        {
            var person = Person.CreatePerson(
                userId,
                firstName,
                "Isolation",
                string.Empty,
                new DateTime(1990, 1, 1),
                DateTime.Today.AddYears(-1),
                WaiverType.Section21,
                new Settings());
            person.AgencyId = agencyId;
            return person;
        }

        private static Note CreateNote(Person person, int agencyId, string narrative)
        {
            var note = Note.Create(
                narrative,
                DateTime.Today,
                NoteStatus.Logged,
                30,
                person.Id,
                noteType: NoteType.Visit);
            note.Person = person;
            note.AgencyId = agencyId;
            return note;
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class TestContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);

        public Task<SatiContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
