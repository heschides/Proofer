using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Models.Assessments;
using Xunit;

namespace Sati.Tests;

public sealed class ProviderDirectoryMergeTests
{
    [Fact]
    public async Task AdminMergeMovesLiveRelationshipsKeepsDocumentSnapshotAndWritesAudit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var graph = await fixture.SeedMergeGraphAsync();

        var summary = await fixture.AdminService.MergeAsync(graph.SurvivorId, graph.MergedId);

        await using var db = fixture.Factory.CreateDbContext();
        Assert.False(await db.Providers.AnyAsync(provider => provider.Id == graph.MergedId));
        var survivor = await db.Providers.SingleAsync(provider => provider.Id == graph.SurvivorId);
        Assert.Equal("1999999984", survivor.Npi);
        Assert.Equal(graph.SurvivorId,
            (await db.Providers.SingleAsync(provider => provider.Id == graph.ChildId)).ParentProviderId);
        Assert.Equal(graph.SurvivorId,
            (await db.PersonProviders.SingleAsync()).ProviderId);
        var contact = await db.ProviderContacts.SingleAsync();
        Assert.Equal(graph.SurvivorId, contact.ProviderId);
        Assert.False(contact.IsPrimary);
        Assert.Equal(graph.SurvivorId,
            (await db.Settings.SingleAsync()).DefaultPassthroughProviderId);
        Assert.Equal(graph.DocumentJson,
            (await db.ComprehensiveAssessments.SingleAsync()).DocumentJson);

        var audit = await db.AuditEvents.SingleAsync(candidate =>
            candidate.Action == "provider.merged");
        Assert.Equal(graph.SurvivorId.ToString(), audit.ResourceId);
        Assert.Contains($"\"mergedProviderId\":{graph.MergedId}", audit.MetadataJson);
        Assert.DoesNotContain("Duplicate network", audit.MetadataJson);
        Assert.Contains("Moved 1 affiliated entry", summary);
        Assert.Contains("1 consumer link", summary);
        Assert.Contains("1 contact", summary);
    }

    [Fact]
    public async Task MergeThatWouldCreateAnAffiliationLoopWritesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var parent = await fixture.AddProviderAsync("Parent", MedicalProviderKind.Network);
        var child = await fixture.AddProviderAsync("Child", MedicalProviderKind.Network, parent.Id);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.MergeAsync(child.Id, parent.Id));

        Assert.Equal(ProviderDirectoryRules.MergeWouldCreateLoopMessage, error.Message);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Equal(2, await db.Providers.CountAsync());
        Assert.Equal(parent.Id,
            (await db.Providers.SingleAsync(provider => provider.Id == child.Id)).ParentProviderId);
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task ConflictingDurableIdentifiersRefuseTheMergeBeforeAnyWrite()
    {
        await using var fixture = await Fixture.CreateAsync();
        var survivor = await fixture.AddProviderAsync(
            "Survivor", MedicalProviderKind.Individual, npi: "1999999984");
        var merged = await fixture.AddProviderAsync(
            "Merged", MedicalProviderKind.Individual, npi: "1111111111");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.MergeAsync(survivor.Id, merged.Id));

        Assert.Contains("different National Provider Identifier", error.Message);
        await fixture.AssertProvidersRemainAsync(survivor.Id, merged.Id);
    }

    [Fact]
    public async Task CurrentLinksToBothEntriesMustBeCorrectedBeforeMerge()
    {
        await using var fixture = await Fixture.CreateAsync();
        var survivor = await fixture.AddProviderAsync("Survivor", MedicalProviderKind.Individual);
        var merged = await fixture.AddProviderAsync("Merged", MedicalProviderKind.Individual);
        await fixture.AddPersonWithLinksAsync(survivor.Id, merged.Id);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.MergeAsync(survivor.Id, merged.Id));

        Assert.Contains("current links to both entries", error.Message);
        Assert.DoesNotContain("Synthetic Consumer", error.Message);
        await fixture.AssertProvidersRemainAsync(survivor.Id, merged.Id);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Equal(2, await db.PersonProviders.CountAsync());
    }

    [Fact]
    public async Task CaseManagerCannotMergeSharedDirectoryEntries()
    {
        await using var fixture = await Fixture.CreateAsync();
        var survivor = await fixture.AddProviderAsync("Survivor", MedicalProviderKind.Network);
        var merged = await fixture.AddProviderAsync("Merged", MedicalProviderKind.Network);

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CaseManagerService.MergeAsync(survivor.Id, merged.Id));

        Assert.Equal(ProviderDirectoryRules.MergeRequiresAdminMessage, error.Message);
        await fixture.AssertProvidersRemainAsync(survivor.Id, merged.Id);
    }

    [Fact]
    public async Task StaleAdminSessionIsRevalidatedAgainstTheDatabase()
    {
        await using var fixture = await Fixture.CreateAsync();
        var survivor = await fixture.AddProviderAsync("Survivor", MedicalProviderKind.Network);
        var merged = await fixture.AddProviderAsync("Merged", MedicalProviderKind.Network);
        await fixture.DemoteAdminAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.AdminService.MergeAsync(survivor.Id, merged.Id));

        await fixture.AssertProvidersRemainAsync(survivor.Id, merged.Id);
    }

    [Fact]
    public async Task ProviderFromAnotherAgencyCannotBeMergedIntoThisDirectory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var survivor = await fixture.AddProviderAsync("Survivor", MedicalProviderKind.Network);
        var foreign = await fixture.AddProviderAsync(
            "Foreign", MedicalProviderKind.Network, agencyId: Fixture.ForeignAgencyId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.MergeAsync(survivor.Id, foreign.Id));

        Assert.Contains("outside the current agency", error.Message);
        await fixture.AssertProvidersRemainAsync(survivor.Id, foreign.Id);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const int AgencyId = 710;
        public const int ForeignAgencyId = 711;
        private readonly SqliteConnection _connection;
        private readonly User _admin;
        private readonly User _caseManager;

        private Fixture(
            SqliteConnection connection,
            IDbContextFactory<SatiContext> factory,
            User admin,
            User caseManager)
        {
            _connection = connection;
            Factory = factory;
            _admin = admin;
            _caseManager = caseManager;
            AdminService = ServiceFor(admin);
            CaseManagerService = ServiceFor(caseManager);
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public ProviderService AdminService { get; }
        public ProviderService CaseManagerService { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
            var factory = new ContextFactory(options);
            var admin = User.Create(
                7101, "merge-admin", "Merge Admin", "hash", "salt", UserRole.Admin, null, AgencyId);
            var caseManager = User.Create(
                7102, "merge-cm", "Merge CM", "hash", "salt", UserRole.CaseManager, null, AgencyId);

            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            db.Agencies.AddRange(
                new Agency { Id = AgencyId, Name = "Merge Agency" },
                new Agency { Id = ForeignAgencyId, Name = "Foreign Agency" });
            db.Users.AddRange(admin, caseManager);
            await db.SaveChangesAsync();
            return new Fixture(connection, factory, admin, caseManager);
        }

        public async Task<Provider> AddProviderAsync(
            string name,
            MedicalProviderKind kind,
            int? parentId = null,
            string? npi = null,
            int agencyId = AgencyId)
        {
            await using var db = Factory.CreateDbContext();
            var provider = new Provider
            {
                AgencyId = agencyId,
                Type = ProviderType.Healthcare,
                Name = name,
                MedicalKind = kind,
                ParentProviderId = parentId,
                Npi = npi
            };
            db.Providers.Add(provider);
            await db.SaveChangesAsync();
            return provider;
        }

        public async Task<MergeGraph> SeedMergeGraphAsync()
        {
            var survivor = await AddProviderAsync("Surviving network", MedicalProviderKind.Network);
            var merged = await AddProviderAsync(
                "Duplicate network", MedicalProviderKind.Network, npi: "1999999984");
            var child = await AddProviderAsync(
                "Affiliated practice", MedicalProviderKind.Practice, merged.Id);

            await using var db = Factory.CreateDbContext();
            var person = Person.CreatePerson(
                _caseManager.Id,
                "Synthetic",
                "Consumer",
                "Merge test.",
                new DateTime(1990, 1, 1),
                null,
                WaiverType.None,
                new Settings());
            person.AgencyId = AgencyId;
            db.People.Add(person);
            await db.SaveChangesAsync();

            var documentJson = $"{{\"needs\":[{{\"providerId\":{merged.Id},\"providerNameSnapshot\":\"Duplicate network\"}}]}}";
            db.PersonProviders.Add(new PersonProvider
            {
                PersonId = person.Id,
                ProviderId = merged.Id,
                Role = "Specialist"
            });
            db.ProviderContacts.Add(new ProviderContact
            {
                ProviderId = merged.Id,
                Name = "Referral coordinator",
                IsPrimary = true
            });
            db.Settings.Add(new Settings
            {
                AgencyId = AgencyId,
                DefaultPassthroughProviderId = merged.Id
            });
            db.ComprehensiveAssessments.Add(new ComprehensiveAssessment
            {
                PersonId = person.Id,
                AuthorUserId = _caseManager.Id,
                DocumentJson = documentJson
            });
            await db.SaveChangesAsync();
            return new MergeGraph(survivor.Id, merged.Id, child.Id, documentJson);
        }

        public async Task AddPersonWithLinksAsync(int firstProviderId, int secondProviderId)
        {
            await using var db = Factory.CreateDbContext();
            var person = Person.CreatePerson(
                _caseManager.Id,
                "Synthetic",
                "Consumer",
                "Duplicate-link test.",
                new DateTime(1990, 1, 1),
                null,
                WaiverType.None,
                new Settings());
            person.AgencyId = AgencyId;
            db.People.Add(person);
            await db.SaveChangesAsync();
            db.PersonProviders.AddRange(
                new PersonProvider { PersonId = person.Id, ProviderId = firstProviderId },
                new PersonProvider { PersonId = person.Id, ProviderId = secondProviderId });
            await db.SaveChangesAsync();
        }

        public async Task DemoteAdminAsync()
        {
            await using var db = Factory.CreateDbContext();
            var admin = await db.Users.SingleAsync(user => user.Id == _admin.Id);
            admin.Permissions &= ~UserPermissions.Administration;
            await db.SaveChangesAsync();
        }

        public async Task AssertProvidersRemainAsync(params int[] providerIds)
        {
            await using var db = Factory.CreateDbContext();
            var existing = await db.Providers
                .Where(provider => providerIds.Contains(provider.Id))
                .Select(provider => provider.Id)
                .ToListAsync();
            Assert.Equal(providerIds.OrderBy(id => id), existing.OrderBy(id => id));
            Assert.Empty(await db.AuditEvents.ToListAsync());
        }

        private ProviderService ServiceFor(User user)
        {
            var session = new SessionService();
            session.SetUser(user);
            return new ProviderService(Factory, session);
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }

    private sealed record MergeGraph(
        int SurvivorId,
        int MergedId,
        int ChildId,
        string DocumentJson);
}
