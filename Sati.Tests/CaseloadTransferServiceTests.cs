using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Caseload transfer through the desktop-local EF path.
///
/// <para>
/// These duplicate the intent of <c>CaseloadTransferApiTests</c> on purpose. In local Production
/// there is no server between the view model and SQL Server — <see cref="PersonService"/> is the
/// last thing standing between a caller and <c>Person.UserId</c>. A route test proves nothing
/// about this path, and "the API checks it" is not a control that exists here.
/// </para>
///
/// <para>
/// The authorization decision itself is not restated in either place: both call
/// <see cref="CaseloadTransferRules"/>. What these tests pin is that the local service actually
/// consults it, and loads the facts it decides over from the database rather than from its
/// arguments.
/// </para>
/// </summary>
public sealed class CaseloadTransferServiceTests
{
    private const int AgencyOne = 7;
    private const int AgencyTwo = 8;
    private const int SupervisorId = 41;
    private const int SuperviseeId = 42;
    private const int UnrelatedCaseManagerId = 43;
    private const int OtherAgencyCaseManagerId = 44;
    private const int BillingOnlyId = 45;
    private const int PersonId = 900;

    [Fact]
    public async Task ASupervisorMovesAConsumerTheyHoldToTheirSupervisee()
    {
        await using var fixture = await CaseloadFixture.CreateAsync(actorId: SupervisorId);

        var ownership = await fixture.Service.TransferOwnershipAsync(PersonId, SuperviseeId, 1);

        Assert.Equal(SuperviseeId, ownership.UserId);
        Assert.True(ownership.Revision > 1);
        await fixture.AssertOwnedByAsync(PersonId, SuperviseeId);
    }

    [Fact]
    public async Task TheMoveWritesAVersionAndAnAuditEvent()
    {
        await using var fixture = await CaseloadFixture.CreateAsync(actorId: SupervisorId);

        await fixture.Service.TransferOwnershipAsync(PersonId, SuperviseeId, 1);

        await using var db = fixture.Factory.CreateDbContext();
        var versions = await db.PersonVersions.AsNoTracking()
            .Where(version => version.PersonId == PersonId)
            .ToListAsync();
        var audit = await db.AuditEvents.AsNoTracking()
            .Where(candidate => candidate.Action == LocalAuditActions.PersonReassigned)
            .ToListAsync();

        Assert.Contains(versions, version => version.ChangeKind == "Reassigned");
        var recorded = Assert.Single(audit);
        Assert.Equal(SupervisorId, recorded.ActorUserId);
        Assert.Equal(AgencyOne, recorded.AgencyId);

        // The trail names the move, never the consumer. A metadata blob that quietly grew a
        // name or a MaineCare ID is exactly what AUDIT_EVENTS.md forbids.
        Assert.Contains("previousUserId", recorded.MetadataJson);
        Assert.DoesNotContain("Consumer", recorded.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    // The local mirror of the supervision gate. Without it, any case manager could redistribute
    // consumers by calling this service directly — and in local Production, calling this service
    // directly is all the desktop ever does.
    [Fact]
    public async Task ACaseManagerCannotMoveAConsumerAtAll()
    {
        await using var fixture = await CaseloadFixture.CreateAsync(actorId: SuperviseeId);

        await Assert.ThrowsAsync<PersonValidationException>(
            () => fixture.Service.TransferOwnershipAsync(PersonId, UnrelatedCaseManagerId, 1));

        await fixture.AssertOwnedByAsync(PersonId, SupervisorId);
    }

    [Fact]
    public async Task ASupervisorCannotMoveAConsumerToACaseManagerTheyDoNotSupervise()
    {
        await using var fixture = await CaseloadFixture.CreateAsync(actorId: SupervisorId);

        await Assert.ThrowsAsync<PersonValidationException>(
            () => fixture.Service.TransferOwnershipAsync(PersonId, UnrelatedCaseManagerId, 1));

        await fixture.AssertOwnedByAsync(PersonId, SupervisorId);
    }

    // Tenant isolation on the desktop path, where nothing else enforces it.
    [Fact]
    public async Task AConsumerCannotBeMovedToAnotherAgency()
    {
        await using var fixture = await CaseloadFixture.CreateAsync(actorId: SupervisorId);

        await Assert.ThrowsAsync<PersonValidationException>(
            () => fixture.Service.TransferOwnershipAsync(PersonId, OtherAgencyCaseManagerId, 1));

        await fixture.AssertOwnedByAsync(PersonId, SupervisorId);
    }

    [Fact]
    public async Task AConsumerCannotBeMovedToSomeoneWhoCannotHoldACaseload()
    {
        await using var fixture = await CaseloadFixture.CreateAsync(actorId: SupervisorId);

        await Assert.ThrowsAsync<PersonValidationException>(
            () => fixture.Service.TransferOwnershipAsync(PersonId, BillingOnlyId, 1));

        await fixture.AssertOwnedByAsync(PersonId, SupervisorId);
    }

    [Fact]
    public async Task AMoveToAUserWhoDoesNotExistIsRefused()
    {
        await using var fixture = await CaseloadFixture.CreateAsync(actorId: SupervisorId);

        await Assert.ThrowsAsync<PersonValidationException>(
            () => fixture.Service.TransferOwnershipAsync(PersonId, 9999, 1));

        await fixture.AssertOwnedByAsync(PersonId, SupervisorId);
    }

    [Fact]
    public async Task AStaleRevisionIsRefusedRatherThanOverwriting()
    {
        await using var fixture = await CaseloadFixture.CreateAsync(actorId: SupervisorId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.TransferOwnershipAsync(PersonId, SuperviseeId, 99));

        await fixture.AssertOwnedByAsync(PersonId, SupervisorId);
    }

    // Authorization is decided before the revision token, so an unauthorized caller cannot use
    // the difference between the two failures to probe whether a record changed.
    [Fact]
    public async Task AnUnauthorizedMoveFailsOnAuthorizationEvenWithAStaleRevision()
    {
        await using var fixture = await CaseloadFixture.CreateAsync(actorId: SuperviseeId);

        await Assert.ThrowsAsync<PersonValidationException>(
            () => fixture.Service.TransferOwnershipAsync(PersonId, UnrelatedCaseManagerId, 99));
    }

    private sealed class CaseloadFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private CaseloadFixture(
            SqliteConnection connection,
            IDbContextFactory<SatiContext> factory,
            User actor)
        {
            _connection = connection;
            Factory = factory;
            var session = new SessionService();
            session.SetUser(actor);
            Service = new PersonService(factory, new TransferSettingsService(), session);
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public PersonService Service { get; }

        public static async Task<CaseloadFixture> CreateAsync(int actorId)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>()
                .UseSqlite(connection)
                .Options;
            var factory = new LocalContextFactory(options);

            var supervisor = User.Create(
                SupervisorId, "supervisor", "Supervisor", "hash", "salt",
                UserRole.Supervisor, null, AgencyOne);
            var supervisee = User.Create(
                SuperviseeId, "supervisee", "Supervisee", "hash", "salt",
                UserRole.CaseManager, SupervisorId, AgencyOne);
            // In the same agency and a real case manager, but reporting to nobody. This is what
            // makes the "not your supervisee" test load-bearing rather than incidentally denied.
            var unrelated = User.Create(
                UnrelatedCaseManagerId, "unrelated", "Unrelated", "hash", "salt",
                UserRole.CaseManager, null, AgencyOne);
            var otherAgency = User.Create(
                OtherAgencyCaseManagerId, "other-agency", "Other Agency", "hash", "salt",
                UserRole.CaseManager, SupervisorId, AgencyTwo);
            var billingOnly = User.Create(
                BillingOnlyId, "billing-only", "Billing Only", "hash", "salt",
                UserRole.CaseManager, SupervisorId, AgencyOne);
            billingOnly.Permissions = UserPermissions.Billing;

            await using (var db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
                db.Agencies.AddRange(
                    new Agency { Id = AgencyOne, Name = "Agency Seven" },
                    new Agency { Id = AgencyTwo, Name = "Agency Eight" });
                db.Users.AddRange(supervisor, supervisee, unrelated, otherAgency, billingOnly);

                var person = Person.CreatePerson(
                    SupervisorId,
                    "Imported",
                    "Consumer",
                    "Seeded for caseload transfer tests.",
                    new DateTime(1990, 1, 1),
                    null,
                    WaiverType.None,
                    new Settings());
                typeof(Person).GetProperty(nameof(Person.Id))!
                    .SetValue(person, PersonId);
                person.AgencyId = AgencyOne;
                db.People.Add(person);
                await db.SaveChangesAsync();
            }

            var actor = actorId == SupervisorId ? supervisor : supervisee;
            return new CaseloadFixture(connection, factory, actor);
        }

        public async Task AssertOwnedByAsync(int personId, int expectedUserId)
        {
            await using var db = Factory.CreateDbContext();
            var person = await db.People.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == personId);
            Assert.Equal(expectedUserId, person.UserId);
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

        private sealed class LocalContextFactory(DbContextOptions<SatiContext> options)
            : IDbContextFactory<SatiContext>
        {
            public SatiContext CreateDbContext() => new(options);
        }

        private sealed class TransferSettingsService : ISettingsService
        {
            public Task<Settings> LoadAsync() => Task.FromResult(new Settings());
            public Task SaveAsync(Settings settings) => Task.CompletedTask;
        }
    }
}
