using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The transitional desktop path for a consumer's provider list. Every method here takes a
/// caller-supplied id, so the tests that matter most are the ones proving each of them
/// re-establishes access rather than trusting what it was handed.
/// </summary>
public sealed class ConsumerProviderServiceTests
{
    [Fact]
    public async Task ACaseManagerCannotReadAnotherCaseloadsProviderList()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new ConsumerProviderService(fixture.Factory, fixture.Session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GetByPersonAsync(fixture.OtherPersonId));
    }

    [Fact]
    public async Task ACaseManagerCannotAddAProviderToAnotherCaseloadsConsumer()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new ConsumerProviderService(fixture.Factory, fixture.Session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(new PersonProvider
        {
            PersonId = fixture.OtherPersonId,
            ProviderId = fixture.ClinicianId
        }));

        await using var verification = fixture.Factory.CreateDbContext();
        Assert.False(await verification.PersonProviders.AnyAsync());
    }

    [Fact]
    public async Task ALinkIdFromAnotherConsumerCannotBeEndedByNamingYourOwnConsumer()
    {
        // The consumer is the security scope, so the row must belong to the consumer named
        // rather than the caller's link id choosing the scope it is then checked against.
        await using var fixture = await Fixture.CreateAsync();
        var service = new ConsumerProviderService(fixture.Factory, fixture.Session);
        var foreignLinkId = await fixture.SeedLinkAsync(fixture.OtherPersonId, fixture.ClinicianId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EndAsync(fixture.PersonId, foreignLinkId, new DateTime(2026, 8, 28)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RemoveAsync(fixture.PersonId, foreignLinkId));

        await using var verification = fixture.Factory.CreateDbContext();
        var untouched = await verification.PersonProviders.SingleAsync(link => link.Id == foreignLinkId);
        Assert.Null(untouched.EndDate);
    }

    [Fact]
    public async Task AProviderFromAnotherAgencyCannotBeLinkedToAConsumer()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new ConsumerProviderService(fixture.Factory, fixture.Session);
        var foreignProviderId = await fixture.SeedProviderAsync(
            agencyId: 2, "Other Agency Clinician", MedicalProviderKind.Individual);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(
            new PersonProvider { PersonId = fixture.PersonId, ProviderId = foreignProviderId }));

        Assert.Equal(ConsumerProviderRules.ProviderOutsideAgencyMessage(), error.Message);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.False(await verification.PersonProviders.AnyAsync());
    }

    [Fact]
    public async Task ASecondCurrentPrimaryCareProviderIsRefusedAndNamesTheFirst()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new ConsumerProviderService(fixture.Factory, fixture.Session);
        await service.SaveAsync(Link(fixture.PersonId, fixture.ClinicianId, isPrimaryCare: true));
        var second = await fixture.SeedProviderAsync(1, "Dr. Okafor", MedicalProviderKind.Individual);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(Link(fixture.PersonId, second, isPrimaryCare: true)));

        Assert.Contains("Dr. Reed", error.Message);
    }

    [Fact]
    public async Task APrimaryCareProviderMayBeReplacedOnceTheFirstRelationshipHasEnded()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new ConsumerProviderService(fixture.Factory, fixture.Session);
        var first = await service.SaveAsync(
            Link(fixture.PersonId, fixture.ClinicianId, isPrimaryCare: true));
        var second = await fixture.SeedProviderAsync(1, "Dr. Okafor", MedicalProviderKind.Individual);

        await service.EndAsync(fixture.PersonId, first.Id, new DateTime(2026, 8, 1));
        var replacement = await service.SaveAsync(Link(fixture.PersonId, second, isPrimaryCare: true));

        Assert.True(replacement.IsPrimaryCare);
        var all = await service.GetByPersonAsync(fixture.PersonId);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task TheSameProviderCannotAppearTwiceOnTheCurrentList()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new ConsumerProviderService(fixture.Factory, fixture.Session);
        await service.SaveAsync(Link(fixture.PersonId, fixture.ClinicianId));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(Link(fixture.PersonId, fixture.ClinicianId)));

        Assert.Contains("already on this consumer's current provider list", error.Message);
    }

    [Fact]
    public async Task AConsumerMayReturnToAProviderTheyPreviouslyLeft()
    {
        // Two rows for the same provider is correct when the first has ended: it is the only
        // way the record can show a gap and a return.
        await using var fixture = await Fixture.CreateAsync();
        var service = new ConsumerProviderService(fixture.Factory, fixture.Session);
        var first = await service.SaveAsync(Link(fixture.PersonId, fixture.ClinicianId));
        await service.EndAsync(fixture.PersonId, first.Id, new DateTime(2026, 3, 1));

        var again = await service.SaveAsync(Link(fixture.PersonId, fixture.ClinicianId));

        Assert.NotEqual(first.Id, again.Id);
        Assert.Equal(2, (await service.GetByPersonAsync(fixture.PersonId)).Count);
    }

    [Fact]
    public async Task EndingARelationshipKeepsTheRow()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new ConsumerProviderService(fixture.Factory, fixture.Session);
        var link = await service.SaveAsync(Link(fixture.PersonId, fixture.ClinicianId));

        await service.EndAsync(fixture.PersonId, link.Id, new DateTime(2026, 8, 28));

        var stored = Assert.Single(await service.GetByPersonAsync(fixture.PersonId));
        Assert.Equal(new DateTime(2026, 8, 28), stored.EndDate);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task ADirectoryEntryOnAConsumerRecordIsRefusedWithACountRatherThanNames()
    {
        await using var fixture = await Fixture.CreateAsync();
        var links = new ConsumerProviderService(fixture.Factory, fixture.Session);
        var providers = new ProviderService(fixture.Factory, fixture.AdminSession);
        await links.SaveAsync(Link(fixture.PersonId, fixture.ClinicianId));
        var clinician = (await providers.GetAllAsync()).Single(p => p.Id == fixture.ClinicianId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => providers.DeleteAsync(clinician));

        Assert.Contains("Dr. Reed", error.Message);
        Assert.Contains("1 consumer record", error.Message);
        // Who sees whom is not disclosed on a directory screen.
        Assert.DoesNotContain("Mine", error.Message);
    }

    [Fact]
    public async Task AnEndedRelationshipStillProtectsTheDirectoryEntry()
    {
        // The row still references the entry, and keeping that history readable is the
        // whole reason the row was not deleted when the relationship ended.
        await using var fixture = await Fixture.CreateAsync();
        var links = new ConsumerProviderService(fixture.Factory, fixture.Session);
        var providers = new ProviderService(fixture.Factory, fixture.AdminSession);
        var link = await links.SaveAsync(Link(fixture.PersonId, fixture.ClinicianId));
        await links.EndAsync(fixture.PersonId, link.Id, new DateTime(2026, 4, 1));
        var clinician = (await providers.GetAllAsync()).Single(p => p.Id == fixture.ClinicianId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => providers.DeleteAsync(clinician));
    }

    [Fact]
    public async Task TheDatabaseRefusesTheDeleteEvenIfTheServiceGuardIsBypassed()
    {
        // Restrict on the provider foreign key, so a path that does not go through the
        // service still cannot leave a consumer's record pointing at nothing.
        await using var fixture = await Fixture.CreateAsync();
        var service = new ConsumerProviderService(fixture.Factory, fixture.Session);
        await service.SaveAsync(Link(fixture.PersonId, fixture.ClinicianId));

        await using var context = fixture.Factory.CreateDbContext();
        var clinician = await context.Providers.SingleAsync(p => p.Id == fixture.ClinicianId);
        context.Providers.Remove(clinician);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ThePracticeAndNetworkFollowADirectoryChangeWithoutTouchingTheConsumer()
    {
        // The whole reason nothing is copied onto the link: move the clinician in the
        // directory and every consumer who names her resolves to the new practice.
        await using var fixture = await Fixture.CreateAsync();
        var links = new ConsumerProviderService(fixture.Factory, fixture.Session);
        var providers = new ProviderService(fixture.Factory, fixture.Session);
        await links.SaveAsync(Link(fixture.PersonId, fixture.ClinicianId));

        var before = ProviderAffiliation.DescribeAffiliation(
            fixture.ClinicianId, (await providers.GetAllAsync()).ToAffiliationNodes());

        var moved = await providers.AddAsync(new Provider
        {
            Type = ProviderType.Healthcare,
            Name = "InterMed",
            MedicalKind = MedicalProviderKind.Network
        });
        var newPractice = await providers.AddAsync(new Provider
        {
            Type = ProviderType.Healthcare,
            Name = "InterMed Primary Care",
            MedicalKind = MedicalProviderKind.Practice,
            ParentProviderId = moved.Id
        });
        var clinician = (await providers.GetAllAsync()).Single(p => p.Id == fixture.ClinicianId);
        clinician.ParentProviderId = newPractice.Id;
        await providers.UpdateAsync(clinician);

        var after = ProviderAffiliation.DescribeAffiliation(
            fixture.ClinicianId, (await providers.GetAllAsync()).ToAffiliationNodes());

        Assert.Equal("Coastal Women's Healthcare · MaineHealth", before);
        Assert.Equal("InterMed Primary Care · InterMed", after);

        // Nothing on the consumer's row changed, because nothing derived was ever there.
        var stored = Assert.Single(await links.GetByPersonAsync(fixture.PersonId));
        Assert.Equal(fixture.ClinicianId, stored.ProviderId);
    }

    private static PersonProvider Link(int personId, int providerId, bool isPrimaryCare = false) => new()
    {
        PersonId = personId,
        ProviderId = providerId,
        Role = "Neurologist",
        IsPrimaryCare = isPrimaryCare
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection, IDbContextFactory<SatiContext> factory, ISessionService session,
            int personId, int otherPersonId, int clinicianId)
        {
            _connection = connection;
            Factory = factory;
            Session = session;
            PersonId = personId;
            OtherPersonId = otherPersonId;
            ClinicianId = clinicianId;
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public ISessionService Session { get; }

        /// <summary>
        /// An Admin in the same agency. Removing a directory entry is Admin-only since the
        /// directory became a shared agency rolodex, so the delete-guard tests need one.
        /// </summary>
        public ISessionService AdminSession { get; private set; } = null!;

        public int PersonId { get; }
        public int OtherPersonId { get; }
        public int ClinicianId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
            var factory = new ContextFactory(options);
            await using var context = factory.CreateDbContext();
            await context.Database.EnsureCreatedAsync();

            var actor = User.Create(
                901, "provider-cm", "Provider CM", "hash", "salt", UserRole.CaseManager, null, 1);
            var peer = User.Create(
                902, "provider-peer", "Provider Peer", "hash", "salt", UserRole.CaseManager, null, 1);
            context.Users.AddRange(actor, peer);
            await context.SaveChangesAsync();

            // Rehydrate with id 0 rather than CreatePerson: the factory also generates a
            // compliance form cycle, which this fixture has no use for.
            var mine = Consumer(actor.Id, "Mine");
            var theirs = Consumer(peer.Id, "Theirs");
            context.People.AddRange(mine, theirs);

            var network = new Provider
            {
                AgencyId = 1, Type = ProviderType.Healthcare, Name = "MaineHealth",
                MedicalKind = MedicalProviderKind.Network
            };
            context.Providers.Add(network);
            await context.SaveChangesAsync();

            var practice = new Provider
            {
                AgencyId = 1, Type = ProviderType.Healthcare, Name = "Coastal Women's Healthcare",
                MedicalKind = MedicalProviderKind.Practice, ParentProviderId = network.Id
            };
            context.Providers.Add(practice);
            await context.SaveChangesAsync();

            var clinician = new Provider
            {
                AgencyId = 1, Type = ProviderType.Healthcare, Name = "Dr. Reed",
                MedicalKind = MedicalProviderKind.Individual, ParentProviderId = practice.Id
            };
            context.Providers.Add(clinician);
            await context.SaveChangesAsync();

            var session = new SessionService();
            session.SetUser(actor);

            var admin = User.Create(
                903, "provider-admin", "Provider Admin", "hash", "salt", UserRole.Admin, null, 1);
            await using (var adminContext = factory.CreateDbContext())
            {
                adminContext.Users.Add(admin);
                await adminContext.SaveChangesAsync();
            }
            var adminSession = new SessionService();
            adminSession.SetUser(admin);

            return new Fixture(connection, factory, session, mine.Id, theirs.Id, clinician.Id)
            {
                AdminSession = adminSession
            };
        }

        private static Person Consumer(int userId, string firstName)
        {
            var person = Person.Rehydrate(0, userId);
            person.FirstName = firstName;
            person.LastName = "Consumer";
            person.BirthDate = new DateTime(1990, 1, 1);
            person.AgencyId = 1;
            return person;
        }

        public async Task<int> SeedProviderAsync(int agencyId, string name, MedicalProviderKind kind)
        {
            await using var context = Factory.CreateDbContext();
            var provider = new Provider
            {
                AgencyId = agencyId, Type = ProviderType.Healthcare, Name = name, MedicalKind = kind
            };
            context.Providers.Add(provider);
            await context.SaveChangesAsync();
            return provider.Id;
        }

        /// <summary>Inserts directly, so a link can exist on a consumer the actor cannot reach.</summary>
        public async Task<int> SeedLinkAsync(int personId, int providerId)
        {
            await using var context = Factory.CreateDbContext();
            var link = new PersonProvider { PersonId = personId, ProviderId = providerId };
            context.PersonProviders.Add(link);
            await context.SaveChangesAsync();
            return link.Id;
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
