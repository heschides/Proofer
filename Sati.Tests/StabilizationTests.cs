using Sati.Api.Security;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Edi;
using Sati.Helpers;
using Sati.Models;
using Sati.Models.Billing;
using Sati.ViewModels.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Sati.Contracts.V1;
using Sati.Reporting;
using PdfSharp.Fonts;
using Xunit;

namespace Sati.Tests;

public sealed class StabilizationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    public void CaseManagerMayWriteOnlyOwnedWorkflowStatuses(int status)
    {
        Assert.True(NoteWorkflow.IsCaseManagerWritableStatus(status));
    }

    [Theory]
    [InlineData(6)] // Approved
    [InlineData(7)] // Returned
    [InlineData(8)] // Abandoned
    public void CaseManagerCannotAssertServerOwnedStatuses(int status)
    {
        Assert.False(NoteWorkflow.IsCaseManagerWritableStatus(status));
    }

    [Fact]
    public void LoggedAndApprovedNotesAreLocked()
    {
        Assert.False(NoteWorkflow.CanCaseManagerEdit(2));
        Assert.False(NoteWorkflow.CanCaseManagerEdit(6));
        Assert.True(NoteWorkflow.CanCaseManagerEdit(7));
    }

    [Fact]
    public void OnlyUnsubmittedNotesCanBeDeleted()
    {
        Assert.True(NoteWorkflow.CanCaseManagerDelete(1));
        Assert.False(NoteWorkflow.CanCaseManagerDelete(2));
        Assert.False(NoteWorkflow.CanCaseManagerDelete(6));
        Assert.False(NoteWorkflow.CanCaseManagerDelete(7));
    }

    [Fact]
    public void ApiPasswordHasherProducesVerifiableSaltedCredentials()
    {
        var service = new PasswordVerifier();
        var first = service.Hash("correct horse battery staple");
        var second = service.Hash("correct horse battery staple");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.True(service.Verify("correct horse battery staple", first.Hash, first.Salt));
        Assert.False(service.Verify("wrong password", first.Hash, first.Salt));
    }

    [Fact]
    public void DayAfterThanksgivingIsExcludedWhenConfigured()
    {
        var settings = new Settings { ExcludeDayAfterThanksgiving = true };
        Assert.True(WorkdayHelper.IsAlwaysExcludedWorkday(new DateTime(2026, 11, 27), settings));
    }

    [Fact]
    public void PersonCreationGeneratesOneFormPerType()
    {
        var settings = new Settings();
        var person = Person.CreatePerson(
            1, "Ada", "Lovelace", string.Empty,
            new DateTime(1990, 1, 1), new DateTime(2026, 1, 1),
            WaiverType.Section21, settings);

        Assert.Equal(Enum.GetValues<FormType>().Length, person.Forms.Count);
    }

    [Fact]
    public void IncentiveCalculationAppliesThresholdAndPerUnitRate()
    {
        var incentive = new Incentive
        {
            BaseIncentive = 100m,
            PerUnitIncentive = 2m,
            UnitsPerDay = 10,
            DaysScheduled = 10
        };

        Assert.Equal(0m, incentive.Calculate(99m));
        Assert.Equal(100m, incentive.Calculate(100m));
        Assert.Equal(110m, incentive.Calculate(105m));
    }

    [Fact]
    public void EfModelMatchesLatestMigrationSnapshot()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiModelValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void ClaimLineUniquenessMigrationReplacesTheExistingIndex()
    {
        var migration = new Migrations.RequireOneClaimLinePerNote();
        var builder = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        var up = typeof(Migrations.RequireOneClaimLinePerNote).GetMethod(
            "Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        up.Invoke(migration, [builder]);

        var drop = Assert.Single(builder.Operations.OfType<
            Microsoft.EntityFrameworkCore.Migrations.Operations.DropIndexOperation>());
        var create = Assert.Single(builder.Operations.OfType<
            Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation>());
        Assert.Equal("IX_ClaimLines_NoteId", drop.Name);
        Assert.Equal("ClaimLines", drop.Table);
        Assert.Equal("IX_ClaimLines_NoteId", create.Name);
        Assert.Equal(["NoteId"], create.Columns);
        Assert.True(create.IsUnique);
    }

    [Fact]
    public void TenantOwnershipRepairIsRegisteredAsAnEfMigration()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiMigrationValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var migrations = context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();

        Assert.Contains("20260812213000_ReconcileTenantOwnership", migrations.Migrations.Keys);
    }

    [Fact]
    public void NoteRevisionIsAConcurrencyTokenAndItsMigrationIsRegistered()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiNoteRevisionValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var noteRevision = context.Model.FindEntityType(typeof(Note))!
            .FindProperty(nameof(Note.Revision));
        var migrations = context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();

        Assert.NotNull(noteRevision);
        Assert.True(noteRevision!.IsConcurrencyToken);
        Assert.Contains("20260812223000_AddNoteRevision", migrations.Migrations.Keys);
    }

    [Fact]
    public void AtRequestRevisionProtectsTheWholeAggregateAndItsMigrationIsRegistered()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiAtRevisionValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var revision = context.Model.FindEntityType(typeof(ATRequest))!
            .FindProperty(nameof(ATRequest.Revision));
        var migrations = context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();

        Assert.NotNull(revision);
        Assert.True(revision!.IsConcurrencyToken);
        Assert.Contains("20260812230000_AddAtRequestRevision", migrations.Migrations.Keys);
    }

    [Fact]
    public void SettingsRevisionIsAConcurrencyTokenAndItsMigrationIsRegistered()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiSettingsRevisionValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var revision = context.Model.FindEntityType(typeof(Settings))!
            .FindProperty(nameof(Settings.Revision));
        var migrations = context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();

        Assert.NotNull(revision);
        Assert.True(revision!.IsConcurrencyToken);
        Assert.Contains("20260812233000_AddSettingsRevision", migrations.Migrations.Keys);
    }

    [Fact]
    public void ScratchpadRevisionIsAConcurrencyTokenAndItsMigrationIsRegistered()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiScratchpadRevisionValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var revision = context.Model.FindEntityType(typeof(Scratchpad))!
            .FindProperty(nameof(Scratchpad.Revision));
        var migrations = context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();

        Assert.NotNull(revision);
        Assert.True(revision!.IsConcurrencyToken);
        Assert.Contains("20260812234500_AddScratchpadRevision", migrations.Migrations.Keys);
    }

    [Fact]
    public void BillingSubmissionAndEdiGenerationHaveDatabaseRetryGuards()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiEdiIdempotencyValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var billingStatus = context.Model.FindEntityType(typeof(BillingPeriod))!
            .FindProperty(nameof(BillingPeriod.Status));
        var generation = context.Model.FindEntityType(typeof(EdiGeneration))!;
        var retryIndex = Assert.Single(generation.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(EdiGeneration.AgencyId),
                nameof(EdiGeneration.ActorUserId),
                nameof(EdiGeneration.IdempotencyKey)]));
        var migrations = context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();

        Assert.NotNull(billingStatus);
        Assert.True(billingStatus!.IsConcurrencyToken);
        Assert.True(retryIndex.IsUnique);
        Assert.Contains("20260812235500_AddEdiIdempotency", migrations.Migrations.Keys);
    }

    [Fact]
    public async Task DesktopKeepsTheSameEdiRetryKeyUntilGenerationSucceeds()
    {
        var edi = new RetryRecordingEdiService();
        var viewModel = new BillingSubmissionsViewModel(
            new StubBillingService(),
            edi,
            new SessionService())
        {
            SelectedPeriod = new BillingPeriod { Id = 99 }
        };

        await viewModel.GenerateEdiCommand.ExecuteAsync(null);
        await viewModel.GenerateEdiCommand.ExecuteAsync(null);
        await viewModel.GenerateEdiCommand.ExecuteAsync(null);

        Assert.Equal(3, edi.Keys.Count);
        Assert.Equal(edi.Keys[0], edi.Keys[1]);
        Assert.NotEqual(edi.Keys[1], edi.Keys[2]);
        Assert.All(edi.Keys, key => Assert.True(Guid.TryParse(key, out _)));
    }

    [Fact]
    public async Task DesktopAdminAuditExporterCreatesAReadablePdf()
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        var person = Person.CreatePerson(
            12,
            "Lifecycle",
            "Example",
            "Initial biography.",
            new DateTime(1985, 5, 6),
            new DateTime(2025, 5, 6),
            WaiverType.Section21,
            new Settings());
        person.Revision = 2;
        var requester = User.Create(
            11,
            "admin-one",
            "Admin One",
            "hash",
            "salt",
            UserRole.Admin,
            null,
            1);
        var versions = new List<PersonVersionDto>
        {
            new(
                1, 0, 1, "TrackingBaseline", 0, "Sati tracking baseline",
                new DateTime(2026, 8, 12, 14, 0, 0, DateTimeKind.Utc),
                "desktop-baseline",
                [new("firstName", "First name", null, "Lifecycle")]),
            new(
                2, 0, 2, "Updated", 12, "Case Manager One",
                new DateTime(2026, 8, 12, 15, 0, 0, DateTimeKind.Utc),
                "desktop-update",
                [new("firstName", "First name", "Lifecycle", "Updated")])
        };

        var pdf = new PersonAuditPdfExporter().Generate(
            person,
            versions,
            new Agency { Id = 1, Name = "Agency One" },
            requester,
            new DateTime(2026, 8, 12, 16, 0, 0, DateTimeKind.Utc));

        Assert.True(pdf.Length > 2_000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var qaOutput = Environment.GetEnvironmentVariable("SATI_LOCAL_ADMIN_PDF_QA_OUTPUT");
        if (!string.IsNullOrWhiteSpace(qaOutput))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(qaOutput)!);
            await File.WriteAllBytesAsync(qaOutput, pdf);
        }
    }

    private sealed class RetryRecordingEdiService : IEdiService
    {
        public List<string> Keys { get; } = [];

        public Task<string> GenerateAndSaveAsync(int billingPeriodId, bool isTest, string idempotencyKey)
        {
            Keys.Add(idempotencyKey);
            if (Keys.Count == 1)
                throw new HttpRequestException("Simulated ambiguous network failure.");
            return Task.FromResult(@"C:\Sati\retry-safe-837p.txt");
        }
    }

    private sealed class StubBillingService : IBillingService
    {
        public Task<BillingPeriod> GetOrCreateBillingPeriodAsync(int userId, int month, int year) =>
            throw new NotSupportedException();
        public Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(int userId) =>
            throw new NotSupportedException();
        public Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync() =>
            throw new NotSupportedException();
        public Task<ClaimLine> CreateClaimLineAsync(int noteId, bool isComplianceException = false,
            string? complianceExceptionReason = null) => throw new NotSupportedException();
        public Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(int userId) =>
            throw new NotSupportedException();
        public Task SubmitBillingPeriodAsync(int billingPeriodId) => throw new NotSupportedException();
        public Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync() => throw new NotSupportedException();
        public BillingValidationResult ValidateNoteForBilling(Note note) => throw new NotSupportedException();
    }
}
