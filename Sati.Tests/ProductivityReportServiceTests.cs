using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sati.Data;
using Sati.Models;
using System.Data.Common;
using Xunit;

namespace Sati.Tests;

public sealed class ProductivityReportServiceTests
{
    [Fact]
    public async Task ReportReturnsOnlyLoggedAndApprovedUnitsInTheRequestedWindow()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new ProductivityReportService(fixture.Factory, fixture.Session);
        fixture.Commands.Clear();

        var months = await service.GetUnitsAsync(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 31));

        var july = Assert.Single(months);
        Assert.Equal(2026, july.Year);
        Assert.Equal(7, july.Month);
        Assert.Equal(6, july.Units);
        Assert.Contains("EventDate", fixture.Commands.LastReaderCommand);
        Assert.Contains("Minutes", fixture.Commands.LastReaderCommand);
        Assert.DoesNotContain(
            "Narrative",
            fixture.Commands.LastReaderCommand,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportRequiresASignedInUser()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new ProductivityReportService(fixture.Factory, new SessionService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetUnitsAsync(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 31)));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            IDbContextFactory<SatiContext> factory,
            SessionService session,
            CommandCapture commands)
        {
            _connection = connection;
            Factory = factory;
            Session = session;
            Commands = commands;
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public SessionService Session { get; }
        public CommandCapture Commands { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var commands = new CommandCapture();
            var options = new DbContextOptionsBuilder<SatiContext>()
                .UseSqlite(connection)
                .AddInterceptors(commands)
                .Options;
            var factory = new ContextFactory(options);
            await using var context = factory.CreateDbContext();
            await context.Database.EnsureCreatedAsync();

            var actor = User.Create(
                801, "statistics-owner", "Statistics Owner", "hash", "salt",
                UserRole.CaseManager, null, 1);
            var other = User.Create(
                802, "statistics-peer", "Statistics Peer", "hash", "salt",
                UserRole.CaseManager, null, 1);
            context.Users.AddRange(actor, other);

            var settings = new Settings();
            var ownPerson = Person.CreatePerson(
                actor.Id, "Own", "Consumer", string.Empty,
                new DateTime(1990, 1, 1), new DateTime(2025, 1, 1),
                WaiverType.Section21, settings);
            ownPerson.AgencyId = 1;
            var otherPerson = Person.CreatePerson(
                other.Id, "Other", "Consumer", string.Empty,
                new DateTime(1990, 1, 1), new DateTime(2025, 1, 1),
                WaiverType.Section21, settings);
            otherPerson.AgencyId = 1;
            var mismatchedPerson = Person.CreatePerson(
                actor.Id, "Mismatched", "Tenant", string.Empty,
                new DateTime(1990, 1, 1), new DateTime(2025, 1, 1),
                WaiverType.Section21, settings);
            mismatchedPerson.AgencyId = 2;
            context.People.AddRange(ownPerson, otherPerson, mismatchedPerson);
            await context.SaveChangesAsync();

            var mismatchedNoteAgency =
                NoteFor(ownPerson.Id, NoteStatus.Logged, new DateTime(2026, 7, 7), 60);
            mismatchedNoteAgency.AgencyId = 2;
            var mismatchedPersonAgency =
                NoteFor(mismatchedPerson.Id, NoteStatus.Logged, new DateTime(2026, 7, 8), 60);
            context.Notes.AddRange(
                NoteFor(ownPerson.Id, NoteStatus.Logged, new DateTime(2026, 7, 3), 60),
                NoteFor(ownPerson.Id, NoteStatus.Approved, new DateTime(2026, 7, 4), 16),
                NoteFor(ownPerson.Id, NoteStatus.Pending, new DateTime(2026, 7, 5), 60),
                NoteFor(ownPerson.Id, NoteStatus.Logged, new DateTime(2026, 8, 1), 60),
                NoteFor(otherPerson.Id, NoteStatus.Logged, new DateTime(2026, 7, 6), 60),
                mismatchedNoteAgency,
                mismatchedPersonAgency);
            await context.SaveChangesAsync();

            var session = new SessionService();
            session.SetUser(actor);
            return new Fixture(connection, factory, session, commands);
        }

        private static Note NoteFor(
            int personId,
            NoteStatus status,
            DateTime date,
            int minutes)
        {
            var note = Note.Create("Narrative must not be needed by statistics.", date, status, minutes, personId);
            note.AgencyId = 1;
            return note;
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }

    public sealed class CommandCapture : DbCommandInterceptor
    {
        public string LastReaderCommand { get; private set; } = string.Empty;

        public void Clear() => LastReaderCommand = string.Empty;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            LastReaderCommand = command.CommandText;
            return ValueTask.FromResult(result);
        }
    }
}
