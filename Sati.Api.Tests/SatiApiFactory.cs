using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Signatures;

namespace Sati.Api.Tests;

public sealed class SatiApiFactory : WebApplicationFactory<Program>
{
    private const string TestPassword = "Correct-Horse-42!";
    private const string TestSigningKey = "integration-test-signing-key-that-is-at-least-32-characters";
    private const string TestDatabaseConnection =
        "Data Source=SatiApiTests;Mode=Memory;Cache=Shared;Default Timeout=30";
    private readonly SqliteConnection _connection = new(TestDatabaseConnection);
    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly SemaphoreSlim _testDataLock = new(1, 1);
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private bool _seeded;

    /// <summary>
    /// The in-memory stand-in for this environment's Key Vault key. Exposed so a test
    /// can rotate it and confirm rows wrapped under the old version still decrypt.
    /// </summary>
    internal static TestKeyWrapper TestVault { get; } = new();

    public SatiApiFactory()
    {
        _connection.Open();
        // Minimal-host startup reads these before WebApplicationFactory applies
        // ConfigureWebHost. They exist only in this test process; the production
        // API keeps its fail-closed startup validation.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__SatiDemo",
            "Server=(localdb)\\MSSQLLocalDB;Database=SatiApiTests;Trusted_Connection=True;Encrypt=False;");
        Environment.SetEnvironmentVariable("Authentication__Issuer", "Sati.Api.Tests");
        Environment.SetEnvironmentVariable("Authentication__Audience", "Sati.Api.Tests");
        Environment.SetEnvironmentVariable("Authentication__SigningKey", TestSigningKey);
        Environment.SetEnvironmentVariable("Authentication__TokenMinutes", "15");
        // The section is "Sati", and startup binds it before ConfigureWebHost runs,
        // so it has to arrive as an environment variable like the two above. Setting
        // it only through ConfigureAppConfiguration leaves the section empty and the
        // host throws "Sati configuration is required." before any test can run.
        Environment.SetEnvironmentVariable("Sati__ExpectedDatabaseName", "SatiApiTests");
        Environment.SetEnvironmentVariable("Sati__ExpectedEnvironment", "Testing");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SatiDemo"] = "Server=(localdb)\\MSSQLLocalDB;Database=SatiApiTests;Trusted_Connection=True;Encrypt=False;",
                ["Authentication:Issuer"] = "Sati.Api.Tests",
                ["Authentication:Audience"] = "Sati.Api.Tests",
                ["Authentication:SigningKey"] = TestSigningKey,
                ["Authentication:TokenMinutes"] = "15",
                ["Sati:ExpectedDatabaseName"] = "SatiApiTests",
                ["Sati:ExpectedEnvironment"] = "Testing"
            });
        });
        builder.ConfigureServices(services =>
        {
            var identityHostedService = services.SingleOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(DatabaseIdentityHostedService));
            if (identityHostedService is not null)
                services.Remove(identityHostedService);

            services.RemoveAll<ApiDbContext>();
            services.RemoveAll<DbContextOptions<ApiDbContext>>();
            services.RemoveAll<IDbContextFactory<ApiDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApiDbContext>>();
            services.RemoveAll<IDatabaseProvider>();
            services.AddDbContextFactory<ApiDbContext>(options =>
                options.UseSqlite(TestDatabaseConnection)
                    .ReplaceService<IExecutionStrategyFactory, TestRetryingExecutionStrategyFactory>());
            services.AddScoped(provider =>
                provider.GetRequiredService<IDbContextFactory<ApiDbContext>>().CreateDbContext());
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // Startup registers UnconfiguredKeyWrapper when Ssn:KeyUri is absent, which
            // is correct for a real environment with no vault and useless for testing
            // the SSN paths. Substituting an in-memory wrapper exercises the real
            // EnvelopeProtector — envelope, binding, and tag — without a vault or a
            // secret. Configuring a Key Vault URI here instead would make the suite
            // depend on Azure to run.
            services.RemoveAll<IKeyWrapper>();
            services.AddSingleton<IKeyWrapper>(TestVault);
            services.RemoveAll<ISignatureBlobStore>();
            services.RemoveAll<ISigningPinKeyWrapper>();
            services.RemoveAll<ISignatureOutboxKeyWrapper>();
            services.AddSingleton<ISignatureBlobStore, SignatureTestBlobStore>();
            services.AddSingleton<ISigningPinKeyWrapper, SignatureTestKeyWrapper>();
            services.AddSingleton<ISignatureOutboxKeyWrapper, SignatureTestKeyWrapper>();

            // Added rather than replacing the console provider, so the redaction test
            // reads the same stream the hosted API writes to App Service.
            services.AddSingleton<ILoggerProvider, CapturingLoggerProvider>();
        });
    }

    /// <summary>
    /// An authenticated client for a seeded user.
    /// </summary>
    /// <remarks>
    /// The token is issued once per username and reused. Sign-in is deliberately
    /// rate limited — 120 attempts a minute across the host, twelve per account —
    /// and a suite that logs in afresh for every test eventually trips that limit
    /// and fails whichever tests happen to run last. Caching keeps the limiter at
    /// its production settings instead of relaxing a real control to suit the
    /// tests. Tokens outlive the suite comfortably at fifteen minutes.
    /// </remarks>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(string username)
    {
        var token = await GetAccessTokenAsync(username);
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<string> GetAccessTokenAsync(string username)
    {
        await EnsureSeededAsync();
        await _tokenLock.WaitAsync();
        try
        {
            if (_tokens.TryGetValue(username, out var cached))
                return cached;

            using var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new LoginRequest(username, TestPassword));
            response.EnsureSuccessStatusCode();
            var login = await response.Content.ReadFromJsonAsync<LoginResponse>()
                ?? throw new InvalidOperationException("The test login returned no response body.");
            _tokens[username] = login.AccessToken;
            return login.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public HttpClient CreateAnonymousClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost")
    });

    /// <summary>
    /// An unauthenticated client whose requests will find seeded data. Use this
    /// for anonymous endpoints that still read the database — sign-in, for one —
    /// so the test does not depend on some earlier test having seeded first.
    /// </summary>
    public async Task<HttpClient> CreateSeededAnonymousClientAsync()
    {
        await EnsureSeededAsync();
        return CreateAnonymousClient();
    }

    public async Task AddForeignAgencyNoteToBillingPeriodAsync(int periodId, int noteId)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.ClaimLines.Add(new ServerClaimLine
        {
            NoteId = noteId,
            BillingPeriodId = periodId,
            DateOfService = new DateTime(2026, 7, 11),
            ProcedureCode = "T2021",
            Units = 4,
            ClientMaineCareId = "FOREIGN",
            RenderingProviderNpi = "2222222222",
            DiagnosisCode = "F89",
            PlaceOfService = 11
        });
        await db.SaveChangesAsync();
    }

    public async Task<(int DraftPeriodId, int SubmittedPeriodId)> CreateLegacyBillingPeriodsWithoutSnapshotsAsync()
    {
        await EnsureSeededAsync();
        await _testDataLock.WaitAsync();
        try
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var noteId = await db.Notes.MaxAsync(note => note.Id) + 1;
            var periodId = await db.BillingPeriods.MaxAsync(period => period.Id) + 1;

            db.Notes.AddRange(
                new ServerNote
                {
                    Id = noteId, PersonId = 101, AgencyId = 1, Narrative = "Legacy draft billing test",
                    EventDate = new DateTime(2098, 1, 10), Minutes = 15, Status = 6
                },
                new ServerNote
                {
                    Id = noteId + 1, PersonId = 101, AgencyId = 1, Narrative = "Legacy submitted billing test",
                    EventDate = new DateTime(2099, 1, 10), Minutes = 15, Status = 6
                });
            db.BillingPeriods.AddRange(
                LegacyPeriod(periodId, noteId, 2098, status: 0),
                LegacyPeriod(periodId + 1, noteId + 1, 2099, status: 1));
            await db.SaveChangesAsync();
            return (periodId, periodId + 1);
        }
        finally
        {
            _testDataLock.Release();
        }

        static ServerBillingPeriod LegacyPeriod(int id, int noteId, int year, int status) => new()
        {
            Id = id,
            UserId = 12,
            Month = 1,
            Year = year,
            Status = status,
            SubmittedAt = status == 1 ? DateTime.UtcNow : null,
            Lines =
            [
                new ServerClaimLine
                {
                    NoteId = noteId,
                    DateOfService = new DateTime(year, 1, 10),
                    ProcedureCode = "G9012",
                    ProcedureModifier = "HI",
                    Units = 1,
                    ChargeAmount = 25,
                    ClientMaineCareId = "111111",
                    RenderingProviderNpi = "1999999984",
                    DiagnosisCode = "F89",
                    PlaceOfService = 11,
                    ClaimSnapshotJson = null
                }
            ]
        };
    }

    public async Task<int> CreateZeroChargeDraftBillingPeriodAsync()
    {
        await EnsureSeededAsync();
        await _testDataLock.WaitAsync();
        try
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var noteId = await db.Notes.MaxAsync(note => note.Id) + 1;
            var periodId = await db.BillingPeriods.MaxAsync(period => period.Id) + 1;
            db.Notes.Add(new ServerNote
            {
                Id = noteId, PersonId = 101, AgencyId = 1, Narrative = "Zero charge billing test",
                EventDate = new DateTime(2097, 1, 10), Minutes = 15, Status = 6
            });
            db.BillingPeriods.Add(new ServerBillingPeriod
            {
                Id = periodId,
                UserId = 12,
                Month = 1,
                Year = 2097,
                Status = 0,
                Lines =
                [
                    new ServerClaimLine
                    {
                        NoteId = noteId,
                        DateOfService = new DateTime(2097, 1, 10),
                        ProcedureCode = "G9012",
                        ProcedureModifier = "HI",
                        Units = 1,
                        ChargeAmount = 0,
                        ClientMaineCareId = "111111",
                        RenderingProviderNpi = "1999999984",
                        DiagnosisCode = "F89",
                        PlaceOfService = 11,
                        ClaimSnapshotJson = ClaimSnapshot(1, 101, "Person", "One", "111111", "10 Test Street", "Portland", "04101", "SATITEST1", "Agency One", "111111111", "1 First Street", "Portland", "04101", "2075550101")
                    }
                ]
            });
            await db.SaveChangesAsync();
            return periodId;
        }
        finally
        {
            _testDataLock.Release();
        }
    }

    public async Task ChangeUserRoleAsync(int userId, string role)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Id == userId);
        user.Role = role;
        await db.SaveChangesAsync();
    }

    public async Task ChangeUserPermissionsAsync(int userId, UserPermissions permissions)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Id == userId);
        user.Permissions = permissions;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The stored workflow status, for asserting that a refused supervisory action changed
    /// nothing. A 403 that had already written the transition would otherwise go unnoticed.
    /// </summary>
    public async Task<int?> GetNoteStatusAsync(int noteId)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        return await db.Notes.AsNoTracking()
            .Where(note => note.Id == noteId)
            .Select(note => note.Status)
            .SingleAsync();
    }

    public async Task<IReadOnlyList<AuditEventSnapshot>> GetAuditEventsAsync(string action)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        return await db.AuditEvents.AsNoTracking()
            .Where(candidate => candidate.Action == action)
            .OrderBy(candidate => candidate.Id)
            .Select(candidate => new AuditEventSnapshot(
                candidate.AgencyId,
                candidate.ActorUserId,
                candidate.Action,
                candidate.ResourceType,
                candidate.ResourceId,
                candidate.CorrelationId,
                candidate.MetadataJson))
            .ToListAsync();
    }

    public async Task<int> GetEdiGenerationCountAsync(string idempotencyKey)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var normalizedKey = Guid.Parse(idempotencyKey).ToString("N");
        return await db.EdiGenerations.AsNoTracking()
            .CountAsync(candidate => candidate.IdempotencyKey == normalizedKey);
    }

    public async Task<int> GetGeneratedSubmissionEventCountAsync(int billingPeriodId)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        return await db.BillingSubmissionEvents.AsNoTracking().CountAsync(candidate =>
            candidate.BillingPeriodId == billingPeriodId &&
            candidate.Stage == BillingSubmissionStage.Generated);
    }

    public async Task TryToModifyFirstAuditEventAsync()
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var auditEvent = await db.AuditEvents.FirstAsync();
        auditEvent.Action = "tampered";
        await db.SaveChangesAsync();
    }

    public async Task<int> GetAssessmentRevisionAsync(int assessmentId)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        return await db.ComprehensiveAssessments
            .Where(candidate => candidate.Id == assessmentId)
            .Select(candidate => candidate.Revision)
            .SingleAsync();
    }

    public async Task TryToModifyFirstPersonVersionAsync()
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var version = await db.PersonVersions.FirstAsync();
        version.ChangeKind = "Tampered";
        await db.SaveChangesAsync();
    }

    private async Task EnsureSeededAsync()
    {
        await _seedLock.WaitAsync();
        try
        {
            if (_seeded)
                return;

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await db.Database.EnsureCreatedAsync();
            // EnsureCreated omits read-only views. Mirror the migration's narrow
            // signing projection without exposing the full clinical entity.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE VIEW IF NOT EXISTS SignatureSourceDocuments AS
                SELECT Id, AgencyId, PersonId, Kind, CycleStart, Origin, ContentSha256,
                       ByteCount, BlankFieldsJson, SupersededByArtifactId
                FROM DocumentArtifacts
                """);
            var verifier = scope.ServiceProvider.GetRequiredService<PasswordVerifier>();

            db.Agencies.AddRange(
                new ServerAgency
                {
                    Id = 1, Name = "Agency One", Npi = "1999999984", TaxId = "111111111",
                    Street = "1 First Street", City = "Portland", State = "ME", Zip = "04101",
                    BillingProcedureCode = "G9012", BillingModifier = "HI", BillingUnitRate = 25m,
                    EdiSubmitterId = "SATITEST1", EdiPayerName = "MEDICAID MAINE",
                    EdiPayerId = "MCDME", EdiContactName = "Test Billing",
                    EdiContactPhone = "2075550101"
                },
                new ServerAgency
                {
                    Id = 2, Name = "Agency Two", Npi = "1999999984", TaxId = "222222222",
                    Street = "2 Second Street", City = "Bangor", State = "ME", Zip = "04401",
                    BillingProcedureCode = "G9012", BillingModifier = "HI", BillingUnitRate = 25m,
                    EdiSubmitterId = "SATITEST2", EdiPayerName = "MEDICAID MAINE",
                    EdiPayerId = "MCDME", EdiContactName = "Test Billing",
                    EdiContactPhone = "2075550102"
                });
            db.Users.AddRange(
                CreateUser(verifier, 11, "admin-one", "Admin", 1),
                CreateUser(verifier, 12, "case-manager-one", "CaseManager", 1, 13),
                CreateUser(verifier, 13, "supervisor-one", "Supervisor", 1),
                CreateUser(verifier, 14, "stale-badge-user", "CaseManager", 1),
                CreateUser(verifier, 15, "billing-only-one", "CaseManager", 1,
                    permissions: UserPermissions.Billing),
                CreateUser(verifier, 16, "admin-without-billing-one", "Admin", 1,
                    permissions: UserPermissions.Administration),
                // Legacy Director: agency-wide supervisory reach, no administration. Seeded
                // through FromLegacyRole so the fixture cannot drift from the backfill.
                CreateUser(verifier, 17, "director-one", "Director", 1),
                // A demoted supervisor: still named as user 19's supervisor, but carrying only
                // case management. This pair is what makes the supervision gates testable. Every
                // query beneath those gates is scoped by SupervisorId, so an ordinary case
                // manager sees an empty result whether the gate is there or not, and a denial
                // test built on one proves nothing. This actor WOULD be handed real rows if the
                // gate were removed, so the test fails when the gate does.
                CreateUser(verifier, 18, "demoted-supervisor-one", "CaseManager", 1,
                    permissions: UserPermissions.CaseManagement),
                CreateUser(verifier, 19, "supervisee-of-demoted-one", "CaseManager", 1, 18),
                CreateUser(verifier, 21, "admin-two", "Admin", 2),
                CreateUser(verifier, 22, "case-manager-two", "CaseManager", 2, 23),
                CreateUser(verifier, 23, "supervisor-two", "Supervisor", 2),
                CreateUser(verifier, 31, "platform-operator", "PlatformOperator", 1));
            db.People.AddRange(
                new ServerPerson
                {
                    Id = 101, UserId = 12, AgencyId = 1, FirstName = "Person", LastName = "One",
                    BirthDate = new DateTime(1990, 1, 1), EffectiveDate = CycleStart,
                    Journal = "Agency one journal", MaineCareId = "111111",
                    DiagnosisCode = "F89", PlaceOfService = 11,
                    BillingStreet = "10 Test Street", BillingCity = "Portland",
                    BillingState = "ME", BillingZip = "04101",
                    Forms =
                    [
                        CompliantForm(101, "PCP"),
                        CompliantForm(101, "ComprehensiveAssessment"),
                        CompliantForm(101, "Reclassification"),
                        CompliantForm(101, "SafetyPlan")
                    ]
                },
                new ServerPerson
                {
                    Id = 102, UserId = 12, AgencyId = 1, FirstName = "Lifecycle", LastName = "Example",
                    BirthDate = new DateTime(1985, 5, 6), EffectiveDate = new DateTime(2025, 5, 6),
                    Bio = "Initial lifecycle biography.", Journal = "Initial private journal.",
                    MaineCareId = "333333", DiagnosisCode = "F89", PlaceOfService = 11,
                    HasGuardian = true, GuardianName = "Original Guardian", Address = "10 First Avenue",
                    BillingStreet = "10 First Avenue", BillingCity = "Portland",
                    BillingState = "ME", BillingZip = "04101",
                    DayProgramCount = 1
                },
                // Owned by the demoted supervisor's supervisee, so a supervision gate that stopped
                // working would expose a real consumer and a real note rather than an empty list.
                // Seeded declaratively rather than through a helper so the counts stay fixed
                // regardless of which tests have run.
                new ServerPerson
                {
                    Id = 103, UserId = 19, AgencyId = 1, FirstName = "Supervisee", LastName = "Consumer",
                    BirthDate = new DateTime(1992, 3, 4), EffectiveDate = CycleStart,
                    Journal = "Supervisee caseload journal", MaineCareId = "444444",
                    DiagnosisCode = "F89", PlaceOfService = 11,
                    BillingStreet = "30 Test Street", BillingCity = "Augusta",
                    BillingState = "ME", BillingZip = "04330",
                    Forms =
                    [
                        CompliantForm(103, "PCP"),
                        CompliantForm(103, "ComprehensiveAssessment"),
                        CompliantForm(103, "Reclassification"),
                        CompliantForm(103, "SafetyPlan")
                    ]
                },
                new ServerPerson
                {
                    Id = 201, UserId = 22, AgencyId = 2, FirstName = "Person", LastName = "Two",
                    BirthDate = new DateTime(1990, 1, 1), Journal = "Agency two journal",
                    MaineCareId = "222222", DiagnosisCode = "F89", PlaceOfService = 11,
                    BillingStreet = "20 Test Street", BillingCity = "Bangor",
                    BillingState = "ME", BillingZip = "04401"
                });
            db.Providers.AddRange(
                new ServerProvider { Id = 301, AgencyId = 1, Type = "Other", Name = "Provider One" },
                new ServerProvider { Id = 401, AgencyId = 2, Type = "Other", Name = "Provider Two" });
            db.Notes.AddRange(
                new ServerNote
                {
                    Id = 501, PersonId = 101, AgencyId = 1, Narrative = "Agency one note",
                    EventDate = new DateTime(2026, 7, 10), Minutes = 60, Status = 2
                },
                // Logged, so it sits in the supervisory review queue and is a legal target for
                // approve, approve-override, and return. Dated in a prior month so it does not
                // move the administrator dashboard's notes-this-month count.
                new ServerNote
                {
                    Id = 507, PersonId = 103, AgencyId = 1,
                    Narrative = "Supervisee note awaiting review",
                    EventDate = new DateTime(2026, 7, 15), Minutes = 45, Status = 2
                },
                new ServerNote
                {
                    Id = 601, PersonId = 201, AgencyId = 2, Narrative = "Agency two note",
                    EventDate = new DateTime(2026, 7, 11), Minutes = 60, Status = 2
                },
                new ServerNote
                {
                    Id = 602, PersonId = 201, AgencyId = 2, Narrative = "Unbilled agency two note",
                    EventDate = new DateTime(2026, 7, 12), Minutes = 60, Status = 6
                },
                new ServerNote
                {
                    Id = 603, PersonId = 201, AgencyId = 2, Narrative = "Submitted agency two billing note",
                    EventDate = new DateTime(2026, 8, 12), Minutes = 20, Status = 6
                },
                new ServerNote
                {
                    Id = 502, PersonId = 101, AgencyId = 1, Narrative = "Approved agency one note",
                    // This is the one current-month record used by the administrator
                    // overview test. Keep it relative so the fixture remains valid
                    // after the month in which it was first written.
                    EventDate = DateTime.Today, Minutes = 60, Status = 6
                },
                new ServerNote
                {
                    Id = 503, PersonId = 101, AgencyId = 1, Narrative = "Editable concurrency note",
                    EventDate = new DateTime(2026, 7, 13), Minutes = 30, Status = 1
                },
                new ServerNote
                {
                    Id = 504, PersonId = 101, AgencyId = 1, Narrative = "Delete concurrency note",
                    EventDate = new DateTime(2026, 7, 14), Minutes = 30, Status = 1
                },
                new ServerNote
                {
                    Id = 505, PersonId = 101, AgencyId = 1, Narrative = "Supervisor concurrency note",
                    EventDate = new DateTime(2026, 7, 15), Minutes = 30, Status = 2
                },
                new ServerNote
                {
                    Id = 506, PersonId = 101, AgencyId = 1, Narrative = "Legacy client guard note",
                    EventDate = new DateTime(2026, 7, 16), Minutes = 30, Status = 1
                });
            db.ComprehensiveAssessments.AddRange(
                new ServerComprehensiveAssessment
                {
                    Id = 701, PersonId = 101, AuthorUserId = 12, Status = "Draft", Version = 1,
                    CreatedAt = new DateTime(2026, 7, 1), UpdatedAt = new DateTime(2026, 7, 1),
                    DocumentJson = "{\"agency\":\"one\"}"
                },
                new ServerComprehensiveAssessment
                {
                    Id = 702, PersonId = 101, AuthorUserId = 12, Status = "Draft", Version = 2,
                    CreatedAt = new DateTime(2026, 7, 2), UpdatedAt = new DateTime(2026, 7, 2),
                    DocumentJson = "{\"concurrencyTest\":true}"
                },
                new ServerComprehensiveAssessment
                {
                    Id = 801, PersonId = 201, AuthorUserId = 22, Status = "Draft", Version = 1,
                    CreatedAt = new DateTime(2026, 7, 1), UpdatedAt = new DateTime(2026, 7, 1),
                    DocumentJson = "{\"agency\":\"two\"}"
                });
            db.AtRequests.AddRange(
                new ServerAtRequest
                {
                    Id = 901, PersonId = 101, ClientName = "Person One", CaseManagerName = "case-manager-one",
                    Status = "Development", SnapshotPng = [1, 2, 3]
                },
                new ServerAtRequest
                {
                    Id = 902, PersonId = 101, ClientName = "Person One", CaseManagerName = "case-manager-one",
                    VendorName = "Original Vendor", Status = "Development",
                    Items = [new ServerAtRequestItem { Id = 905, Name = "Original Item", ItemCost = 25m, Quantity = 1 }]
                },
                new ServerAtRequest
                {
                    Id = 903, PersonId = 101, ClientName = "Person One", CaseManagerName = "case-manager-one",
                    VendorName = "Delete Guard Vendor", Status = "Development"
                },
                new ServerAtRequest
                {
                    Id = 904, PersonId = 101, ClientName = "Person One", CaseManagerName = "case-manager-one",
                    VendorName = "Legacy Guard Vendor", Status = "Development"
                },
                new ServerAtRequest
                {
                    Id = 1001, PersonId = 201, ClientName = "Person Two", CaseManagerName = "case-manager-two",
                    Status = "Development", SnapshotPng = [4, 5, 6]
                });
            db.BillingPeriods.AddRange(
                new ServerBillingPeriod
                {
                    Id = 1101, UserId = 12, Month = 7, Year = 2026, Status = 0,
                    Lines =
                    [
                        new ServerClaimLine
                        {
                            Id = 1301, NoteId = 501, DateOfService = new DateTime(2026, 7, 10),
                            ProcedureCode = "G9012", ProcedureModifier = "HI", Units = 4, ChargeAmount = 100m, ClientMaineCareId = "111111",
                            RenderingProviderNpi = "1999999984", DiagnosisCode = "F89", PlaceOfService = 11,
                            ClaimSnapshotJson = ClaimSnapshot(1, 101, "Person", "One", "111111", "10 Test Street", "Portland", "04101", "SATITEST1", "Agency One", "111111111", "1 First Street", "Portland", "04101", "2075550101")
                        }
                    ]
                },
                new ServerBillingPeriod
                {
                    Id = 1201, UserId = 22, Month = 7, Year = 2026, Status = 0,
                    Lines =
                    [
                        new ServerClaimLine
                        {
                            Id = 1401, NoteId = 601, DateOfService = new DateTime(2026, 7, 11),
                            ProcedureCode = "G9012", ProcedureModifier = "HI", Units = 4, ChargeAmount = 100m, ClientMaineCareId = "222222",
                            RenderingProviderNpi = "1999999984", DiagnosisCode = "F89", PlaceOfService = 11,
                            ClaimSnapshotJson = ClaimSnapshot(2, 201, "Person", "Two", "222222", "20 Test Street", "Bangor", "04401", "SATITEST2", "Agency Two", "222222222", "2 Second Street", "Bangor", "04401", "2075550102")
                        }
                    ]
                },
                new ServerBillingPeriod
                {
                    Id = 1202, UserId = 22, Month = 8, Year = 2026, Status = 1,
                    SubmittedAt = new DateTime(2026, 8, 13, 1, 0, 0, DateTimeKind.Utc),
                    Lines =
                    [
                        new ServerClaimLine
                        {
                            Id = 1402, NoteId = 603, DateOfService = new DateTime(2026, 8, 12),
                            ProcedureCode = "G9012", ProcedureModifier = "HI", Units = 1.33m, ChargeAmount = 33.25m, ClientMaineCareId = "222222",
                            RenderingProviderNpi = "1999999984", DiagnosisCode = "F89", PlaceOfService = 11,
                            ClaimSnapshotJson = ClaimSnapshot(2, 201, "Person", "Two", "222222", "20 Test Street", "Bangor", "04401", "SATITEST2", "Agency Two", "222222222", "2 Second Street", "Bangor", "04401", "2075550102")
                        }
                    ]
                });
            db.BillingSubmissionEvents.AddRange(
                new ServerBillingSubmissionEvent
                {
                    Id = 1501, AgencyId = 1, BillingPeriodId = 1101,
                    OccurredAtUtc = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc),
                    Stage = BillingSubmissionStage.TransportFailed,
                    Reference = "SYN-ONE", ResponseType = "Transport", ResponseCode = "TIMEOUT",
                    Explanation = "Synthetic agency-one failure.", IsSynthetic = true
                },
                new ServerBillingSubmissionEvent
                {
                    Id = 1502, AgencyId = 2, BillingPeriodId = 1202,
                    OccurredAtUtc = new DateTime(2026, 8, 14, 13, 0, 0, DateTimeKind.Utc),
                    Stage = BillingSubmissionStage.ClaimAccepted,
                    Reference = "SYN-TWO", ResponseType = "277CA", ResponseCode = "A1",
                    Explanation = "Synthetic agency-two acceptance.", IsSynthetic = true
                });
            db.RemittanceClaimOutcomes.AddRange(
                new ServerRemittanceClaimOutcome
                {
                    Id = 1601, AgencyId = 1, BillingPeriodId = 1101,
                    ClaimReference = "1101-501", PayerName = "Synthetic Payer",
                    ReceivedAtUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
                    PaymentDate = new DateTime(2026, 8, 15), Status = RemittanceClaimStatus.Denied,
                    BilledAmount = 100m, AllowedAmount = 0m, PaidAmount = 0m,
                    AdjustmentAmount = 100m, PatientResponsibilityAmount = 0m,
                    ReasonCode = "DEMO-DENY", Explanation = "Synthetic agency-one denial.",
                    IsSynthetic = true
                },
                new ServerRemittanceClaimOutcome
                {
                    Id = 1602, AgencyId = 2, BillingPeriodId = 1202,
                    ClaimReference = "1202-603", PayerName = "Synthetic Payer",
                    ReceivedAtUtc = new DateTime(2026, 8, 15, 13, 0, 0, DateTimeKind.Utc),
                    PaymentDate = new DateTime(2026, 8, 15), Status = RemittanceClaimStatus.Paid,
                    BilledAmount = 33.25m, AllowedAmount = 26.60m, PaidAmount = 26.60m,
                    AdjustmentAmount = 6.65m, PatientResponsibilityAmount = 0m,
                    PaymentReference = "SYN-EFT-TWO", Explanation = "Synthetic agency-two payment.",
                    IsSynthetic = true
                });
            db.RemittanceDeposits.AddRange(
                new ServerRemittanceDeposit
                {
                    Id = 1701, AgencyId = 1, PaymentReference = "SYN-EFT-ONE",
                    PayerName = "Synthetic Payer",
                    ReceivedAtUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
                    PaymentDate = new DateTime(2026, 8, 15), ClaimPaymentAmount = 26.60m,
                    ProviderLevelAdjustmentAmount = 0m,
                    ProviderLevelAdjustmentSummary = "No provider-level adjustment",
                    RemittancePaymentAmount = 26.60m, EftDepositAmount = 26.60m,
                    IsSynthetic = true
                },
                new ServerRemittanceDeposit
                {
                    Id = 1702, AgencyId = 2, PaymentReference = "SYN-EFT-TWO",
                    PayerName = "Synthetic Payer",
                    ReceivedAtUtc = new DateTime(2026, 8, 15, 13, 0, 0, DateTimeKind.Utc),
                    PaymentDate = new DateTime(2026, 8, 15), ClaimPaymentAmount = 26.60m,
                    ProviderLevelAdjustmentAmount = -1m,
                    ProviderLevelAdjustmentSummary = "Synthetic takeback",
                    RemittancePaymentAmount = 25.60m, EftDepositAmount = 25.50m,
                    IsSynthetic = true
                });
            await db.SaveChangesAsync();
            _seeded = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    private static ServerUser CreateUser(
        PasswordVerifier verifier,
        int id,
        string username,
        string role,
        int agencyId,
        int? supervisorId = null,
        UserPermissions? permissions = null)
    {
        var credential = verifier.Hash(TestPassword);
        return new ServerUser
        {
            Id = id,
            Username = username,
            DisplayName = username,
            Role = role,
            Permissions = permissions ?? UserPermissionRules.FromLegacyRole(role),
            AgencyId = agencyId,
            SupervisorId = supervisorId,
            PasswordHash = credential.Hash,
            Salt = credential.Salt
        };
    }

    public async Task<TestConsumerSeed> CreateTestConsumerGraphAsync(
        int agencyId = 1,
        bool withClaimLine = false)
    {
        await EnsureSeededAsync();
        await _testDataLock.WaitAsync();
        try
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var userId = agencyId == 1 ? 12 : 22;
            var person = new ServerPerson
            {
                UserId = userId,
                AgencyId = agencyId,
                FirstName = "Disposable",
                LastName = $"Test {Guid.NewGuid():N}",
                BirthDate = new DateTime(1990, 1, 1),
                EffectiveDate = CycleStart,
                IsTestData = true,
                Revision = 3
            };
            db.People.Add(person);
            var provider = new ServerProvider
            {
                AgencyId = agencyId,
                Type = "Healthcare",
                Name = $"Synthetic clinician {Guid.NewGuid():N}",
                MedicalKind = "Individual"
            };
            db.Providers.Add(provider);
            await db.SaveChangesAsync();

            var note = new ServerNote
            {
                PersonId = person.Id,
                AgencyId = agencyId,
                Narrative = "Synthetic deletion fixture",
                EventDate = DateTime.Today,
                Minutes = 15,
                Status = 1
            };
            var review = new ServerReviewItem
            {
                PersonId = person.Id,
                CycleAnchor = CycleStart,
                Quarter = 1,
                Category = "Medical",
                Appointment = new ServerAppointment
                {
                    Date = DateTime.Today,
                    ProviderName = "Test provider"
                }
            };
            var atRequest = new ServerAtRequest
            {
                PersonId = person.Id,
                ClientName = "Disposable Test",
                CaseManagerName = "case-manager",
                Items =
                [
                    new ServerAtRequestItem
                    {
                        Name = "Test item",
                        ItemCost = 1m,
                        Quantity = 1
                    }
                ]
            };
            var form = new ServerForm
            {
                PersonId = person.Id,
                Type = "PCP",
                DueDate = DateTime.Today.AddDays(30),
                CompletedDate = DateTime.Today
            };
            form.Attestations.Add(new ServerFormAttestation
            {
                Form = form,
                Kind = "Attested",
                CompletedOn = DateTime.Today,
                ActorKind = "CaseManager",
                ActorUserId = userId,
                RecordedAtUtc = DateTime.UtcNow,
                PrerequisiteStateJson = FormAttestationRules.NoPrerequisitesStateJson
            });
            db.Forms.Add(form);
            db.Notes.Add(note);
            db.PersonContacts.Add(new ServerPersonContact
            {
                PersonId = person.Id,
                FirstName = "Test",
                LastName = "Contact"
            });
            db.PersonProviders.Add(new ServerPersonProvider
            {
                PersonId = person.Id,
                ProviderId = provider.Id,
                Role = "Test clinician"
            });
            db.ReviewItems.Add(review);
            db.ComprehensiveAssessments.Add(new ServerComprehensiveAssessment
            {
                PersonId = person.Id,
                AuthorUserId = userId,
                DocumentJson = "{\"testData\":true}"
            });
            db.AtRequests.Add(atRequest);
            db.PersonVersions.Add(new ServerPersonVersion
            {
                PersonId = person.Id,
                Person = person,
                AgencyId = agencyId,
                ActorUserId = userId,
                ActorDisplayName = "Test fixture",
                Version = 1,
                ChangeKind = "Created",
                ChangedAtUtc = DateTime.UtcNow,
                CorrelationId = $"fixture-{Guid.NewGuid():N}",
                SnapshotGzip = [1],
                ChangesGzip = [1]
            });
            db.AuditEvents.Add(new ServerAuditEvent
            {
                AgencyId = agencyId,
                ActorUserId = userId,
                Action = "test-data.fixture-created",
                ResourceType = "Person",
                ResourceId = person.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CorrelationId = $"fixture-{Guid.NewGuid():N}"
            });
            await db.SaveChangesAsync();

            if (withClaimLine)
            {
                db.ClaimLines.Add(new ServerClaimLine
                {
                    NoteId = note.Id,
                    BillingPeriodId = agencyId == 1 ? 1101 : 1201,
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
                await db.SaveChangesAsync();
            }

            return new TestConsumerSeed(person.Id, person.Revision);
        }
        finally
        {
            _testDataLock.Release();
        }
    }

    public async Task<TestConsumerGraphSnapshot> GetTestConsumerGraphAsync(int personId)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        return new TestConsumerGraphSnapshot(
            await db.People.CountAsync(candidate => candidate.Id == personId),
            await db.Forms.CountAsync(candidate => candidate.PersonId == personId),
            await db.FormAttestations.CountAsync(attestation => db.Forms.Any(form =>
                form.Id == attestation.FormId && form.PersonId == personId)),
            await db.Notes.CountAsync(candidate => candidate.PersonId == personId),
            await db.PersonContacts.CountAsync(candidate => candidate.PersonId == personId),
            await db.PersonProviders.CountAsync(candidate => candidate.PersonId == personId),
            await db.ReviewItems.CountAsync(candidate => candidate.PersonId == personId),
            await db.Appointments.CountAsync(appointment => db.ReviewItems.Any(review =>
                review.Id == appointment.ReviewItemId && review.PersonId == personId)),
            await db.ComprehensiveAssessments.CountAsync(candidate => candidate.PersonId == personId),
            await db.AtRequests.CountAsync(candidate => candidate.PersonId == personId),
            await db.AtRequestItems.CountAsync(item => db.AtRequests.Any(request =>
                request.Id == item.ATRequestId && request.PersonId == personId)),
            await db.PersonVersions.CountAsync(candidate => candidate.PersonId == personId),
            await db.ClaimLines.CountAsync(line => db.Notes.Any(note =>
                note.Id == line.NoteId && note.PersonId == personId)),
            await db.AuditEvents.CountAsync(candidate =>
                candidate.ResourceType == "Person" && candidate.ResourceId == personId.ToString()));
    }

    public async Task RemoveTestConsumerGraphAsync(int personId)
    {
        await EnsureSeededAsync();
        await _testDataLock.WaitAsync();
        try
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await db.ClaimLines.Where(line => db.Notes.Any(note =>
                note.Id == line.NoteId && note.PersonId == personId)).ExecuteDeleteAsync();
            await db.Appointments.Where(appointment => db.ReviewItems.Any(review =>
                review.Id == appointment.ReviewItemId && review.PersonId == personId)).ExecuteDeleteAsync();
            await db.ReviewItems.Where(candidate => candidate.PersonId == personId).ExecuteDeleteAsync();
            await db.PersonContacts.Where(candidate => candidate.PersonId == personId).ExecuteDeleteAsync();
            await db.PersonProviders.Where(candidate => candidate.PersonId == personId).ExecuteDeleteAsync();
            await db.FormAttestations.Where(attestation => db.Forms.Any(form =>
                form.Id == attestation.FormId && form.PersonId == personId)).ExecuteDeleteAsync();
            await db.Forms.Where(candidate => candidate.PersonId == personId).ExecuteDeleteAsync();
            await db.AtRequestItems.Where(item => db.AtRequests.Any(request =>
                request.Id == item.ATRequestId && request.PersonId == personId)).ExecuteDeleteAsync();
            await db.AtRequests.Where(candidate => candidate.PersonId == personId).ExecuteDeleteAsync();
            await db.ComprehensiveAssessments.Where(candidate => candidate.PersonId == personId).ExecuteDeleteAsync();
            await db.Notes.Where(candidate => candidate.PersonId == personId).ExecuteDeleteAsync();
            await db.PersonVersions.Where(candidate => candidate.PersonId == personId).ExecuteDeleteAsync();
            await db.People.Where(candidate => candidate.Id == personId).ExecuteDeleteAsync();
            await db.AuditEvents.Where(candidate =>
                candidate.ResourceType == "Person" && candidate.ResourceId == personId.ToString()).ExecuteDeleteAsync();
        }
        finally
        {
            _testDataLock.Release();
        }
    }

    public async Task SetTestConsumerMarkerAsync(int personId, bool isTestData)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var person = await db.People.SingleAsync(candidate => candidate.Id == personId);
        person.IsTestData = isTestData;
        await db.SaveChangesAsync();
    }

    public async Task<int> CreateBillingWorkflowPersonAsync()
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var nextId = await db.People.MaxAsync(person => person.Id) + 1;
        db.People.Add(new ServerPerson
        {
            Id = nextId,
            UserId = 12,
            AgencyId = 1,
            FirstName = "Workflow",
            LastName = nextId.ToString(),
            BirthDate = new DateTime(1990, 1, 1),
            EffectiveDate = CycleStart,
            MaineCareId = $"9{nextId:D5}",
            DiagnosisCode = "F89",
            PlaceOfService = 11,
            BillingStreet = "10 Test Street",
            BillingCity = "Portland",
            BillingState = "ME",
            BillingZip = "04101",
            Forms =
            [
                CompliantForm(nextId, "PCP"),
                CompliantForm(nextId, "ComprehensiveAssessment"),
                CompliantForm(nextId, "Reclassification"),
                CompliantForm(nextId, "SafetyPlan")
            ]
        });
        await db.SaveChangesAsync();
        return nextId;
    }

    /// <summary>
    /// A fully billable approved note on its own new person. Billing tests that
    /// share a seeded note compete for the one claim line it is allowed, so each
    /// caller gets a private note instead.
    /// </summary>
    public async Task<int> CreateApprovedBillableNoteAsync(
        bool complianceOverride = false,
        string? overrideReason = null,
        int minutes = 60)
    {
        var personId = await CreateBillingWorkflowPersonAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var nextId = await db.Notes.MaxAsync(note => note.Id) + 1;
        var approvedAt = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
        db.Notes.Add(new ServerNote
        {
            Id = nextId,
            PersonId = personId,
            AgencyId = 1,
            Narrative = "Approved billable note",
            EventDate = new DateTime(2026, 8, 3),
            Minutes = minutes,
            Status = 6,
            ApprovedById = 13,
            ApprovedAt = approvedAt,
            ComplianceOverride = complianceOverride,
            OverrideReason = complianceOverride ? overrideReason : null,
            OverrideApprovedById = complianceOverride ? 13 : null,
            OverrideApprovedAt = complianceOverride ? approvedAt : null
        });
        await db.SaveChangesAsync();
        return nextId;
    }

    /// <summary>
    /// A note for person 101 (agency one, case-manager-one) in an arbitrary status,
    /// so a workflow test can start from any point in the pipeline. No start time,
    /// so seeded notes never contend for service minutes.
    /// </summary>
    public async Task<int> CreateNoteInStatusAsync(int status, int personId = 101, int minutes = 60, int? noteType = null)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var nextId = await db.Notes.MaxAsync(note => note.Id) + 1;
        db.Notes.Add(new ServerNote
        {
            Id = nextId,
            PersonId = personId,
            AgencyId = personId == 201 ? 2 : 1,
            Narrative = $"Workflow note in status {status}",
            EventDate = new DateTime(2026, 8, 3),
            Minutes = minutes,
            NoteType = noteType,
            Status = status
        });
        await db.SaveChangesAsync();
        return nextId;
    }

    public async Task<(int? Status, int Revision)> GetNoteStateAsync(int noteId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var note = await db.Notes.AsNoTracking().SingleAsync(candidate => candidate.Id == noteId);
        return (note.Status, note.Revision);
    }

    public async Task<int> CreateNonCompliantReviewNoteAsync()
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var nextId = await db.Notes.MaxAsync(note => note.Id) + 1;
        var overduePcp = DateTime.Today.AddDays(-2);
        var overdueAssessment = DateTime.Today.AddDays(-1);
        if (!await db.Forms.AnyAsync(form => form.PersonId == 102 &&
                form.Type == "PCP" && form.DueDate == overduePcp))
            db.Forms.Add(new ServerForm
            {
                PersonId = 102,
                Type = "PCP",
                DueDate = overduePcp,
                // Outstanding. There is no longer a flag that could claim otherwise —
                // ServerForm.IsCompliant is derived from this date.
                CompletedDate = null
            });
        if (!await db.Forms.AnyAsync(form => form.PersonId == 102 &&
                form.Type == "ComprehensiveAssessment" && form.DueDate == overdueAssessment))
            db.Forms.Add(new ServerForm
            {
                PersonId = 102,
                Type = "ComprehensiveAssessment",
                DueDate = overdueAssessment,
                CompletedDate = null,
            });
        db.Notes.Add(new ServerNote
        {
            Id = nextId,
            PersonId = 102,
            AgencyId = 1,
            Narrative = "Non-compliant review explanation test",
            EventDate = new DateTime(2026, 8, 12),
            Minutes = 30,
            Status = 2
        });
        await db.SaveChangesAsync();
        return nextId;
    }

    public async Task<string?> GetLatestFormAttestationReasonAsync(int formId)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        return await db.FormAttestations.AsNoTracking()
            .Where(candidate => candidate.FormId == formId && candidate.Kind == "Attested")
            .OrderByDescending(candidate => candidate.Id)
            .Select(candidate => candidate.Reason)
            .FirstOrDefaultAsync();
    }

    public async Task<int> CreatePendingAttestationEvidenceAsync()
    {
        _ = await CreateNonCompliantReviewNoteAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var person = await db.People.AsNoTracking().SingleAsync(candidate => candidate.Id == 102);
        var form = await db.Forms.AsNoTracking().SingleAsync(candidate =>
            candidate.PersonId == 102 &&
            candidate.Type == nameof(FormType.PCP) &&
            candidate.DueDate == DateTime.Today.AddDays(-2));
        var cycle = FormAttestationRules.ResolveCycle(person.EffectiveDate!.Value, form.DueDate)!.Value;
        var eventDate = cycle.CycleStart > DateTime.Today.AddDays(-3)
            ? cycle.CycleStart
            : DateTime.Today.AddDays(-3);
        var nextId = await db.Notes.MaxAsync(note => note.Id) + 1;
        db.Notes.Add(new ServerNote
        {
            Id = nextId,
            PersonId = person.Id,
            AgencyId = person.AgencyId,
            Narrative = "Pending attestation projection test",
            EventDate = eventDate,
            Minutes = 15,
            Status = (int)NoteStatus.Logged,
            NoteType = (int)NoteType.Form,
            FormType = (int)FormType.PCP
        });
        await db.SaveChangesAsync();
        return nextId;
    }

    public async Task DeleteDocumentArtifactsAsync(int personId, AnnualDocumentKind kind)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await db.DocumentArtifacts
            .Where(artifact => artifact.PersonId == personId && artifact.Kind == kind.ToString())
            .ExecuteDeleteAsync();
    }

    public async Task<int> CreateOutstandingFormAsync(int personId, string type)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var person = await db.People.SingleAsync(candidate => candidate.Id == personId);
        person.EffectiveDate ??= DateTime.Today.AddMonths(-1);
        var cycleStart = AnnualDocumentCycle.CurrentStart(person.EffectiveDate.Value, DateTime.Today);
        var dueDate = cycleStart.AddMonths(6);
        var existing = await db.Forms.SingleOrDefaultAsync(form =>
            form.PersonId == personId && form.Type == type && form.DueDate == dueDate);
        if (existing is not null)
        {
            existing.CompletedDate = null;
            await db.SaveChangesAsync();
            return existing.Id;
        }
        var form = new ServerForm
        {
            PersonId = personId,
            Type = type,
            DueDate = dueDate,
            CompletedDate = null
        };
        db.Forms.Add(form);
        await db.SaveChangesAsync();
        return form.Id;
    }

    public async Task<int> CreateFutureIncompleteReviewNoteAsync()
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var personId = await db.People.MaxAsync(person => person.Id) + 1;
        var noteId = await db.Notes.MaxAsync(note => note.Id) + 1;
        var futureDueDate = DateTime.Today.AddMonths(4);
        var person = new ServerPerson
        {
            Id = personId,
            UserId = 12,
            AgencyId = 1,
            FirstName = "Future",
            LastName = "Documents",
            BirthDate = new DateTime(1990, 1, 1),
            EffectiveDate = DateTime.Today.AddMonths(-1),
            Forms =
            [
                FutureIncompleteForm(personId, "PCP", futureDueDate),
                FutureIncompleteForm(personId, "ComprehensiveAssessment", futureDueDate),
                FutureIncompleteForm(personId, "Reclassification", futureDueDate),
                FutureIncompleteForm(personId, "SafetyPlan", futureDueDate)
            ]
        };
        db.People.Add(person);
        db.Notes.Add(new ServerNote
        {
            Id = noteId,
            PersonId = personId,
            AgencyId = 1,
            Narrative = "Future documents must not block this note.",
            EventDate = DateTime.Today,
            Minutes = 30,
            Status = 2
        });
        await db.SaveChangesAsync();
        return noteId;
    }

    /// <summary>
    /// A relative anchor for the billable seeded people.
    /// </summary>
    /// <remarks>
    /// Relative to today so the fixtures remain representative at any run date.
    /// </remarks>
    private static DateTime CycleStart => DateTime.Today.AddMonths(-1);

    private static ServerForm CompliantForm(int personId, string type) => new()
    {
        PersonId = personId,
        Type = type,
        DueDate = CycleStart.AddMonths(6),
        CompletedDate = CycleStart.AddDays(1)
    };

    private static ServerForm FutureIncompleteForm(
        int personId,
        string type,
        DateTime dueDate) => new()
    {
        PersonId = personId,
        Type = type,
        DueDate = dueDate,
        CompletedDate = null
    };

    private static string ClaimSnapshot(
        int agencyId, int personId, string firstName, string lastName, string memberId,
        string subscriberStreet, string city, string zip, string submitterId,
        string providerName, string taxId, string providerStreet, string providerCity,
        string providerZip, string contactPhone) => ProfessionalClaimSnapshotCodec.Serialize(new(
            ProfessionalClaimSnapshotCodec.CurrentVersion,
            agencyId,
            personId,
            firstName,
            lastName,
            new DateTime(1990, 1, 1),
            "U",
            memberId,
            subscriberStreet,
            city,
            "ME",
            zip,
            providerName,
            "1999999984",
            taxId,
            providerStreet,
            providerCity,
            "ME",
            providerZip,
            submitterId,
            "Test Billing",
            contactPhone,
            "MEDICAID MAINE",
            "MCDME"));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}

public sealed record AuditEventSnapshot(
    int AgencyId,
    int ActorUserId,
    string Action,
    string ResourceType,
    string? ResourceId,
    string CorrelationId,
    string MetadataJson);

public sealed record TestConsumerSeed(int PersonId, int Revision);

public sealed record TestConsumerGraphSnapshot(
    int People,
    int Forms,
    int FormAttestations,
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
        Forms + FormAttestations + Notes + Contacts + Reviews + Appointments + Assessments +
        PersonProviders + AtRequests + AtRequestItems + PersonVersions;
}
