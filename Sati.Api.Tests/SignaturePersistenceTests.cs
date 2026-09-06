using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Migrations;
using Sati.Models;
using Xunit;

namespace Sati.Api.Tests;

public sealed class SignaturePersistenceTests
{
    private static readonly Type[] ImmutableTypes = [typeof(FrozenSignatureDocument), typeof(SignatureConsent),
        typeof(SignatureEvent), typeof(SignatureCompletion), typeof(SignaturePackage)];

    [Fact]
    public void FullContextsShareTheSigningSchemaAndPortalModelOmitsClinicalEntities()
    {
        const string unused = "Server=unused;Database=unused;Integrated Security=True;TrustServerCertificate=True";
        using var local = new SatiContext(new DbContextOptionsBuilder<SatiContext>().UseSqlServer(unused).Options);
        using var api = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer(unused).Options);
        using var portal = new SignatureDbContext(new DbContextOptionsBuilder<SignatureDbContext>().UseSqlServer(unused).Options);
        var a = local.GetService<IDesignTimeModel>().Model;
        var b = api.GetService<IDesignTimeModel>().Model;
        var narrow = portal.GetService<IDesignTimeModel>().Model;
        var types = ImmutableTypes.Concat([typeof(SignatureRequest), typeof(SignatureSession), typeof(SignatureOutbox)]);
        foreach (var type in types)
        {
            var left = a.FindEntityType(type)!;
            var right = b.FindEntityType(type)!;
            Assert.Equal(Describe(left), Describe(right));
            Assert.All(left.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
            Assert.Equal(Properties(left), Properties(narrow.FindEntityType(type)!));
        }
        Assert.DoesNotContain(narrow.GetEntityTypes(), entity => entity.GetTableName() is "People" or "Users" or "Notes" or "AuditEvents" or "DocumentArtifacts");
        Assert.Equal("SignatureSourceDocuments", narrow.FindEntityType(typeof(SignatureSourceDocument))!.GetViewName());
        Assert.Equal("SignatureDatabaseEnvironment", narrow.FindEntityType(typeof(SignatureDatabaseEnvironment))!.GetViewName());
    }

    [Fact]
    public async Task StoredSigningTimestampsRemainUtcAndSerializeWithAnExplicitTimezone()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        var request = await db.SignatureRequests.SingleAsync(x => x.Id == 1);
        var session = await db.SignatureSessions.SingleAsync(x => x.Id == 1);
        var frozen = await db.FrozenSignatureDocuments.SingleAsync();
        Assert.Equal(DateTimeKind.Utc, request.IssuedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, request.ExpiresAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, request.CompletedAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, session.DocumentReleasedAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, frozen.StoredAtUtc.Kind);
        Assert.EndsWith("Z\"", System.Text.Json.JsonSerializer.Serialize(request.ExpiresAtUtc));
    }

    [Fact]
    public void MigrationCreatesEightRetainedTablesAndGuardsRollbackBeforeDroppingAnything()
    {
        var migration = new AddSignatureEvidence();
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(8, tables.Length);
        Assert.All(tables.SelectMany(table => table.ForeignKeys), fk => Assert.Equal(
            Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict, fk.OnDelete));
        Assert.Contains(migration.UpOperations.OfType<SqlOperation>(), operation => operation.Sql.Contains("CREATE VIEW dbo.SignatureSourceDocuments", StringComparison.Ordinal));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM dbo.FrozenSignatureDocuments)", guard.Sql);
        Assert.Contains("THROW 51001", guard.Sql);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    [InlineData(3, false)] [InlineData(3, true)]
    [InlineData(4, false)] [InlineData(4, true)]
    public async Task RetainedSourcesConsentEvidenceAndSigningDecisionsCannotChange(int kind, bool delete)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        object entity = kind switch
        {
            0 => await db.FrozenSignatureDocuments.SingleAsync(),
            1 => await db.SignatureConsents.SingleAsync(x => x.Id == 1),
            2 => await db.SignatureEvents.SingleAsync(x => x.Id == 1),
            3 => await db.SignatureCompletions.SingleAsync(),
            _ => await db.SignaturePackages.SingleAsync()
        };
        if (delete) db.Remove(entity);
        else db.Entry(entity).Property(kind switch
        {
            0 => "BlobPath", 1 => "DisclosureText", 2 => "DetailJson", 3 => "TypedSignerName", _ => "BlobPath"
        }).CurrentValue = "rewritten evidence";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ASignedRequestCannotBeReopened()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        var request = await db.SignatureRequests.SingleAsync(x => x.Id == 1);
        request.State = "Issued"; request.CompletedAtUtc = null; request.Revision++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task PinFailuresCannotBeResetOnTheSameInvitation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        var request = await db.SignatureRequests.SingleAsync(x => x.Id == 2);
        request.FailedPinAttempts = 1; request.Revision++;
        await db.SaveChangesAsync();
        request.FailedPinAttempts = 0; request.Revision++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task StaleParallelRequestChangesCannotOverwriteTheWinner()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var first = fixture.Open();
        await using var second = fixture.Open();
        var a = await first.SignatureRequests.SingleAsync(x => x.Id == 2);
        var b = await second.SignatureRequests.SingleAsync(x => x.Id == 2);
        a.FailedPinAttempts = b.FailedPinAttempts = 1;
        a.Revision++; b.Revision++;
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task TheFrozenArtifactCannotBeReboundToAnotherAgency()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        db.FrozenSignatureDocuments.Add(new FrozenSignatureDocument
        {
            AgencyId = 1, PersonId = 1, DocumentArtifactId = 2, ContentSha256 = new('B', 64),
            ByteCount = 3, BlobPath = "synthetic/foreign.pdf", StoredByUserId = 1, StoredAtUtc = DateTime.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ASessionCannotCrossTheRequestsAgencyBoundary()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        db.SignatureSessions.Add(new SignatureSession
        {
            AgencyId = 2, RequestId = 2, TokenSha256 = new('9', 64), AuthenticationVersion = 1,
            IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CompletionCannotBorrowConsentFromAnotherSessionOfTheSameRequest()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        db.SignatureSessions.Add(new SignatureSession
        {
            Id = 3, AgencyId = 1, RequestId = 2, TokenSha256 = new('9', 64), AuthenticationVersion = 1,
            IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
        });
        await db.SaveChangesAsync();
        db.SignatureCompletions.Add(new SignatureCompletion
        {
            AgencyId = 1, RequestId = 2, FrozenDocumentId = 1, SessionId = 3, ConsentId = 2,
            TypedSignerName = "Synthetic Signer", IntentText = "Synthetic intent", SignedAtUtc = DateTime.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ASigningSessionCannotBecomeAReceiptOrGainUnlimitedTime()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        var session = await db.SignatureSessions.SingleAsync(x => x.Id == 2);
        session.Purpose = "Receipt"; session.Revision++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        session.Purpose = "Signing"; session.ExpiresAtUtc = session.ExpiresAtUtc.AddHours(12);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AnUncertainEmailCannotBeAssignedANewOperationId()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        var work = await db.SignatureOutbox.SingleAsync();
        work.ProviderOperationId = Guid.NewGuid(); work.Revision++;
        await db.SaveChangesAsync();
        work.ProviderOperationId = Guid.NewGuid(); work.Revision++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ExistingArtifactHashesCannotBeRewrittenBehindTheFrozenCopy()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        var artifact = await db.DocumentArtifacts.SingleAsync(x => x.Id == 1);
        artifact.ContentSha256 = new('9', 64);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CentralSupersessionRevokesOnlyOpenRequestsAndRetainsCompletedEvidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Open())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await DocumentArtifactPersistence.StageGeneratedAsync(db, 1, 1, AnnualDocumentKind.ReleaseMedical,
                DateTime.Today, DocumentArtifactOrigin.GeneratedInSati, DateTime.UtcNow, 1, [2, 3, 4], "synthetic.pdf", [], default);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await using var verify = fixture.Open();
        var revoked = await verify.SignatureRequests.SingleAsync(x => x.Id == 2);
        Assert.Equal("Revoked", revoked.State);
        Assert.Equal(2, revoked.AuthenticationVersion);
        Assert.NotNull(revoked.CompletedAtUtc);
        Assert.Contains(await verify.SignatureEvents.ToListAsync(), e => e.RequestId == 2 && e.Kind == "ArtifactSuperseded" && e.Sequence == revoked.Revision);
        Assert.Equal("Signed", (await verify.SignatureRequests.SingleAsync(x => x.Id == 1)).State);
        Assert.Single(await verify.SignatureCompletions.ToListAsync());
        Assert.Single(await verify.FrozenSignatureDocuments.ToListAsync());
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(1, 3)]
    [InlineData(2, 4)]
    public async Task SignerChangesRevokeOnlyTheMatchingOpenIdentityAndKeepSignedEvidence(int? contactId, int expectedId)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Open())
        {
            var template = await db.SignatureRequests.AsNoTracking().SingleAsync(x => x.Id == 2);
            for (var id = 1; id <= 2; id++)
            {
                db.PersonContacts.Add(new ServerPersonContact { Id = id, PersonId = 1, FirstName = "Synthetic", LastName = $"Guardian {id}", Kind = "Guardian" });
                var request = new SignatureRequest();
                db.Entry(request).CurrentValues.SetValues(template);
                request.Id = id + 2; request.ClientRequestId = Guid.NewGuid(); request.TokenSha256 = new((char)('K' + id), 64);
                request.SignerContactId = id; request.SignerCapacity = "Guardian";
                db.SignatureRequests.Add(request);
            }
            await db.SaveChangesAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await SignaturePersistenceMutations.RevokeOpenForSignerAsync(db, 1, contactId, 1, DateTime.UtcNow);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await using var verify = fixture.Open();
        var requests = await verify.SignatureRequests.OrderBy(x => x.Id).ToListAsync();
        var signed = requests.Single(x => x.Id == 1);
        Assert.Equal("Signed", signed.State);
        Assert.Equal(contactId is null, signed.ExternalAccessRevokedAtUtc is not null);
        Assert.Null(signed.AuthorizationRevokedAtUtc);
        Assert.Equal(contactId is null ? 2 : 1, signed.AuthenticationVersion);
        foreach (var row in requests.Where(x => x.Id != 1))
            Assert.Equal(row.Id == expectedId ? "Revoked" : "Issued", row.State);
        Assert.Equal(2, requests.Single(x => x.Id == expectedId).AuthenticationVersion);
        var evidence = Assert.Single(await verify.SignatureEvents.Where(x => x.Kind == "SignerRecordChanged").ToListAsync());
        Assert.Equal(expectedId, evidence.RequestId);
        Assert.Equal("{}", evidence.DetailJson);
        Assert.Single(await verify.SignatureCompletions.ToListAsync());
        Assert.Single(await verify.SignaturePackages.ToListAsync());
    }

    [Fact]
    public async Task ExternalCopyAccessWithdrawalIsOneWayAndRequiresSessionInvalidation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        var signed = await db.SignatureRequests.SingleAsync(x => x.Id == 1);
        signed.ExternalAccessRevokedAtUtc = DateTime.UtcNow;
        signed.ExternalAccessRevocationReason = "Synthetic recipient change.";
        signed.Revision++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        signed.AuthenticationVersion++;
        await db.SaveChangesAsync();
        signed.ExternalAccessRevokedAtUtc = signed.ExternalAccessRevokedAtUtc.Value.AddSeconds(1);
        signed.Revision++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SignerRevocationCannotCommitIndependentlyOfTheProfileChange()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SignaturePersistenceMutations.RevokeOpenForSignerAsync(db, 1, null, 1, DateTime.UtcNow));
        Assert.Equal("Issued", (await db.SignatureRequests.SingleAsync(x => x.Id == 2)).State);
        Assert.False(await db.SignatureEvents.AnyAsync(x => x.Kind == "SignerRecordChanged"));
    }

    private static string Properties(IEntityType type) => string.Join("|", type.GetProperties().OrderBy(p => p.Name)
        .Select(p => $"{p.Name}:{p.GetColumnType()}:{p.IsNullable}:{p.GetMaxLength()}:{p.IsConcurrencyToken}"));
    private static string Describe(IEntityType type) => Properties(type) + string.Join("|", type.GetForeignKeys()
        .Select(fk => $"{string.Join(',', fk.Properties.Select(p => p.Name))}>{fk.PrincipalEntityType.GetTableName()}:{string.Join(',', fk.PrincipalKey.Properties.Select(p => p.Name))}:{fk.DeleteBehavior}").Order());

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public ApiDbContext Open() => new(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options);
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture(); await fixture._connection.OpenAsync();
            await using var db = fixture.Open(); await db.Database.EnsureCreatedAsync();
            db.Agencies.AddRange(new ServerAgency { Id = 1, Name = "Synthetic One" }, new ServerAgency { Id = 2, Name = "Synthetic Two" });
            db.Users.AddRange(new ServerUser { Id = 1, AgencyId = 1, Username = "synthetic-1", DisplayName = "Synthetic Staff", Role = "Admin", Permissions = UserPermissions.AllAgencyPermissions },
                new ServerUser { Id = 2, AgencyId = 2, Username = "synthetic-2", DisplayName = "Synthetic Other", Role = "Admin", Permissions = UserPermissions.AllAgencyPermissions });
            db.People.AddRange(new ServerPerson { Id = 1, AgencyId = 1, UserId = 1, FirstName = "Synthetic", LastName = "One" }, new ServerPerson { Id = 2, AgencyId = 2, UserId = 2, FirstName = "Synthetic", LastName = "Two" });
            for (var id = 1; id <= 2; id++) db.DocumentArtifacts.Add(new ServerDocumentArtifact
            {
                Id = id, AgencyId = id, PersonId = id, GeneratedByUserId = id, GeneratedAtUtc = DateTime.UtcNow,
                Kind = "ReleaseMedical", Origin = "GeneratedInSati", CycleStart = DateTime.Today, ContentSha256 = new('A', 64), ByteCount = 3, BlankFieldsJson = "[]"
            });
            db.FrozenSignatureDocuments.Add(new FrozenSignatureDocument { Id = 1, AgencyId = 1, PersonId = 1, DocumentArtifactId = 1, ContentSha256 = new('A', 64), ByteCount = 3, BlobPath = "synthetic/original.pdf", StoredByUserId = 1, StoredAtUtc = DateTime.UtcNow });
            for (var id = 1; id <= 2; id++)
            {
                db.SignatureRequests.Add(new SignatureRequest
                {
                    Id = id, AgencyId = 1, PersonId = 1, FrozenDocumentId = 1, ClientRequestId = Guid.NewGuid(), SignerCapacity = "Consumer", SignerName = "Synthetic Signer", DeliveryEmail = "synthetic@example.invalid",
                    TokenSha256 = new((char)('B' + id), 64), PinHash = "synthetic-not-a-credential", PinSalt = "synthetic-salt", PinIterations = 600000, PinPepperWrapped = [1], PinKeyId = "synthetic-key",
                    DisclosureVersion = "synthetic-v1", DisclosureText = "Synthetic disclosure", IntentText = "Synthetic intent", IssuedByUserId = 1, IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(3),
                    State = id == 1 ? "Signed" : "Issued", CompletedAtUtc = id == 1 ? DateTime.UtcNow : null
                });
                db.SignatureSessions.Add(new SignatureSession { Id = id, AgencyId = 1, RequestId = id, TokenSha256 = new((char)('E' + id), 64), AuthenticationVersion = 1, IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30), DocumentReleasedAtUtc = DateTime.UtcNow });
                db.SignatureConsents.Add(new SignatureConsent { Id = id, AgencyId = 1, RequestId = id, SessionId = id, DisclosureVersion = "synthetic-v1", DisclosureText = "Synthetic disclosure", AcceptedAtUtc = DateTime.UtcNow });
                db.SignatureEvents.Add(new SignatureEvent { Id = id, AgencyId = 1, RequestId = id, Sequence = 1, Kind = "Issued", ActorKind = "Staff", ActorUserId = 1, OccurredAtUtc = DateTime.UtcNow });
            }
            db.SignatureCompletions.Add(new SignatureCompletion { Id = 1, AgencyId = 1, RequestId = 1, FrozenDocumentId = 1, SessionId = 1, ConsentId = 1, TypedSignerName = "Synthetic Signer", IntentText = "Synthetic intent", SignedAtUtc = DateTime.UtcNow });
            db.SignaturePackages.Add(new SignaturePackage { Id = 1, AgencyId = 1, RequestId = 1, CompletionId = 1, ContentSha256 = new('8', 64), ByteCount = 3, BlobPath = "synthetic/package.pdf", CreatedAtUtc = DateTime.UtcNow });
            db.SignatureOutbox.Add(new SignatureOutbox { Id = 1, AgencyId = 1, RequestId = 2, Purpose = "Invitation", NextAttemptAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(); return fixture;
        }
        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
