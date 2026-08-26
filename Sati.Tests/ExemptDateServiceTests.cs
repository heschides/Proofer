using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class ExemptDateServiceTests
{
    [Fact]
    public async Task ACaseManagerCannotReadOrCreateAnotherUsersExemptDates()
    {
        await using var fixture = await ExemptDateFixture.CreateAsync();
        var service = new ExemptDateService(fixture.Factory, fixture.Session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetByYearAsync(fixture.OtherUser.Id, 2026));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.AddAsync(fixture.OtherUser.Id, new DateTime(2026, 8, 12)));
    }

    [Fact]
    public async Task ACaseManagerCannotRemoveAnotherUsersExemptDate()
    {
        await using var fixture = await ExemptDateFixture.CreateAsync();
        var service = new ExemptDateService(fixture.Factory, fixture.Session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RemoveAsync(fixture.OtherExemptDateId));

        await using var verification = fixture.Factory.CreateDbContext();
        Assert.True(await verification.ExemptDates.AnyAsync(
            date => date.Id == fixture.OtherExemptDateId));
    }

    [Fact]
    public async Task ACaseManagerCanManageTheirOwnExemptDate()
    {
        await using var fixture = await ExemptDateFixture.CreateAsync();
        var service = new ExemptDateService(fixture.Factory, fixture.Session);
        var date = new DateTime(2026, 8, 12, 15, 30, 0);

        var added = await service.AddAsync(fixture.Actor.Id, date);
        var loaded = await service.GetByYearAsync(fixture.Actor.Id, date.Year);

        Assert.Equal(date.Date, added.Date);
        Assert.Contains(loaded, candidate => candidate.Id == added.Id);

        await service.RemoveAsync(added.Id);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.False(await verification.ExemptDates.AnyAsync(
            candidate => candidate.Id == added.Id));
    }

    private sealed class ExemptDateFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ExemptDateFixture(
            SqliteConnection connection,
            IDbContextFactory<SatiContext> factory,
            ISessionService session,
            User actor,
            User otherUser,
            int otherExemptDateId)
        {
            _connection = connection;
            Factory = factory;
            Session = session;
            Actor = actor;
            OtherUser = otherUser;
            OtherExemptDateId = otherExemptDateId;
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public ISessionService Session { get; }
        public User Actor { get; }
        public User OtherUser { get; }
        public int OtherExemptDateId { get; }

        public static async Task<ExemptDateFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>()
                .UseSqlite(connection)
                .Options;
            var factory = new ContextFactory(options);
            await using var context = factory.CreateDbContext();
            await context.Database.EnsureCreatedAsync();

            var actor = User.Create(
                701, "calendar-owner", "Calendar Owner", "hash", "salt",
                UserRole.CaseManager, null, 1);
            var other = User.Create(
                702, "calendar-peer", "Calendar Peer", "hash", "salt",
                UserRole.CaseManager, null, 1);
            context.Users.AddRange(actor, other);
            var foreignDate = new ExemptDate
            {
                UserId = other.Id,
                Date = new DateTime(2026, 8, 13)
            };
            context.ExemptDates.Add(foreignDate);
            await context.SaveChangesAsync();
            var session = new SessionService();
            session.SetUser(actor);

            return new ExemptDateFixture(
                connection, factory, session, actor, other, foreignDate.Id);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
