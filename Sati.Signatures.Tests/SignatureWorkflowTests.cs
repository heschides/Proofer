using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Signatures.Tests;

public sealed class SignatureWorkflowTests
{
    [Theory]
    [InlineData("Production", "SatiProduction", true)]
    [InlineData("Demo", "SatiProduction", true)]
    [InlineData("Testing", "Other", true)]
    [InlineData("Demo", "SatiDemo", false)]
    public void EnvironmentGateFailsClosed(string environment, string database, bool enabled) => Assert.False(new SignatureFeature(new() { ExpectedEnvironment = environment, ExpectedDatabaseName = database, Enabled = enabled }).Enabled);

    [Fact]
    public async Task SignRequiresActualDocumentReleaseConsentAccessAndIntent()
    {
        await using var f = await Fixture.Create();
        var issued = await f.Issue();
        var auth = await f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.CompleteAsync(auth.SessionToken, "Synthetic Person", true));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.ConsentAsync(auth.SessionToken, true, true));
        Assert.Equal(f.Pdf, await f.Workflow.PortalDocumentAsync(auth.SessionToken));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.ConsentAsync(auth.SessionToken, false, true));
        await f.Workflow.ConsentAsync(auth.SessionToken, true, true);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.CompleteAsync(auth.SessionToken, "Different Person", true));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.CompleteAsync(auth.SessionToken, "Synthetic Person", false));
        var receipt = await f.Workflow.CompleteAsync(auth.SessionToken, "Synthetic Person", true);
        var request = await f.Db.SignatureRequests.SingleAsync();
        Assert.Equal("Signed", request.State);
        Assert.Single(await f.Db.SignatureCompletions.ToListAsync());
        Assert.Equal(SignatureRules.DisclosureText, (await f.Db.SignatureConsents.SingleAsync()).DisclosureText);
        Assert.Equal("Signed", (await f.Workflow.DetailsAsync(receipt.SessionToken, true)).State);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.DetailsAsync(auth.SessionToken));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.CompleteAsync(receipt.SessionToken, "Synthetic Person", true));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin));
        Assert.DoesNotContain(await f.Db.SignatureEvents.ToListAsync(), e => e.ActorKind == "Staff" && e.Kind == "Signed");
    }

    [Fact]
    public async Task FifthFailurePersistsLockAndInvalidatesAnExistingSession()
    {
        await using var f = await Fixture.Create();
        var issued = await f.Issue();
        var auth = await f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin);
        for (var i = 0; i < 5; i++) await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.AuthenticateAsync(issued.Token, "83572019"));
        f.Db.ChangeTracker.Clear();
        var request = await f.Db.SignatureRequests.SingleAsync();
        Assert.Equal(5, request.FailedPinAttempts);
        Assert.NotNull(request.LockedAtUtc);
        Assert.Equal(5, await f.Db.SignatureEvents.CountAsync(x => x.Kind == "PinRejected" || x.Kind == "PinLocked"));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.DetailsAsync(auth.SessionToken));
    }

    [Fact]
    public async Task ReplacementRevokesOldLinkAndSessionsAndRequiresFreshIdentityChecks()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        var session = await f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin);
        var latest = (await f.Workflow.ListAsync(f.Actor, 2)).Single();
        var next = new ReplaceSignatureRequest(Guid.NewGuid(), latest.Revision, "47295813", "47295813", true, true, "Confirmed correct test address");
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.ReplaceAsync(f.Actor, issued.Dto.Id, next with { Pin = Fixture.Pin, ConfirmPin = Fixture.Pin }, new("Synthetic Person", "synthetic@example.test")));
        var replaced = await f.Workflow.ReplaceAsync(f.Actor, issued.Dto.Id, next, new("Synthetic Person", "synthetic@example.test"));
        Assert.NotEqual(issued.Dto.Id, replaced.Id);
        Assert.Equal(replaced.Id, (await f.Workflow.ReplaceAsync(f.Actor, issued.Dto.Id, next, new("Synthetic Person", "synthetic@example.test"))).Id);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.ReplaceAsync(f.Actor, issued.Dto.Id, next with { Pin = "93618427", ConfirmPin = "93618427" }, new("Synthetic Person", "synthetic@example.test")));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.DetailsAsync(session.SessionToken));
    }

    [Theory]
    [InlineData("decline", "Declined")]
    [InlineData("changes", "ChangesRequested")]
    [InlineData("withdraw", "Revoked")]
    public async Task NonSigningDecisionsAreTerminalWithoutFabricatingSignature(string choice, string outcome)
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue(); var auth = await f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin);
        await f.Workflow.DecideAsync(auth.SessionToken, choice, "Please contact me about paper options.");
        Assert.Equal(outcome, (await f.Db.SignatureRequests.SingleAsync()).State);
        Assert.Empty(await f.Db.SignatureCompletions.ToListAsync());
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.DetailsAsync(auth.SessionToken));
    }

    [Fact]
    public async Task ExpiredLinkAndSessionRefuseEveryProtectedAction()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue(); var auth = await f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin);
        f.Time.Advance(TimeSpan.FromMinutes(31));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.DetailsAsync(auth.SessionToken));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.ExtendSessionAsync(auth.SessionToken));
        f.Time.Advance(TimeSpan.FromDays(4));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin));
    }

    [Fact]
    public async Task UserCanExtendAnActiveSessionButNeverBeyondRequestExpiry()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue(); var auth = await f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin);
        f.Time.Advance(TimeSpan.FromMinutes(29));
        var extended = await f.Workflow.ExtendSessionAsync(auth.SessionToken);
        Assert.True(extended > auth.ExpiresAtUtc);
        f.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(extended, (await f.Workflow.DetailsAsync(auth.SessionToken)).SessionExpiresAtUtc);
        Assert.True(extended <= issued.Dto.ExpiresAtUtc);
    }

    [Fact]
    public async Task ChangedSourceIsRejectedEvenWithPreviouslyAuthenticatedSession()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue(); var auth = await f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin);
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE TestSources SET SupersededByArtifactId = 99 WHERE Id = 3");
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.PortalDocumentAsync(auth.SessionToken));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.ConsentAsync(auth.SessionToken, true, true));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.CompleteAsync(auth.SessionToken, "Synthetic Person", true));
    }

    [Fact]
    public async Task BlobReplacementFailsHashCheckBeforeAnyDocumentReleaseEvidence()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue(); var auth = await f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin);
        var frozen = await f.Db.FrozenSignatureDocuments.SingleAsync(); f.Blobs.Values[frozen.BlobPath] = [1, 2, 3];
        var error = await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.PortalDocumentAsync(auth.SessionToken));
        Assert.Equal("signature_integrity_failed", error.Code);
        Assert.Null((await f.Db.SignatureSessions.SingleAsync()).DocumentReleasedAtUtc);
    }

    [Fact]
    public async Task TokensAndPinsAreNotStoredInPlaintextAndOutboxIsBoundToItsRow()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        var request = await f.Db.SignatureRequests.SingleAsync(); var row = await f.Db.SignatureOutbox.SingleAsync();
        Assert.NotEqual(issued.Token, request.TokenSha256); Assert.NotEqual(Fixture.Pin, request.PinHash);
        Assert.DoesNotContain(issued.Token, System.Text.Encoding.UTF8.GetString(row.PayloadCiphertext!));
        Assert.True(await f.Pins.VerifyAsync(request, Fixture.Pin));
        var originalId = row.Id; row.Id++;
        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() => f.Outbox.UnprotectAsync(row));
        row.Id = originalId;
        Assert.Contains(issued.Token, (await f.Outbox.UnprotectAsync(row)).Link);
        request.ClientRequestId = Guid.NewGuid();
        Assert.False(await f.Pins.VerifyAsync(request, Fixture.Pin));
    }

    [Fact]
    public async Task AgencyAndConcurrencyChecksRefuseForeignOrStaleStaffActions()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        Assert.Empty(await f.Workflow.ListAsync(new(44, 9), 2));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.DownloadAsync(new(44, 9), issued.Dto.Id, false));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.RevokeAsync(f.Actor, issued.Dto.Id, new(issued.Dto.Revision + 1, "Stale")));
        Assert.Equal("Issued", (await f.Db.SignatureRequests.SingleAsync()).State);
    }

    [Fact]
    public async Task RepeatedSubmissionMustMatchTheOriginalCodeIdentityAndExpiry()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        var input = new CreateSignatureRequest(issued.Dto.ClientRequestId, 2, 3, SignerCapacity.Consumer, null, Fixture.Pin, Fixture.Pin, true, true, null);
        var signer = new VerifiedSignatureSigner("Synthetic Person", "synthetic@example.test");
        Assert.Equal(issued.Dto.Id, (await f.Workflow.CreateAsync(f.Actor, input, signer)).Id);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.CreateAsync(f.Actor, input with { Pin = "93618427", ConfirmPin = "93618427" }, signer));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.CreateAsync(f.Actor, input with { ExpiryHours = 24 }, signer));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.CreateAsync(f.Actor, input, signer with { Email = "changed@example.test" }));
        Assert.Single(await f.Db.SignatureRequests.ToListAsync());
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public const string Pin = "58392716";
        private readonly SqliteConnection connection;
        public SignatureDbContext Db { get; }
        public MemoryBlobs Blobs { get; } = new();
        public TestTime Time { get; } = new();
        public SigningPinProtector Pins { get; }
        public SignatureOutboxProtector Outbox { get; }
        public SignatureWorkflow Workflow { get; }
        public SignatureActor Actor { get; } = new(1, 7);
        public byte[] Pdf { get; }
        private Fixture(SqliteConnection connection)
        {
            this.connection = connection;
            Db = new(new DbContextOptionsBuilder<SignatureDbContext>().UseSqlite(connection).Options);
            var key = new TestKey(); Pins = new(key); Outbox = new(key);
            var options = new SignatureOptions { Enabled = true, ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests", PortalBaseUri = "https://sign.example.test/" };
            Workflow = new(Db, new(options), options, Blobs, Pins, Outbox, Time);
            using var document = new PdfSharp.Pdf.PdfDocument(); document.AddPage();
            using var stream = new MemoryStream(); document.Save(stream, false); Pdf = stream.ToArray();
        }
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var result = new Fixture(connection); await result.Db.Database.EnsureCreatedAsync();
            await result.Db.Database.ExecuteSqlRawAsync("CREATE TABLE TestSources(Id INTEGER, AgencyId INTEGER, PersonId INTEGER, Kind TEXT, CycleStart TEXT, Origin TEXT, ContentSha256 TEXT, ByteCount INTEGER, BlankFieldsJson TEXT, SupersededByArtifactId INTEGER NULL)");
            await result.Db.Database.ExecuteSqlRawAsync("CREATE VIEW SignatureSourceDocuments AS SELECT * FROM TestSources");
            await result.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO TestSources VALUES(3,1,2,'PrivacyPractices','2026-01-01','GeneratedInSati',{SignatureSecrets.Hash(result.Pdf)},{result.Pdf.LongLength},'[]',NULL)");
            return result;
        }
        public async Task<(SignatureRequestDto Dto, string Token)> Issue()
        {
            await Workflow.FreezeAsync(Actor, 2, 3, new(Guid.NewGuid(), Pdf, true));
            var dto = await Workflow.CreateAsync(Actor, new(Guid.NewGuid(), 2, 3, SignerCapacity.Consumer, null, Pin, Pin, true, true, null), new("Synthetic Person", "synthetic@example.test"));
            var mail = await Outbox.UnprotectAsync(await Db.SignatureOutbox.SingleAsync());
            return (dto, new Uri(mail.Link).Segments.Last());
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    internal sealed class MemoryBlobs : ISignatureBlobStore
    {
        public Dictionary<string, byte[]> Values { get; } = [];
        public Task WriteOnceAsync(string path, byte[] content, CancellationToken cancellationToken = default) { Values.Add(path, content.ToArray()); return Task.CompletedTask; }
        public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Values[path].ToArray());
    }
    internal sealed class TestKey : ISigningPinKeyWrapper, ISignatureOutboxKeyWrapper
    {
        public Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken = default) => Task.FromResult(new WrappedDataKey(dataKey.ToArray(), "test-only-key"));
        public Task<byte[]> UnwrapAsync(byte[] wrappedKey, string keyId, CancellationToken cancellationToken = default) => Task.FromResult(wrappedKey.ToArray());
    }
    internal sealed class TestTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
