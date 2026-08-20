using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The local Production form path: everything on the workstation, no network, and —
/// as of 2026-08-19 — Social Security numbers stored here too, protected by the
/// Windows user's DPAPI key.
///
/// That last part reverses the original cloud-only decision. The reason was workflow
/// rather than architecture: filling the Appointment form is occasional, but reading
/// a consumer's number to the Social Security Administration on their behalf is
/// routine, and a case manager cannot do that from a mask. See
/// <c>DECISIONS.md</c> and <see cref="DpapiKeyWrapper"/> for what that protection
/// does and does not cover.
/// </summary>
[Collection(PdfRenderingCollection.Name)]
public sealed class LocalDhhsFormServiceTests
{
    private const DhhsFormDefinition.FormKey Appointment =
        DhhsFormDefinition.FormKey.AuthorizedRepresentative;

    /// <summary>
    /// A consumer with no number on file still produces a correct form — the box is
    /// named as needing a pen rather than the fill failing.
    /// </summary>
    [Fact]
    public async Task NoSsnOnFileLeavesTheBoxBlankAndSaysSo()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);

        var result = await service.GenerateAsync(
            Appointment, fixture.PersonId, DhhsFormDefinition.Selections.None);

        Assert.Contains("Individual's SSN", result.BlankFields);
    }

    /// <summary>
    /// The round trip that makes local storage worth having: store a number, and it
    /// comes back for the phone call and reaches the form.
    /// </summary>
    [Fact]
    public async Task AStoredNumberIsRevealedAndReachesTheForm()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);

        var status = await service.UpdateSsnAsync(fixture.PersonId, "123-45-6789");
        Assert.True(status.IsOnFile);
        Assert.Equal("***-**-6789", status.Masked);

        Assert.Equal("123456789", await service.RevealSsnAsync(fixture.PersonId));

        var result = await service.GenerateAsync(
            Appointment, fixture.PersonId, DhhsFormDefinition.Selections.None);
        Assert.DoesNotContain("Individual's SSN", result.BlankFields);
    }

    /// <summary>
    /// The column holds ciphertext, not a number. Anyone reading the database file
    /// directly — the case this protection exists for — finds nothing usable.
    /// </summary>
    [Fact]
    public async Task TheStoredColumnsContainNoPlaintext()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);
        await service.UpdateSsnAsync(fixture.PersonId, "123-45-6789");

        await using var db = fixture.Factory.CreateDbContext();
        var person = db.People.Single(candidate => candidate.Id == fixture.PersonId);
        var entry = db.Entry(person);
        var ciphertext = entry.Property<byte[]?>("SsnCiphertext").CurrentValue;

        Assert.NotNull(ciphertext);
        Assert.DoesNotContain("123456789", System.Text.Encoding.UTF8.GetString(ciphertext!));
        // The tail is stored in the clear on purpose: it is what the mask displays and
        // it cannot reconstruct the number.
        Assert.Equal("6789", entry.Property<string?>("SsnLastFour").CurrentValue);
    }

    /// <summary>
    /// Clearing removes the tail as well, so a consumer who asked to be removed is not
    /// left partially on file behind a mask claiming a number that no longer exists.
    /// </summary>
    [Fact]
    public async Task ClearingRemovesEveryPart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);
        await service.UpdateSsnAsync(fixture.PersonId, "123-45-6789");

        var cleared = await service.UpdateSsnAsync(fixture.PersonId, null);

        Assert.False(cleared.IsOnFile);
        Assert.Equal(SsnMask.NotOnFile, cleared.Masked);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RevealSsnAsync(fixture.PersonId));
    }

    /// <summary>Both the write and the read are disclosures, and both are recorded.</summary>
    [Fact]
    public async Task StoringAndRevealingAreBothAudited()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);

        await service.UpdateSsnAsync(fixture.PersonId, "123-45-6789");
        await service.RevealSsnAsync(fixture.PersonId);

        await using var db = fixture.Factory.CreateDbContext();
        var actions = db.AuditEvents.Select(row => row.Action).ToList();
        Assert.Contains(LocalAuditActions.PersonSsnUpdated, actions);
        Assert.Contains(LocalAuditActions.PersonSsnRevealed, actions);
    }

    /// <summary>An audit row names what happened, never the number it happened to.</summary>
    [Fact]
    public async Task NoAuditRowCarriesTheNumber()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);

        await service.UpdateSsnAsync(fixture.PersonId, "123-45-6789");
        await service.RevealSsnAsync(fixture.PersonId);

        await using var db = fixture.Factory.CreateDbContext();
        foreach (var row in db.AuditEvents.ToList())
        {
            Assert.DoesNotContain("123456789", row.MetadataJson ?? string.Empty);
            Assert.DoesNotContain("6789", row.Action);
        }
    }

    /// <summary>A number that was never issued is refused before it is encrypted.</summary>
    [Theory]
    [InlineData("666-12-3456")]
    [InlineData("123-00-6789")]
    public async Task ANumberThatIsNeverIssuedIsRefused(string candidate)
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateSsnAsync(fixture.PersonId, candidate));
    }

    /// <summary>The caseload restriction covers the SSN routes, not just form generation.</summary>
    [Fact]
    public async Task AnotherCaseloadsSsnIsUnreachable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateSsnAsync(fixture.ForeignPersonId, "123-45-6789"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RevealSsnAsync(fixture.ForeignPersonId));
    }

    /// <summary>
    /// The representative is the signed-in case manager — appointing them is what the
    /// form is for — so their details come from the User and Agency records rather
    /// than from anything stored on the consumer.
    /// </summary>
    [Fact]
    public async Task TheRepresentativeIsTheSignedInCaseManager()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);

        var result = await service.GenerateAsync(
            Appointment, fixture.PersonId, DhhsFormDefinition.Selections.None);

        Assert.NotEmpty(result.Pdf);
        Assert.DoesNotContain("AR Name", result.BlankFields);
        Assert.DoesNotContain("AR Address", result.BlankFields);
        Assert.DoesNotContain("AR Telephone Number", result.BlankFields);
    }

    /// <summary>
    /// A missing value is named, not thrown over. A representative without a phone on
    /// file still produces a correct, usable form — it just needs a pen.
    /// </summary>
    [Fact]
    public async Task AMissingRepresentativeValueIsReportedRatherThanFatal()
    {
        await using var fixture = await Fixture.CreateAsync(caseManagerPhone: null);
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);

        var result = await service.GenerateAsync(
            Appointment, fixture.PersonId, DhhsFormDefinition.Selections.None);

        Assert.NotEmpty(result.Pdf);
        Assert.Contains("AR Telephone Number", result.BlankFields);
    }

    /// <summary>
    /// The local service repeats the caseload restriction rather than relying on being
    /// the only caller, the way the other transitional desktop services do.
    /// </summary>
    [Fact]
    public async Task AConsumerOnAnotherCaseloadIsRefused()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateAsync(Appointment, fixture.ForeignPersonId, DhhsFormDefinition.Selections.None));
    }

    [Fact]
    public async Task ASelectionThatIsNotAConsentFieldIsRefused()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateAsync(
                Appointment,
                fixture.PersonId,
                new DhhsFormDefinition.Selections(
                    Checks: new Dictionary<string, bool> { ["Individual's Name"] = true })));
    }

    /// <summary>Generating a release form is a disclosure whichever environment produced it.</summary>
    [Fact]
    public async Task GeneratingAFormIsAudited()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DhhsFormService(fixture.Factory, fixture.Session, fixture.SsnStore);

        await service.GenerateAsync(Appointment, fixture.PersonId, DhhsFormDefinition.Selections.None);

        await using var db = fixture.Factory.CreateDbContext();
        Assert.Contains(
            db.AuditEvents.ToList(),
            row => row.Action == LocalAuditActions.DhhsFormGenerated);
    }

    private sealed class StubSession(User user) : ISessionService
    {
        public bool AllowComplianceOverride { get; set; }
        public User? CurrentUser { get; private set; } = user;
        public void SetUser(User signedIn) => CurrentUser = signedIn;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, DbContextOptions<SatiContext> options)
        {
            _connection = connection;
            Factory = new PooledFactory(options);
        }

        public IDbContextFactory<SatiContext> Factory { get; }

        /// <summary>Real store over a DPAPI wrapper: these tests run as a Windows user, so it works.</summary>
        public LocalSsnStore SsnStore { get; } = new(new EnvelopeProtector(new DpapiKeyWrapper()));
        public ISessionService Session { get; private set; } = null!;
        public int PersonId { get; private set; }
        public int ForeignPersonId { get; private set; }

        public static async Task<Fixture> CreateAsync(string? caseManagerPhone = "(207) 555-0100")
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
            var fixture = new Fixture(connection, options);
            await fixture.SeedAsync(caseManagerPhone);
            return fixture;
        }

        private async Task SeedAsync(string? caseManagerPhone)
        {
            await using var db = Factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();

            db.Agencies.Add(new Agency
            {
                Id = 401,
                Name = "Form Test Agency",
                Street = "10 Agency Way",
                City = "Augusta",
                State = "ME",
                Zip = "04330",
            });

            var caseManager = User.Create(
                4101, "form-case-manager", "Form Case Manager", "hash", "salt",
                UserRole.CaseManager, null, 401);
            caseManager.Email = "cm@example.invalid";
            caseManager.Phone = caseManagerPhone;
            var other = User.Create(
                4102, "form-other-manager", "Other Manager", "hash", "salt",
                UserRole.CaseManager, null, 401);
            db.Users.AddRange(caseManager, other);

            var person = CreatePerson(caseManager.Id, "Sample");
            var foreign = CreatePerson(other.Id, "Foreign");
            db.People.AddRange(person, foreign);
            await db.SaveChangesAsync();

            PersonId = person.Id;
            ForeignPersonId = foreign.Id;
            Session = new StubSession(caseManager);
        }

        private static Person CreatePerson(int userId, string lastName)
        {
            var person = Person.CreatePerson(
                userId,
                "Test",
                lastName,
                string.Empty,
                new DateTime(1970, 1, 15),
                DateTime.Today.AddYears(-1),
                WaiverType.Section21,
                new Settings());
            person.AgencyId = 401;
            person.Address = "12 Example Rd, Augusta, ME 04330";
            person.PhoneNumber = "(207) 555-0199";
            return person;
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }

        private sealed class PooledFactory(DbContextOptions<SatiContext> options)
            : IDbContextFactory<SatiContext>
        {
            public SatiContext CreateDbContext() => new(options);
        }
    }
}
