using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class ProviderDirectoryContactServiceTests
{
    private const int AgencyOne = 101;
    private const int AgencyTwo = 102;

    [Fact]
    public async Task CaseManagerCanAddAndCorrectNamedContactsWithNormalizedValues()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = await fixture.AddProviderAsync(AgencyOne, "Clinic");
        var contact = new ProviderContact
        {
            ProviderId = provider.Id,
            Name = "  Jamie Referral  ",
            Role = "  Referrals  ",
            Email = "  jamie@example.test  "
        };

        var saved = await fixture.CaseManagerService.SaveContactAsync(contact);
        saved.Phone = "  207-555-0100  ";
        await fixture.CaseManagerService.SaveContactAsync(saved);

        var stored = Assert.Single(await fixture.CaseManagerService.GetContactsAsync(provider.Id));
        Assert.Equal("Jamie Referral", stored.Name);
        Assert.Equal("Referrals", stored.Role);
        Assert.Equal("jamie@example.test", stored.Email);
        Assert.Equal("207-555-0100", stored.Phone);
    }

    [Fact]
    public async Task PromotingAContactDemotesThePreviousPrimary()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = await fixture.AddProviderAsync(AgencyOne, "Clinic");
        await fixture.CaseManagerService.SaveContactAsync(new ProviderContact
        {
            ProviderId = provider.Id,
            Name = "First",
            IsPrimary = true
        });
        await fixture.CaseManagerService.SaveContactAsync(new ProviderContact
        {
            ProviderId = provider.Id,
            Name = "Second",
            IsPrimary = true
        });

        var contacts = await fixture.CaseManagerService.GetContactsAsync(provider.Id);
        Assert.True(contacts.Single(contact => contact.Name == "Second").IsPrimary);
        Assert.False(contacts.Single(contact => contact.Name == "First").IsPrimary);
    }

    [Fact]
    public async Task InvalidContactIsRejectedBeforeAnyWrite()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = await fixture.AddProviderAsync(AgencyOne, "Clinic");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CaseManagerService.SaveContactAsync(new ProviderContact
            {
                ProviderId = provider.Id,
                Name = "Referral",
                Email = "not-an-email"
            }));

        Assert.Contains("valid email", error.Message);
        Assert.Empty(await fixture.CaseManagerService.GetContactsAsync(provider.Id));
    }

    [Fact]
    public async Task ProviderAndContactIdsCannotCrossTheAgencyBoundary()
    {
        await using var fixture = await Fixture.CreateAsync();
        var foreign = await fixture.AddProviderAsync(AgencyTwo, "Foreign Clinic");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CaseManagerService.GetContactsAsync(foreign.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CaseManagerService.SaveContactAsync(new ProviderContact
            {
                ProviderId = foreign.Id,
                Name = "Foreign contact"
            }));
    }

    [Fact]
    public async Task ContactMustBelongToTheProviderNamedForRemoval()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.AddProviderAsync(AgencyOne, "First Clinic");
        var second = await fixture.AddProviderAsync(AgencyOne, "Second Clinic");
        var contact = await fixture.CaseManagerService.SaveContactAsync(new ProviderContact
        {
            ProviderId = first.Id,
            Name = "Keep me"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CaseManagerService.RemoveContactAsync(second.Id, contact.Id));

        Assert.Single(await fixture.CaseManagerService.GetContactsAsync(first.Id));
    }

    [Fact]
    public async Task CaseManagerMayMaintainButNotDeleteTheSharedProviderEntry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = await fixture.CaseManagerService.AddAsync(new Provider
        {
            Type = ProviderType.Other,
            Name = "Case Manager Entry"
        });
        provider.Name = "Corrected Entry";
        await fixture.CaseManagerService.UpdateAsync(provider);

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CaseManagerService.DeleteAsync(provider));

        Assert.Contains("Only an agency Admin", error.Message);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Equal("Corrected Entry", (await db.Providers.SingleAsync()).Name);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            IDbContextFactory<SatiContext> factory,
            ProviderService caseManagerService)
        {
            _connection = connection;
            Factory = factory;
            CaseManagerService = caseManagerService;
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public ProviderService CaseManagerService { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
            var factory = new ContextFactory(options);
            var user = User.Create(
                801, "directory-cm", "Directory CM", "hash", "salt",
                UserRole.CaseManager, null, AgencyOne);
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            db.Agencies.AddRange(
                new Agency { Id = AgencyOne, Name = "Agency One" },
                new Agency { Id = AgencyTwo, Name = "Agency Two" });
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var session = new SessionService();
            session.SetUser(user);
            return new Fixture(connection, factory, new ProviderService(factory, session));
        }

        public async Task<Provider> AddProviderAsync(int agencyId, string name)
        {
            await using var db = Factory.CreateDbContext();
            var provider = new Provider { AgencyId = agencyId, Type = ProviderType.Other, Name = name };
            db.Providers.Add(provider);
            await db.SaveChangesAsync();
            return provider;
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
