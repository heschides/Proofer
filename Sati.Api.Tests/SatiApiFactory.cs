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
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;

namespace Sati.Api.Tests;

public sealed class SatiApiFactory : WebApplicationFactory<Program>
{
    private const string TestPassword = "Correct-Horse-42!";
    private const string TestSigningKey = "integration-test-signing-key-that-is-at-least-32-characters";
    private const string TestDatabaseConnection =
        "Data Source=SatiApiTests;Mode=Memory;Cache=Shared;Default Timeout=30";
    private readonly SqliteConnection _connection = new(TestDatabaseConnection);
    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private bool _seeded;

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
                ["SatiApi:ExpectedDatabaseName"] = "SatiApiTests",
                ["SatiApi:ExpectedEnvironment"] = "Testing"
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
                options.UseSqlite(TestDatabaseConnection));
            services.AddScoped(provider =>
                provider.GetRequiredService<IDbContextFactory<ApiDbContext>>().CreateDbContext());
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string username)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        await EnsureSeededAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(username, TestPassword));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("The test login returned no response body.");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
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

    public async Task ChangeUserRoleAsync(int userId, string role)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Id == userId);
        user.Role = role;
        await db.SaveChangesAsync();
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
                CreateUser(verifier, 21, "admin-two", "Admin", 2),
                CreateUser(verifier, 22, "case-manager-two", "CaseManager", 2, 23),
                CreateUser(verifier, 23, "supervisor-two", "Supervisor", 2),
                CreateUser(verifier, 31, "platform-operator", "PlatformOperator", 1));
            db.People.AddRange(
                new ServerPerson
                {
                    Id = 101, UserId = 12, AgencyId = 1, FirstName = "Person", LastName = "One",
                    BirthDate = new DateTime(1990, 1, 1), EffectiveDate = new DateTime(2026, 1, 1),
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
                    EventDate = new DateTime(2026, 8, 3), Minutes = 60, Status = 6
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
        int? supervisorId = null)
    {
        var credential = verifier.Hash(TestPassword);
        return new ServerUser
        {
            Id = id,
            Username = username,
            DisplayName = username,
            Role = role,
            AgencyId = agencyId,
            SupervisorId = supervisorId,
            PasswordHash = credential.Hash,
            Salt = credential.Salt
        };
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
            EffectiveDate = new DateTime(2026, 1, 1),
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

    public async Task<int> CreateNonCompliantReviewNoteAsync()
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var nextId = await db.Notes.MaxAsync(note => note.Id) + 1;
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

    private static ServerForm CompliantForm(int personId, string type) => new()
    {
        PersonId = personId,
        Type = type,
        DueDate = new DateTime(2026, 12, 31),
        IsCompliant = true,
        CompletedDate = new DateTime(2026, 1, 2)
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
