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

/// <summary>
/// Rule-3 deletion, in local Production: permanently deleting an ordinary consumer created
/// within the window. Per HANDOFF_CLIENT_DELETION_POLICY.md, the interesting tests are the
/// permissive ones — a record with notes, an assessment, an AT request, and a draft claim line
/// must remain fully deletable, since that is exactly the content a record created to try
/// something out will carry — and the exclusion test that keeps PHI out of the tombstone.
/// </summary>
public sealed class ConsumerDeletionInWindowTests
{
    [Fact]
    public async Task NewAnnualDocumentChildrenAreIncludedInAuthorizedDeletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.SeedDeletableConsumerAsync();
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(x => x.Id == id);
            var artifact = DocumentArtifact.Generated(id, person.AgencyId!.Value, AnnualDocumentKind.PrivacyPractices,
                DateTime.Today, DocumentArtifactOrigin.GeneratedInSati, DateTime.UtcNow, person.UserId, [1], "synthetic.pdf", []);
            db.DocumentArtifacts.Add(artifact);
            db.SafetyPlans.Add(new SafetyPlan { PersonId = id, AuthorUserId = person.UserId, CycleStart = DateTime.Today,
                DocumentJson = SafetyPlanRules.EmptyDocumentJson() });
            await db.SaveChangesAsync();
            db.DocumentAcknowledgments.Add(new DocumentAcknowledgment { DocumentArtifactId = artifact.Id,
                RecordedByUserId = person.UserId, RecordedAtUtc = DateTime.UtcNow, ReceivedOn = DateTime.Today });
            await db.SaveChangesAsync();
        }
        var result = await fixture.AdminService.DeleteConsumerInWindowAsync(id, 1,
            ConsumerDeletionRules.ConsumerAttestation, "Synthetic record cleanup.");
        Assert.Equal(1, result.SafetyPlansDeleted);
        Assert.Equal(1, result.DocumentAcknowledgmentsDeleted);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Empty(await verify.DocumentAcknowledgments.ToListAsync());
        Assert.Empty(await verify.SafetyPlans.ToListAsync());
    }

    [Fact]
    public async Task ADeletableConsumerWithPermissiveContentIsFullyDeleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personId = await fixture.SeedDeletableConsumerAsync(withDraftClaimLine: true);

        var result = await fixture.AdminService.DeleteConsumerInWindowAsync(
            personId, 1, ConsumerDeletionRules.ConsumerAttestation, "Created in error during a demo.");

        Assert.Equal(1, result.NotesDeleted);
        Assert.Equal(1, result.AssessmentsDeleted);
        Assert.Equal(1, result.AtRequestsDeleted);
        Assert.Equal(1, result.ClaimLinesDeleted);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Empty(await db.People.AsNoTracking().ToListAsync());
        Assert.Empty(await db.ClaimLines.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ADayTwentyOldConsumerIsRefused()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personId = await fixture.SeedDeletableConsumerAsync(createdDaysAgo: 20);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.DeleteConsumerInWindowAsync(
                personId, 1, ConsumerDeletionRules.ConsumerAttestation, "Too late."));

        Assert.Equal(ConsumerDeletionRules.OutsideWindowMessage, error.Message);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.NotEmpty(await db.People.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ANonAdminCannotDelete()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personId = await fixture.SeedDeletableConsumerAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CaseManagerService.DeleteConsumerInWindowAsync(
                personId, 1, ConsumerDeletionRules.ConsumerAttestation, "Attempted forgery."));
    }

    [Fact]
    public async Task AStaleRevisionIsRefused()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personId = await fixture.SeedDeletableConsumerAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.DeleteConsumerInWindowAsync(
                personId, 99, ConsumerDeletionRules.ConsumerAttestation, "Stale."));

        Assert.Contains("changed after you selected", error.Message);
    }

    [Fact]
    public async Task AnActiveLegalHoldRefusesDeletionBeforeAnyChildRowChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personId = await fixture.SeedDeletableConsumerAsync();
        await fixture.AdminService.PlaceLegalHoldAsync(
            new PlaceLegalHoldRequest(personId, "Program integrity review", null, null, DateTime.UtcNow));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.DeleteConsumerInWindowAsync(
                personId, 1, ConsumerDeletionRules.ConsumerAttestation, "Should be blocked."));

        Assert.Equal(ConsumerDeletionRules.LegalHoldActiveMessage, error.Message);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.NotEmpty(await db.Notes.AsNoTracking().ToListAsync());
    }

    // A registry that cannot confirm "no hold" must refuse exactly like an active hold — this is
    // what makes the gate fail closed rather than fail open.
    [Fact]
    public async Task AnUnavailableLegalHoldRegistryRefusesDeletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personId = await fixture.SeedDeletableConsumerAsync();
        var service = fixture.BuildAdminServiceWithThrowingRegistry(fixture.Admin);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteConsumerInWindowAsync(
                personId, 1, ConsumerDeletionRules.ConsumerAttestation, "Should be blocked."));

        Assert.Equal(ConsumerDeletionRules.LegalHoldUnavailableMessage, error.Message);
    }

    [Fact]
    public async Task ATransmittedBillingSubmissionEventRefusesDeletionEvenWithAValidAttestation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personId = await fixture.SeedDeletableConsumerAsync(withDraftClaimLine: true);
        await fixture.AddBillingSubmissionEventAsync(
            personId, BillingSubmissionStage.Transmitted, isSynthetic: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.DeleteConsumerInWindowAsync(
                personId, 1, ConsumerDeletionRules.ConsumerAttestation, "Should be blocked."));

        Assert.Equal(ConsumerDeletionRules.TransmittedBillingMessage, error.Message);
    }

    // Generated (not Transmitted) does not block — generating an EDI file is local.
    [Fact]
    public async Task AGeneratedOnlySubmissionEventDoesNotBlock()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personId = await fixture.SeedDeletableConsumerAsync(withDraftClaimLine: true);
        await fixture.AddBillingSubmissionEventAsync(
            personId, BillingSubmissionStage.Generated, isSynthetic: false);

        var result = await fixture.AdminService.DeleteConsumerInWindowAsync(
            personId, 1, ConsumerDeletionRules.ConsumerAttestation, "Only generated locally.");

        Assert.Equal(1, result.ClaimLinesDeleted);
    }

    // A synthetic transmitted-looking event still does not block — IsSynthetic is what marks
    // test/demo exchange data, regardless of stage.
    [Fact]
    public async Task ASyntheticTransmittedSubmissionEventDoesNotBlock()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personId = await fixture.SeedDeletableConsumerAsync(withDraftClaimLine: true);
        await fixture.AddBillingSubmissionEventAsync(
            personId, BillingSubmissionStage.Transmitted, isSynthetic: true);

        var result = await fixture.AdminService.DeleteConsumerInWindowAsync(
            personId, 1, ConsumerDeletionRules.ConsumerAttestation, "Synthetic only.");

        Assert.Equal(1, result.ClaimLinesDeleted);
    }

    [Fact]
    public async Task ANonSyntheticRemittanceClaimOutcomeRefusesDeletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personId = await fixture.SeedDeletableConsumerAsync(withDraftClaimLine: true);
        await fixture.AddRemittanceClaimOutcomeAsync(personId, isSynthetic: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.DeleteConsumerInWindowAsync(
                personId, 1, ConsumerDeletionRules.ConsumerAttestation, "Should be blocked."));

        Assert.Equal(ConsumerDeletionRules.TransmittedBillingMessage, error.Message);
    }

    [Fact]
    public async Task ASubmittedBillingPeriodRefusesDeletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personId = await fixture.SeedDeletableConsumerAsync(
            withDraftClaimLine: true, billingPeriodStatus: BillingStatus.Submitted);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.DeleteConsumerInWindowAsync(
                personId, 1, ConsumerDeletionRules.ConsumerAttestation, "Should be blocked."));

        Assert.Equal(ConsumerDeletionRules.TransmittedBillingMessage, error.Message);
    }

    // The exclusion test: a naive "record everything" tombstone would leak exactly this.
    [Fact]
    public async Task TheAuditTombstoneNeverContainsNarrativeOrMaineCareId()
    {
        await using var fixture = await Fixture.CreateAsync();
        var personId = await fixture.SeedDeletableConsumerAsync(
            narrativeSentinel: "SENTINEL_NARRATIVE_TEXT", maineCareId: "SENTINEL_MC_9137513");

        var result = await fixture.AdminService.DeleteConsumerInWindowAsync(
            personId, 1, ConsumerDeletionRules.ConsumerAttestation, "Duplicate created during import demo.");

        await using var db = fixture.Factory.CreateDbContext();
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(candidate =>
            candidate.Action == "consumer.deleted-in-window" &&
            candidate.ResourceId == personId.ToString());
        Assert.DoesNotContain("SENTINEL_NARRATIVE_TEXT", audit.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL_MC_9137513", audit.MetadataJson, StringComparison.Ordinal);
        Assert.Contains(ConsumerDeletionRules.ConsumerAttestation, audit.MetadataJson, StringComparison.Ordinal);
        // The itemized inventory round-trips against the returned counts. ConsumerDeletionResultDto
        // is serialized with its own declared (PascalCase) property names, not the surrounding
        // anonymous wrapper's camelCase.
        Assert.Contains($"\"NotesDeleted\":{result.NotesDeleted}", audit.MetadataJson, StringComparison.Ordinal);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private int _nextId = 5001;

        private Fixture(SqliteConnection connection, DbContextOptions<SatiContext> options)
        {
            _connection = connection;
            Factory = new TestContextFactory(options);
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public User Admin { get; private set; } = null!;
        public AdminService AdminService { get; private set; } = null!;
        public AdminService CaseManagerService { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
            var fixture = new Fixture(connection, options);
            await fixture.SeedAgencyAndUsersAsync();
            return fixture;
        }

        private async Task SeedAgencyAndUsersAsync()
        {
            await using var db = Factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            var agency = new Agency { Id = 401, Name = "Agency Deletion Window" };
            var caseManager = User.Create(
                1501, "case-manager-deletion", "Case Manager", "hash", "salt",
                UserRole.CaseManager, null, agency.Id);
            Admin = User.Create(
                1502, "admin-deletion", "Admin", "hash", "salt", UserRole.Admin, null, agency.Id);
            db.Agencies.Add(agency);
            db.Users.AddRange(caseManager, Admin);
            await db.SaveChangesAsync();

            AdminService = Build(Admin);
            CaseManagerService = Build(caseManager);
        }

        public async Task<int> SeedDeletableConsumerAsync(
            bool withDraftClaimLine = false,
            int createdDaysAgo = 5,
            BillingStatus billingPeriodStatus = BillingStatus.Draft,
            string narrativeSentinel = "Ordinary note narrative.",
            string maineCareId = "12345678A")
        {
            await using var db = Factory.CreateDbContext();
            var caseManager = await db.Users.AsNoTracking().SingleAsync(u => u.Role == UserRole.CaseManager);

            var person = Person.CreatePerson(
                caseManager.Id, "Deletable", "Consumer", "Created to try something out.",
                new DateTime(1990, 1, 1), null, WaiverType.None, new Settings());
            person.AgencyId = caseManager.AgencyId;
            person.MaineCareId = maineCareId;
            db.People.Add(person);
            await db.SaveChangesAsync();

            // Backdate CreatedAtUtc directly — CreatedAtUtc has no public setter by design, so
            // the test reaches around it with raw SQL rather than the model, the same way a
            // real backfilled row would predate the column.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE People SET CreatedAtUtc = {DateTime.UtcNow.AddDays(-createdDaysAgo)} WHERE Id = {person.Id}");

            var note = Note.Create(narrativeSentinel, DateTime.Today, NoteStatus.Logged, 30, person.Id);
            note.AgencyId = caseManager.AgencyId;
            db.Notes.Add(note);
            db.ComprehensiveAssessments.Add(new ComprehensiveAssessment
            {
                PersonId = person.Id,
                AuthorUserId = caseManager.Id,
                Status = AssessmentStatus.Draft,
                DocumentJson = "{}"
            });
            var atRequest = ATRequest.CreateForClient(person, caseManager);
            db.Set<ATRequest>().Add(atRequest);
            await db.SaveChangesAsync();

            if (withDraftClaimLine)
            {
                var billingPeriod = new BillingPeriod
                {
                    Id = _nextId++,
                    UserId = caseManager.Id,
                    Month = 8,
                    Year = 2026,
                    Status = billingPeriodStatus
                };
                db.BillingPeriods.Add(billingPeriod);
                db.ClaimLines.Add(new ClaimLine
                {
                    Id = _nextId++,
                    NoteId = note.Id,
                    BillingPeriodId = billingPeriod.Id,
                    DateOfService = DateTime.Today,
                    ProcedureCode = "G9012",
                    Units = 1m,
                    ChargeAmount = 25m,
                    ClientMaineCareId = maineCareId,
                    RenderingProviderNpi = "1234567890",
                    DiagnosisCode = "F84.0",
                    PlaceOfService = 11
                });
                await db.SaveChangesAsync();
            }

            return person.Id;
        }

        public async Task AddBillingSubmissionEventAsync(
            int personId, BillingSubmissionStage stage, bool isSynthetic)
        {
            await using var db = Factory.CreateDbContext();
            var billingPeriodId = await db.ClaimLines.AsNoTracking()
                .Where(claimLine => db.Notes.Any(note => note.Id == claimLine.NoteId && note.PersonId == personId))
                .Select(claimLine => claimLine.BillingPeriodId)
                .FirstAsync();
            db.BillingSubmissionEvents.Add(new BillingSubmissionEvent
            {
                Id = _nextId++,
                AgencyId = Admin.AgencyId,
                BillingPeriodId = billingPeriodId,
                OccurredAtUtc = DateTime.UtcNow,
                Stage = stage,
                IsSynthetic = isSynthetic
            });
            await db.SaveChangesAsync();
        }

        public async Task AddRemittanceClaimOutcomeAsync(int personId, bool isSynthetic)
        {
            await using var db = Factory.CreateDbContext();
            var billingPeriodId = await db.ClaimLines.AsNoTracking()
                .Where(claimLine => db.Notes.Any(note => note.Id == claimLine.NoteId && note.PersonId == personId))
                .Select(claimLine => claimLine.BillingPeriodId)
                .FirstAsync();
            db.RemittanceClaimOutcomes.Add(new RemittanceClaimOutcome
            {
                Id = _nextId++,
                AgencyId = Admin.AgencyId,
                BillingPeriodId = billingPeriodId,
                ClaimReference = $"CLM{_nextId}",
                PayerName = "MaineCare",
                ReceivedAtUtc = DateTime.UtcNow,
                Status = RemittanceClaimStatus.Paid,
                BilledAmount = 25m,
                PaidAmount = 25m,
                IsSynthetic = isSynthetic
            });
            await db.SaveChangesAsync();
        }

        public AdminService BuildAdminServiceWithThrowingRegistry(User actor)
        {
            var session = new SessionService();
            session.SetUser(actor);
            return new AdminService(
                Factory, session, new PersonAuditPdfExporter(), new ThrowingLegalHoldRegistry());
        }

        private AdminService Build(User actor)
        {
            var session = new SessionService();
            session.SetUser(actor);
            return new AdminService(
                Factory, session, new PersonAuditPdfExporter(), new LocalLegalHoldRegistry(Factory));
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

        private sealed class TestContextFactory(DbContextOptions<SatiContext> options)
            : IDbContextFactory<SatiContext>
        {
            public SatiContext CreateDbContext() => new(options);
        }

        private sealed class ThrowingLegalHoldRegistry : ILegalHoldRegistry
        {
            public Task<LegalHoldStatus> GetStatusAsync(
                int agencyId, int personId, CancellationToken cancellationToken = default) =>
                Task.FromResult(LegalHoldStatus.Unavailable);
        }
    }
}
