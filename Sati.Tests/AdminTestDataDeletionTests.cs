using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Models.Assessments;
using Sati.Models.Billing;
using Sati.Reporting;
using Xunit;

namespace Sati.Tests;

public sealed class AdminTestDataDeletionTests
{
    [Fact]
    public async Task LocalAdminDeletesEveryConsumerOwnedTestRecordButKeepsAuditEvidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();

        var result = await fixture.AdminService.DeleteTestConsumerAsync(
            fixture.TestPersonId,
            fixture.TestPersonRevision,
            TestDataDeletionRules.ConsumerAttestation);
        var after = await fixture.SnapshotAsync();

        Assert.Equal(before.RelatedRecords, result.RelatedRecordsDeleted);
        Assert.Equal(0, after.People);
        Assert.Equal(0, after.RelatedRecords);
        Assert.Equal(0, after.ClaimLines);
        Assert.Equal(2, after.AuditEvents);
        await using var db = fixture.Factory.CreateDbContext();
        var audit = await db.AuditEvents.SingleAsync(candidate =>
            candidate.Action == "test-data.consumer-deleted" &&
            candidate.ResourceId == fixture.TestPersonId.ToString());
        Assert.Equal(fixture.Admin.Id, audit.ActorUserId);
        Assert.Equal(fixture.Admin.AgencyId, audit.AgencyId);
        Assert.Contains(TestDataDeletionRules.ConsumerAttestation, audit.MetadataJson);
        Assert.DoesNotContain("Disposable", audit.MetadataJson);
        Assert.Equal(1, result.PersonProvidersDeleted);
    }

    [Fact]
    public async Task LocalDeleteRequiresAnAdminAndTheExactAffirmation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CaseManagerService.DeleteTestConsumerAsync(
                fixture.TestPersonId,
                fixture.TestPersonRevision,
                TestDataDeletionRules.ConsumerAttestation));
        var affirmationError = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.AdminService.DeleteTestConsumerAsync(
                fixture.TestPersonId,
                fixture.TestPersonRevision,
                ""));

        Assert.Contains("affirmation", affirmationError.Message);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task LocalDeleteRevalidatesAStaleAdminSessionAgainstTheDatabase()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        await fixture.DemoteAdminInDatabaseAsync();

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.AdminService.DeleteTestConsumerAsync(
                fixture.TestPersonId,
                fixture.TestPersonRevision,
                TestDataDeletionRules.ConsumerAttestation));

        Assert.Contains("current Admin", error.Message);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task LocalAdminCannotDeleteAnotherAgencysConsumer()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var dbBefore = fixture.Factory.CreateDbContext();
        var existedBefore = await dbBefore.People.AnyAsync(candidate => candidate.Id == fixture.ForeignPersonId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.DeleteTestConsumerAsync(
                fixture.ForeignPersonId,
                fixture.ForeignPersonRevision,
                TestDataDeletionRules.ConsumerAttestation));

        await using var dbAfter = fixture.Factory.CreateDbContext();
        Assert.True(existedBefore);
        Assert.True(await dbAfter.People.AnyAsync(candidate => candidate.Id == fixture.ForeignPersonId));
        Assert.Contains("your agency", error.Message);
    }

    [Fact]
    public async Task LocalStaleRevisionLeavesTheEntireGraphUntouched()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.DeleteTestConsumerAsync(
                fixture.TestPersonId,
                fixture.TestPersonRevision - 1,
                TestDataDeletionRules.ConsumerAttestation));

        Assert.Contains("changed after you selected", error.Message);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task LocalBillingClaimBlocksDeletionAndLeavesTheGraphUntouched()
    {
        await using var fixture = await Fixture.CreateAsync(withClaimLine: true);
        var before = await fixture.SnapshotAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.DeleteTestConsumerAsync(
                fixture.TestPersonId,
                fixture.TestPersonRevision,
                TestDataDeletionRules.ConsumerAttestation));

        Assert.Equal(TestDataDeletionRules.ConsumerHasClaimsMessage, error.Message);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task LocalAdminCannotDeleteAConsumerThatWasNotMarkedTestAtCreation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MarkTestPersonAsOrdinaryAsync();
        var before = await fixture.SnapshotAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.DeleteTestConsumerAsync(
                fixture.TestPersonId,
                fixture.TestPersonRevision,
                TestDataDeletionRules.ConsumerAttestation));

        Assert.Contains("not marked as Test", error.Message);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public void ConfirmationTextUsesTheRequiredWarningVerbatim()
    {
        Assert.Equal(
            "Clicking delete affirms the consumer being deleted was created for testing purposes only.  " +
            "For duplicate consumers or consumers who are no longer receiving services, please click cancel and seek guidance in the help menu.",
            TestDataDeletionRules.ConsumerConfirmationText);
    }

    [Fact]
    public void TestMarkerMigrationBackfillsOnlyTheExactDemoDatabaseIdentity()
    {
        var migration = new Migrations.AddTestConsumerMarker();
        var builder = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        var up = typeof(Migrations.AddTestConsumerMarker).GetMethod(
            "Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        up.Invoke(migration, [builder]);

        var addColumn = Assert.Single(builder.Operations.OfType<
            Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>());
        var backfill = Assert.Single(builder.Operations.OfType<
            Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Equal(nameof(Person.IsTestData), addColumn.Name);
        Assert.Equal(false, addColumn.DefaultValue);
        Assert.Contains("DB_NAME() = N'SatiDemo'", backfill.Sql, StringComparison.Ordinal);
        Assert.Contains("SatiDatabaseIdentity", backfill.Sql, StringComparison.Ordinal);
        Assert.Contains("EnvironmentName = N'Demo'", backfill.Sql, StringComparison.Ordinal);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, DbContextOptions<SatiContext> options)
        {
            _connection = connection;
            Factory = new TestContextFactory(options);
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public User Admin { get; private set; } = null!;
        public User CaseManager { get; private set; } = null!;
        public AdminService AdminService { get; private set; } = null!;
        public AdminService CaseManagerService { get; private set; } = null!;
        public int TestPersonId { get; private set; }
        public int TestPersonRevision { get; private set; }
        public int ForeignPersonId { get; private set; }
        public int ForeignPersonRevision { get; private set; }

        public static async Task<Fixture> CreateAsync(bool withClaimLine = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>()
                .UseSqlite(connection)
                .Options;
            var fixture = new Fixture(connection, options);
            await fixture.SeedAsync(withClaimLine);
            return fixture;
        }

        private async Task SeedAsync(bool withClaimLine)
        {
            await using var db = Factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            var agency = new Agency { Id = 101, Name = "Agency One" };
            var foreignAgency = new Agency { Id = 102, Name = "Agency Two" };
            Admin = User.Create(1101, "admin-test-delete", "Admin", "hash", "salt", UserRole.Admin, null, agency.Id);
            CaseManager = User.Create(1102, "case-manager-test-delete", "Case Manager", "hash", "salt", UserRole.CaseManager, null, agency.Id);
            var foreignCaseManager = User.Create(1202, "foreign-test-delete", "Foreign Case Manager", "hash", "salt", UserRole.CaseManager, null, foreignAgency.Id);
            db.Agencies.AddRange(agency, foreignAgency);
            db.Users.AddRange(Admin, CaseManager, foreignCaseManager);

            var person = Person.CreatePerson(
                CaseManager.Id,
                "Disposable",
                "Test Consumer",
                "Synthetic record",
                new DateTime(1990, 1, 1),
                null,
                WaiverType.None,
                new Settings());
            person.AgencyId = agency.Id;
            person.Revision = 3;
            person.IsTestData = true;
            var foreignPerson = Person.CreatePerson(
                foreignCaseManager.Id,
                "Foreign",
                "Test Consumer",
                "Synthetic record",
                new DateTime(1990, 1, 1),
                null,
                WaiverType.None,
                new Settings());
            foreignPerson.AgencyId = foreignAgency.Id;
            foreignPerson.Revision = 4;
            foreignPerson.IsTestData = true;
            db.People.AddRange(person, foreignPerson);
            var provider = new Provider
            {
                AgencyId = agency.Id,
                Type = ProviderType.Healthcare,
                MedicalKind = MedicalProviderKind.Individual,
                Name = "Synthetic clinician"
            };
            db.Providers.Add(provider);
            await db.SaveChangesAsync();
            TestPersonId = person.Id;
            TestPersonRevision = person.Revision;
            ForeignPersonId = foreignPerson.Id;
            ForeignPersonRevision = foreignPerson.Revision;

            var note = Note.Create(
                "Synthetic deletion fixture",
                DateTime.Today,
                NoteStatus.Logged,
                15,
                person.Id,
                noteType: NoteType.Visit);
            note.Person = person;
            note.AgencyId = agency.Id;
            var review = new ReviewItem(
                person.Id,
                DateTime.Today.AddMonths(-1),
                1,
                ReviewCategory.Medical);
            review.Person = person;
            var request = ATRequest.CreateForClient(person, CaseManager);
            request.Person = person;
            request.Items.Add(new ATRequestItem
            {
                ATRequest = request,
                Name = "Test item",
                ItemCost = 1m,
                Quantity = 1
            });
            db.Forms.Add(new Form(FormType.PCP, DateTime.Today.AddDays(30), false)
            {
                PersonId = person.Id,
                Person = person
            });
            db.Notes.Add(note);
            db.PersonContacts.Add(new PersonContact
            {
                PersonId = person.Id,
                Person = person,
                FirstName = "Test",
                LastName = "Contact"
            });
            db.PersonProviders.Add(new PersonProvider
            {
                PersonId = person.Id,
                Person = person,
                ProviderId = provider.Id,
                Role = "Test clinician"
            });
            db.ReviewItems.Add(review);
            db.ComprehensiveAssessments.Add(new ComprehensiveAssessment
            {
                PersonId = person.Id,
                Person = person,
                AuthorUserId = CaseManager.Id,
                AuthorUser = CaseManager,
                DocumentJson = "{\"testData\":true}"
            });
            db.ATRequests.Add(request);
            db.PersonVersions.Add(new PersonVersion
            {
                PersonId = person.Id,
                Person = person,
                AgencyId = agency.Id,
                ActorUserId = CaseManager.Id,
                ActorDisplayName = CaseManager.DisplayName,
                Version = 1,
                ChangeKind = "Created",
                ChangedAtUtc = DateTime.UtcNow,
                CorrelationId = $"fixture-{Guid.NewGuid():N}",
                SnapshotGzip = [1],
                ChangesGzip = [1]
            });
            db.AuditEvents.Add(new AuditEvent
            {
                AgencyId = agency.Id,
                ActorUserId = CaseManager.Id,
                Action = "test-data.fixture-created",
                ResourceType = "Person",
                ResourceId = person.Id.ToString(),
                CorrelationId = $"fixture-{Guid.NewGuid():N}"
            });
            await db.SaveChangesAsync();

            db.Appointments.Add(new Appointment(review.Id, DateTime.Today, "Test provider")
            {
                ReviewItem = review
            });
            if (withClaimLine)
            {
                var period = new BillingPeriod
                {
                    UserId = CaseManager.Id,
                    User = CaseManager,
                    Month = 1,
                    Year = 2099,
                    Status = BillingStatus.Draft
                };
                period.Lines.Add(new ClaimLine
                {
                    NoteId = note.Id,
                    Note = note,
                    BillingPeriod = period,
                    DateOfService = DateTime.Today,
                    ProcedureCode = "G9012",
                    Units = 1,
                    ChargeAmount = 25m,
                    ClientMaineCareId = "TEST",
                    RenderingProviderNpi = "1999999984",
                    DiagnosisCode = "F89",
                    PlaceOfService = 11,
                    ClaimSnapshotJson = "{\"testData\":true}"
                });
                db.BillingPeriods.Add(period);
            }
            await db.SaveChangesAsync();

            AdminService = CreateService(Admin);
            CaseManagerService = CreateService(CaseManager);
        }

        private AdminService CreateService(User actor)
        {
            var session = new SessionService();
            session.SetUser(actor);
            return new AdminService(Factory, session, new PersonAuditPdfExporter());
        }

        public async Task<GraphSnapshot> SnapshotAsync()
        {
            await using var db = Factory.CreateDbContext();
            return new GraphSnapshot(
                await db.People.CountAsync(candidate => candidate.Id == TestPersonId),
                await db.Forms.CountAsync(candidate => candidate.PersonId == TestPersonId),
                await db.Notes.CountAsync(candidate => candidate.PersonId == TestPersonId),
                await db.PersonContacts.CountAsync(candidate => candidate.PersonId == TestPersonId),
                await db.PersonProviders.CountAsync(candidate => candidate.PersonId == TestPersonId),
                await db.ReviewItems.CountAsync(candidate => candidate.PersonId == TestPersonId),
                await db.Appointments.CountAsync(appointment => db.ReviewItems.Any(review =>
                    review.Id == appointment.ReviewItemId && review.PersonId == TestPersonId)),
                await db.ComprehensiveAssessments.CountAsync(candidate => candidate.PersonId == TestPersonId),
                await db.ATRequests.CountAsync(candidate => candidate.PersonId == TestPersonId),
                await db.ATRequestItems.CountAsync(item => db.ATRequests.Any(request =>
                    request.Id == item.ATRequestId && request.PersonId == TestPersonId)),
                await db.PersonVersions.CountAsync(candidate => candidate.PersonId == TestPersonId),
                await db.ClaimLines.CountAsync(line => db.Notes.Any(note =>
                    note.Id == line.NoteId && note.PersonId == TestPersonId)),
                await db.AuditEvents.CountAsync(candidate =>
                    candidate.ResourceType == "Person" && candidate.ResourceId == TestPersonId.ToString()));
        }

        public async Task DemoteAdminInDatabaseAsync()
        {
            await using var db = Factory.CreateDbContext();
            var storedAdmin = await db.Users.SingleAsync(candidate => candidate.Id == Admin.Id);
            storedAdmin.Role = UserRole.CaseManager;
            await db.SaveChangesAsync();
        }

        public async Task MarkTestPersonAsOrdinaryAsync()
        {
            await using var db = Factory.CreateDbContext();
            var person = await db.People.SingleAsync(candidate => candidate.Id == TestPersonId);
            person.IsTestData = false;
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class TestContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);

        public Task<SatiContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed record GraphSnapshot(
        int People,
        int Forms,
        int Notes,
        int Contacts,
        int PersonProviders,
        int Reviews,
        int Appointments,
        int Assessments,
        int AtRequests,
        int AtRequestItems,
        int PersonVersions,
        int ClaimLines,
        int AuditEvents)
    {
        public int RelatedRecords =>
            Forms + Notes + Contacts + Reviews + Appointments + Assessments +
            PersonProviders + AtRequests + AtRequestItems + PersonVersions;
    }
}
