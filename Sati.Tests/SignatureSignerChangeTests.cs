using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class SignatureSignerChangeTests
{
    [Theory]
    [InlineData("agency", "read")]
    [InlineData("agency", "save")]
    [InlineData("agency", "archive")]
    [InlineData("agency", "profile")]
    [InlineData("owner", "read")]
    [InlineData("owner", "save")]
    [InlineData("owner", "archive")]
    [InlineData("owner", "profile")]
    [InlineData("removed-permission", "read")]
    [InlineData("removed-permission", "save")]
    [InlineData("removed-permission", "archive")]
    [InlineData("removed-permission", "profile")]
    [InlineData("removed-role", "read")]
    [InlineData("removed-role", "save")]
    [InlineData("removed-role", "archive")]
    [InlineData("removed-role", "profile")]
    public async Task LocalTouchedServicesRecheckCurrentOwnershipAndPermissions(string boundary, string operation)
    {
        await using var fixture = await Fixture.CreateAsync(true);
        Person person; PersonContact contact;
        await using (var db = fixture.Factory.CreateDbContext())
        {

            db.Users.Add(User.Create(2, "other", "Other Staff", "hash", "salt", UserRole.CaseManager, null, 1));
            await db.SaveChangesAsync();
            person = await db.People.SingleAsync();
            if (boundary == "agency") person.AgencyId = 2;
            if (boundary == "owner") person.TransferTo(2);
            var actor = await db.Users.SingleAsync(x => x.Id == 1);
            if (boundary == "removed-permission") actor.Permissions = UserPermissions.Billing;
            if (boundary == "removed-role") actor.Role = UserRole.Supervisor;
            await db.SaveChangesAsync();
            contact = await db.PersonContacts.AsNoTracking().SingleAsync();
        }
        var contacts = new PersonContactService(fixture.Factory, fixture.Session());
        if (operation == "read") await Assert.ThrowsAsync<InvalidOperationException>(() => contacts.GetActiveByPersonAsync(person.Id));
        contact.Email = "changed@example.test";
        if (operation == "save") await Assert.ThrowsAsync<InvalidOperationException>(() => contacts.SaveAsync(contact));
        if (operation == "archive") await Assert.ThrowsAsync<InvalidOperationException>(() => contacts.ArchiveAsync(contact.Id));
        person.Email = "changed@example.test";
        if (operation == "profile") await Assert.ThrowsAsync<InvalidOperationException>(() => new PersonService(fixture.Factory, new SettingsService(), fixture.Session()).EditPersonAsync(person));
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Equal("synthetic@example.test", (await verify.PersonContacts.SingleAsync()).Email);
        Assert.True((await verify.PersonContacts.SingleAsync()).IsActive);
        Assert.Equal("synthetic@example.test", (await verify.People.SingleAsync()).Email);
        await fixture.AssertOutcomeAsync(false);
    }

    [Fact]
    public async Task LocalProfileEditCannotTransferTheConsumerToAnotherOwner()
    {
        await using var fixture = await Fixture.CreateAsync(false);
        Person person;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Users.Add(User.Create(2, "other", "Other Staff", "hash", "salt", UserRole.CaseManager, null, 1));
            await db.SaveChangesAsync(); person = await db.People.AsNoTracking().SingleAsync();
        }
        person.TransferTo(2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PersonService(fixture.Factory, new SettingsService(), fixture.Session()).EditPersonAsync(person));
        await using var verify = fixture.Factory.CreateDbContext(); Assert.Equal(1, (await verify.People.SingleAsync()).UserId);
    }

    [Fact]
    public async Task LocalConsumerEditRevokesItsOpenSignatureWithoutRewritingSignerHistory()
    {
        await using var fixture = await Fixture.CreateAsync(false);
        Person person;
        await using (var db = fixture.Factory.CreateDbContext()) person = await db.People.AsNoTracking().SingleAsync();
        person.Email = "changed@example.test";
        var session = new SessionService(); session.SetUser(fixture.Actor);
        await new PersonService(fixture.Factory, new SettingsService(), session).EditPersonAsync(person);
        await fixture.AssertOutcomeAsync(true);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("email")]
    [InlineData("kind")]
    [InlineData("archive")]
    [InlineData("phone")]
    public async Task LocalContactEditOrArchiveInvalidatesOnlySigningDetails(string change)
    {
        await using var fixture = await Fixture.CreateAsync(true);
        var service = new PersonContactService(fixture.Factory, fixture.Session());
        PersonContact contact;
        await using (var db = fixture.Factory.CreateDbContext()) contact = await db.PersonContacts.AsNoTracking().SingleAsync();
        if (change == "archive") await service.ArchiveAsync(contact.Id);
        else
        {
            if (change == "name") contact.FirstName = "Changed";
            if (change == "email") contact.Email = "changed@example.test";
            if (change == "kind") contact.Kind = PersonContactKind.Personal;
            if (change == "phone") contact.Phone = "2075550100";
            await service.SaveAsync(contact);
        }
        await fixture.AssertOutcomeAsync(change != "phone");
    }

    [Fact]
    public async Task FailedRevocationEvidenceRollsBackTheLocalContactChange()
    {
        await using var fixture = await Fixture.CreateAsync(true);
        await using (var db = fixture.Factory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER RejectSyntheticSignerEvidence BEFORE INSERT ON SignatureEvents
                WHEN NEW.Kind = 'SignerRecordChanged'
                BEGIN SELECT RAISE(ABORT, 'Synthetic evidence failure'); END;
                """);
        var service = new PersonContactService(fixture.Factory, fixture.Session());
        await Assert.ThrowsAsync<DbUpdateException>(() => service.ArchiveAsync(fixture.ContactId));
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.True((await verify.PersonContacts.SingleAsync()).IsActive);
        Assert.Equal("Issued", (await verify.SignatureRequests.SingleAsync()).State);
    }

    private sealed class SettingsService : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(new Settings());
        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }
    private sealed class ContextFactory(DbContextOptions<SatiContext> options) : IDbContextFactory<SatiContext>
    { public SatiContext CreateDbContext() => new(options); }
    private sealed class Fixture(SqliteConnection connection, ContextFactory factory, User actor, int contactId) : IAsyncDisposable
    {
        public ContextFactory Factory { get; } = factory;
        public User Actor { get; } = actor;
        public int ContactId { get; } = contactId;
        public SessionService Session() { var session = new SessionService(); session.SetUser(Actor); return session; }
        public static async Task<Fixture> CreateAsync(bool contactSigner)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await connection.OpenAsync();
            var factory = new ContextFactory(new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options);
            await using var db = factory.CreateDbContext(); await db.Database.EnsureCreatedAsync();
            var actor = User.Create(1, "synthetic", "Synthetic Staff", "hash", "salt", UserRole.CaseManager, null, 1);
            db.Users.Add(actor); await db.SaveChangesAsync();
            var person = Person.CreatePerson(1, "Synthetic", "Consumer", "Synthetic biography", new(1990, 1, 1), null, WaiverType.None, new Settings());
            person.AgencyId = 1; person.Email = "synthetic@example.test"; db.People.Add(person); await db.SaveChangesAsync();
            var contact = new PersonContact { PersonId = person.Id, FirstName = "Synthetic", LastName = "Guardian", Kind = PersonContactKind.Guardian, Email = "synthetic@example.test" };
            db.PersonContacts.Add(contact); await db.SaveChangesAsync();
            var artifact = DocumentArtifact.Generated(person.Id, 1, AnnualDocumentKind.PrivacyPractices, DateTime.Today,
                DocumentArtifactOrigin.GeneratedInSati, DateTime.UtcNow, 1, [1, 2, 3], "synthetic.pdf");
            db.DocumentArtifacts.Add(artifact); await db.SaveChangesAsync();
            var frozen = new FrozenSignatureDocument { AgencyId = 1, PersonId = person.Id, DocumentArtifactId = artifact.Id,
                ContentSha256 = artifact.ContentSha256!, ByteCount = 3, BlobPath = "synthetic/original.pdf", StoredAtUtc = DateTime.UtcNow, StoredByUserId = 1 };
            db.FrozenSignatureDocuments.Add(frozen); await db.SaveChangesAsync();
            db.SignatureRequests.Add(new SignatureRequest { AgencyId = 1, PersonId = person.Id, FrozenDocumentId = frozen.Id,
                ClientRequestId = Guid.NewGuid(), SignerCapacity = contactSigner ? "Guardian" : "Consumer", SignerContactId = contactSigner ? contact.Id : null,
                SignerName = "Synthetic Signer", DeliveryEmail = "synthetic@example.test", TokenSha256 = new('A', 64), PinHash = "synthetic-hash", PinSalt = "synthetic-salt",
                PinIterations = 600000, PinPepperWrapped = [1], PinKeyId = "synthetic-key", DisclosureVersion = "synthetic-v1", DisclosureText = "Synthetic disclosure",
                IntentText = "Synthetic intent", IssuedByUserId = 1, IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(3) });
            await db.SaveChangesAsync();
            return new(connection, factory, actor, contact.Id);
        }
        public async Task AssertOutcomeAsync(bool revoked)
        {
            await using var db = Factory.CreateDbContext(); var request = await db.SignatureRequests.SingleAsync();
            Assert.Equal(revoked ? "Revoked" : "Issued", request.State);
            Assert.Equal(revoked ? 2 : 1, request.AuthenticationVersion);
            Assert.Equal("Synthetic Signer", request.SignerName); Assert.Equal("synthetic@example.test", request.DeliveryEmail);
            Assert.Equal(revoked, await db.SignatureEvents.AnyAsync(x => x.Kind == "SignerRecordChanged"));
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
