using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The transitional desktop path. It repeats the affiliation rule rather than trusting
/// the API to be the only caller, so these tests exist to prove the local service refuses
/// what the API refuses — particularly a parent belonging to another agency.
/// </summary>
public sealed class ProviderAffiliationServiceTests
{
    [Fact]
    public async Task AProviderInAnotherAgencyCannotBeUsedAsAParent()
    {
        await using var fixture = await ProviderFixture.CreateAsync();
        var service = new ProviderService(fixture.Factory, fixture.Session);

        // A real, existing, correctly-tiered network — the only thing wrong with it is
        // that it belongs to a different tenant.
        var foreignNetwork = await fixture.SeedAsync(
            agencyId: 2, "Other Agency Health", MedicalProviderKind.Network);

        var attempt = new Provider
        {
            Type = ProviderType.Healthcare,
            Name = "Dr. Reed",
            MedicalKind = MedicalProviderKind.Individual,
            ParentProviderId = foreignNetwork.Id
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(attempt));

        Assert.Contains("not in this agency's provider directory", error.Message);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.False(await verification.Providers.AnyAsync(p => p.Name == "Dr. Reed"));
    }

    [Fact]
    public async Task AnExistingEntryCannotBeRepointedAtAnotherAgencysParent()
    {
        await using var fixture = await ProviderFixture.CreateAsync();
        var service = new ProviderService(fixture.Factory, fixture.Session);
        var foreignNetwork = await fixture.SeedAsync(2, "Other Agency Health", MedicalProviderKind.Network);
        var practice = await service.AddAsync(new Provider
        {
            Type = ProviderType.Healthcare,
            Name = "Coastal Women's Healthcare",
            MedicalKind = MedicalProviderKind.Practice
        });

        practice.ParentProviderId = foreignNetwork.Id;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(practice));

        Assert.Contains("not in this agency's provider directory", error.Message);
        await using var verification = fixture.Factory.CreateDbContext();
        var stored = await verification.Providers.SingleAsync(p => p.Id == practice.Id);
        Assert.Null(stored.ParentProviderId);
    }

    [Fact]
    public async Task AnAffiliationLoopIsRefused()
    {
        await using var fixture = await ProviderFixture.CreateAsync();
        var service = new ProviderService(fixture.Factory, fixture.Session);
        var upper = await service.AddAsync(Medical("MaineHealth", MedicalProviderKind.Network));
        var lower = await service.AddAsync(Medical("Maine Medical Partners", MedicalProviderKind.Network, upper.Id));

        upper.ParentProviderId = lower.Id;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(upper));

        Assert.Contains("already sits beneath this entry", error.Message);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Null((await verification.Providers.SingleAsync(p => p.Id == upper.Id)).ParentProviderId);
    }

    [Fact]
    public async Task AnIllegalTierIsRefused()
    {
        await using var fixture = await ProviderFixture.CreateAsync();
        var service = new ProviderService(fixture.Factory, fixture.Session);
        var practice = await service.AddAsync(Medical("Coastal Women's Healthcare", MedicalProviderKind.Practice));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddAsync(Medical("Maine Medical Partners", MedicalProviderKind.Network, practice.Id)));

        Assert.Contains("A network can only be affiliated with another network", error.Message);
    }

    [Fact]
    public async Task AMedicalEntryWithoutADesignationIsRefused()
    {
        await using var fixture = await ProviderFixture.CreateAsync();
        var service = new ProviderService(fixture.Factory, fixture.Session);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(new Provider
        {
            Type = ProviderType.Healthcare,
            Name = "Unlabelled Clinic"
        }));

        Assert.Contains("individual, a practice, or a network", error.Message);
    }

    [Fact]
    public async Task AWaiverEntryCannotCarryADesignationOrAParent()
    {
        await using var fixture = await ProviderFixture.CreateAsync();
        var service = new ProviderService(fixture.Factory, fixture.Session);
        var network = await service.AddAsync(Medical("MaineHealth", MedicalProviderKind.Network));

        var designation = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(new Provider
        {
            Type = ProviderType.Waiver,
            Name = "Spurwink",
            MedicalKind = MedicalProviderKind.Practice
        }));

        var parent = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(new Provider
        {
            Type = ProviderType.Waiver,
            Name = "Spurwink",
            ParentProviderId = network.Id
        }));

        Assert.Contains("Only medical providers are designated", designation.Message);
        Assert.Contains("Only medical providers can be affiliated", parent.Message);
    }

    [Fact]
    public async Task DeletingAnEntryWithAffiliatedEntriesBeneathItIsRefused()
    {
        await using var fixture = await ProviderFixture.CreateAsync();
        var service = new ProviderService(fixture.Factory, fixture.Session);
        var network = await service.AddAsync(Medical("MaineHealth", MedicalProviderKind.Network));
        await service.AddAsync(Medical("Coastal Women's Healthcare", MedicalProviderKind.Practice, network.Id));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(network));

        Assert.Contains("cannot be deleted", error.Message);
        Assert.Contains("Coastal Women's Healthcare", error.Message);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.True(await verification.Providers.AnyAsync(p => p.Id == network.Id));
    }

    [Fact]
    public async Task DeletingAnEntryWithNothingBeneathItStillSucceeds()
    {
        await using var fixture = await ProviderFixture.CreateAsync();
        var service = new ProviderService(fixture.Factory, fixture.Session);
        var network = await service.AddAsync(Medical("MaineHealth", MedicalProviderKind.Network));
        var practice = await service.AddAsync(Medical("Coastal Women's Healthcare", MedicalProviderKind.Practice, network.Id));

        await service.DeleteAsync(practice);

        await using var verification = fixture.Factory.CreateDbContext();
        Assert.False(await verification.Providers.AnyAsync(p => p.Id == practice.Id));
    }

    [Fact]
    public async Task AFourLevelHierarchyPersistsAndResolves()
    {
        await using var fixture = await ProviderFixture.CreateAsync();
        var service = new ProviderService(fixture.Factory, fixture.Session);

        var top = await service.AddAsync(Medical("MaineHealth", MedicalProviderKind.Network));
        var group = await service.AddAsync(Medical("Maine Medical Partners", MedicalProviderKind.Network, top.Id));
        var practice = await service.AddAsync(Medical("Coastal Women's Healthcare", MedicalProviderKind.Practice, group.Id));
        var clinician = await service.AddAsync(Medical("Dr. Reed", MedicalProviderKind.Individual, practice.Id));

        var directory = (await service.GetAllAsync()).ToAffiliationNodes();

        Assert.Equal(
            "Coastal Women's Healthcare · Maine Medical Partners · MaineHealth",
            ProviderAffiliation.DescribeAffiliation(clinician.Id, directory));
    }

    private static Provider Medical(string name, MedicalProviderKind kind, int? parentId = null) => new()
    {
        Type = ProviderType.Healthcare,
        Name = name,
        MedicalKind = kind,
        ParentProviderId = parentId
    };

    private sealed class ProviderFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ProviderFixture(
            SqliteConnection connection, IDbContextFactory<SatiContext> factory, ISessionService session)
        {
            _connection = connection;
            Factory = factory;
            Session = session;
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public ISessionService Session { get; }

        public static async Task<ProviderFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
            var factory = new ContextFactory(options);
            await using var context = factory.CreateDbContext();
            await context.Database.EnsureCreatedAsync();

            var actor = User.Create(
                801, "directory-admin", "Directory Admin", "hash", "salt", UserRole.Admin, null, 1);
            context.Users.Add(actor);
            await context.SaveChangesAsync();

            var session = new SessionService();
            session.SetUser(actor);
            return new ProviderFixture(connection, factory, session);
        }

        /// <summary>
        /// Inserts a row directly, so a provider can be placed in an agency the signed-in
        /// user has no access to — which the service itself would never let a caller do.
        /// </summary>
        public async Task<Provider> SeedAsync(int agencyId, string name, MedicalProviderKind kind)
        {
            await using var context = Factory.CreateDbContext();
            var provider = new Provider
            {
                AgencyId = agencyId,
                Type = ProviderType.Healthcare,
                Name = name,
                MedicalKind = kind
            };
            context.Providers.Add(provider);
            await context.SaveChangesAsync();
            return provider;
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
