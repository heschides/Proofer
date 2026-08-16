using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The desktop-local publication lock, exercised against a real database.
///
/// This path is not a legacy detour: "My work" runs local EF by design, so
/// ATRequestService is the enforcement point for production desktop use. The
/// lock has to be tested where it lives — the view model's disabled buttons
/// prove nothing, and the API tests cover a different implementation.
///
/// SQLite stands in for SQL Server here. The behaviour under test is a guard
/// clause and a revision check, neither of which is provider-specific.
/// </summary>
public sealed class AtRequestServiceLockTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<SatiContext> _factory;

    public AtRequestServiceLockTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlite(_connection)
            .Options;

        using (var context = new SatiContext(options))
        {
            context.Database.EnsureCreated();

            // An AT request carries a real FK to its client, so the client (and
            // the user who owns them) has to exist before any of this runs.
            var owner = User.Create(0, "cmanager", "Casey Manager", string.Empty, string.Empty,
                UserRole.CaseManager, null, 1);
            context.Users.Add(owner);
            context.SaveChanges();

            var person = Person.Rehydrate(0, owner.Id);
            person.FirstName = "Test";
            person.LastName = "Client";
            context.People.Add(person);
            context.SaveChanges();

            _personId = person.Id;
        }

        _factory = new SingleConnectionContextFactory(options);
    }

    private readonly int _personId;

    public void Dispose() => _connection.Dispose();

    private ATRequestService NewService() => new(_factory, new StubSettingsService());

    private static User CaseManager(int id = 7, string name = "Casey Manager") =>
        User.Create(id, "cmanager", name, string.Empty, string.Empty, UserRole.CaseManager, null, 1);

    private ATRequest NewRequest()
    {
        var person = Person.Rehydrate(_personId, 1);
        person.FirstName = "Test";
        person.LastName = "Client";

        var request = ATRequest.CreateForClient(person, CaseManager());
        request.VendorName = "Maine AT Solutions";
        request.VendorBillingLocation = "1992205249-007";
        request.Items.Add(new ATRequestItem { Name = "Pie cutter", ItemCost = 25.99m, Quantity = 3 });
        return request;
    }

    [Fact]
    public async Task PublishingPersistsTheAttestation()
    {
        var service = NewService();
        var request = await service.AddAsync(NewRequest());

        await service.PublishAsync(request, CaseManager(id: 42, name: "Joshua White"));

        var stored = await service.GetByIdAsync(request.Id);
        Assert.NotNull(stored);
        Assert.True(stored!.IsPublished);
        Assert.Equal("Joshua White", stored.SignedByName);
        Assert.Equal(42, stored.SignedByUserId);
        Assert.Equal(ATRequestStatus.Review, stored.Status);
        Assert.NotNull(stored.AttestationStatement);
    }

    [Fact]
    public async Task APublishedRequestRefusesAFurtherSave()
    {
        // The lock. Without the guard in UpdateAsync this save succeeds and the
        // attested figures are silently replaced.
        var service = NewService();
        var request = await service.AddAsync(NewRequest());
        await service.PublishAsync(request, CaseManager());

        request.VendorName = "Rewritten After Signing";

        await Assert.ThrowsAsync<AtRequestLockedException>(() => service.UpdateAsync(request));

        var stored = await service.GetByIdAsync(request.Id);
        Assert.Equal("Maine AT Solutions", stored!.VendorName);
    }

    [Fact]
    public async Task ARequestCannotBePublishedTwiceThroughTheService()
    {
        var service = NewService();
        var request = await service.AddAsync(NewRequest());
        await service.PublishAsync(request, CaseManager());

        // A second client holding a pre-publication copy of the same request.
        var stale = await service.GetByIdAsync(request.Id);
        stale!.Revision = request.Revision;
        stale.RehydrateAttestation(null, null, null, null, null, null);

        await Assert.ThrowsAsync<AtRequestLockedException>(
            () => service.PublishAsync(stale, CaseManager(id: 99, name: "Second Signer")));

        var stored = await service.GetByIdAsync(request.Id);
        Assert.Equal("Casey Manager", stored!.SignedByName);
    }

    [Fact]
    public async Task PublishingAStaleCopyIsRefused()
    {
        // Someone edited the request after this copy was loaded. Attaching an
        // attestation now would bind it to a version the signer never saw.
        var service = NewService();
        var request = await service.AddAsync(NewRequest());

        var otherCopy = await service.GetByIdAsync(request.Id);
        otherCopy!.VendorName = "Changed Underneath";
        await service.UpdateAsync(otherCopy);

        await Assert.ThrowsAsync<AtRequestConcurrencyException>(
            () => service.PublishAsync(request, CaseManager()));

        var stored = await service.GetByIdAsync(request.Id);
        Assert.False(stored!.IsPublished);
    }

    [Fact]
    public async Task ReopeningClearsTheAttestationAndRestoresEditing()
    {
        var service = NewService();
        var request = await service.AddAsync(NewRequest());
        await service.PublishAsync(request, CaseManager());

        await service.ReopenAsync(request);

        Assert.False(request.IsPublished);

        request.VendorName = "Corrected Vendor";
        await service.UpdateAsync(request);

        var stored = await service.GetByIdAsync(request.Id);
        Assert.Equal("Corrected Vendor", stored!.VendorName);
        Assert.Null(stored.SignedByName);
        Assert.Equal(ATRequestStatus.Development, stored.Status);
    }

    [Fact]
    public async Task PublishingANeverSavedRequestInsertsItAlreadyAttested()
    {
        var service = NewService();

        var published = await service.PublishAsync(NewRequest(), CaseManager());

        Assert.NotEqual(0, published.Id);
        var stored = await service.GetByIdAsync(published.Id);
        Assert.True(stored!.IsPublished);
    }

    [Fact]
    public async Task TheSalesTaxOverrideFlagSurvivesAnUpdate()
    {
        // Regression: CopyMutableValues did not carry SalesTaxOverridden, so a
        // manually entered tax figure reverted to "calculated" on the next save
        // and the next item edit would silently restate it.
        var service = NewService();
        var request = await service.AddAsync(NewRequest());

        request.SalesTaxOverridden = true;
        request.SalesTax = 0m;
        await service.UpdateAsync(request);

        var stored = await service.GetByIdAsync(request.Id);
        Assert.True(stored!.SalesTaxOverridden);
        Assert.Equal(0m, stored.SalesTax);
    }

    // ATRequestService creates a short-lived context per method, exactly as it
    // does in the app. All of them share the one open SQLite connection, which is
    // what keeps the in-memory database alive between calls.
    private sealed class SingleConnectionContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }

    // ATRequestService reads Settings only for the queue projection's passthrough
    // rate, which none of these tests exercise.
    private sealed class StubSettingsService : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(new Settings());
        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }
}
