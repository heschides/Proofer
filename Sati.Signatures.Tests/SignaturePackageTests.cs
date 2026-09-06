using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;
using Sati.Contracts.V1;
using Sati.Models;
using Xunit;

namespace Sati.Signatures.Tests;

internal static class SignatureTestFonts
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (OperatingSystem.IsWindows()) GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }
}

public sealed class SignaturePackageTests
{
    [Fact]
    public async Task CompletedPackageIsIdempotentKeepsOriginalAndQueuesProtectedReceipt()
    {
        await using var f = await Signed();
        var worker = Worker(f);
        var completion = await f.Db.SignatureCompletions.SingleAsync();
        var original = await f.Db.FrozenSignatureDocuments.SingleAsync();
        var package = await worker.BuildAsync(f.Db, completion.Id);
        var revision = (await f.Db.SignatureRequests.SingleAsync()).Revision;
        var blobCount = f.Blobs.Values.Count;
        var repeated = await worker.BuildAsync(f.Db, completion.Id);
        Assert.Equal(package.Id, repeated.Id);
        Assert.Equal(revision, (await f.Db.SignatureRequests.SingleAsync()).Revision);
        Assert.Equal(blobCount, f.Blobs.Values.Count);
        Assert.Single(await f.Db.SignaturePackages.ToListAsync());
        Assert.Single(await f.Db.SignatureEvents.Where(x => x.Kind == "PackagePrepared").ToListAsync());
        Assert.Equal(f.Pdf, f.Blobs.Values[original.BlobPath]);
        Assert.Equal(original.ContentSha256, SignatureSecrets.Hash(f.Pdf));
        Assert.NotEqual(original.ContentSha256, package.ContentSha256);
        var bytes = f.Blobs.Values[package.BlobPath];
        Assert.Equal(package.ContentSha256, SignatureSecrets.Hash(bytes));
        Assert.Equal(package.ByteCount, bytes.LongLength);
        using var pdf = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount >= 3);
        var receipt = await f.Db.SignatureOutbox.SingleAsync(x => x.Purpose == "Receipt");
        var invitation = await f.Db.SignatureOutbox.SingleAsync(x => x.Purpose == "Invitation");
        var email = await f.Outbox.UnprotectAsync(receipt);
        var originalEmail = await f.Outbox.UnprotectAsync(invitation);
        Assert.Equal(originalEmail.Link.Replace("/s/", "/r/", StringComparison.Ordinal), email.Link);
        Assert.Equal("Receipt", email.Purpose);
        Assert.DoesNotContain(SignatureWorkflowTests.Fixture.Pin, email.Link);
        Assert.DoesNotContain(new Uri(email.Link).Segments.Last(), System.Text.Encoding.UTF8.GetString(receipt.PayloadCiphertext!));
        Assert.False(await worker.ProcessNextAsync(f.Db));
    }

    [Fact]
    public async Task CorruptOriginalNeverProducesPackageOrDelivery()
    {
        await using var f = await Signed();
        var original = await f.Db.FrozenSignatureDocuments.SingleAsync();
        f.Blobs.Values[original.BlobPath][^1] ^= 1;
        var error = await Assert.ThrowsAsync<SignatureWorkflowException>(() => Worker(f).ProcessNextAsync(f.Db));
        Assert.Equal("signature_integrity_failed", error.Code);
        Assert.Empty(await f.Db.SignaturePackages.ToListAsync());
        Assert.False(await f.Db.SignatureOutbox.AnyAsync(x => x.Purpose == "Receipt"));
        Assert.Single(f.Blobs.Values);
        Assert.Equal("Signed", (await f.Db.SignatureRequests.SingleAsync()).State);
    }

    [Fact]
    public async Task ReceiptProtectionFailureRollsBackPackageAndEvidenceAndCanRetry()
    {
        await using var f = await Signed();
        var before = (await f.Db.SignatureRequests.SingleAsync()).Revision;
        var failedWorker = Worker(f, new SignatureOutboxProtector(new FailWrapKey()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failedWorker.ProcessNextAsync(f.Db));
        Assert.Empty(await f.Db.SignaturePackages.ToListAsync());
        Assert.False(await f.Db.SignatureOutbox.AnyAsync(x => x.Purpose == "Receipt"));
        Assert.False(await f.Db.SignatureEvents.AnyAsync(x => x.Kind == "PackagePrepared"));
        Assert.Equal(before, (await f.Db.SignatureRequests.SingleAsync()).Revision);
        Assert.Equal("Signed", (await f.Db.SignatureRequests.SingleAsync()).State);
        Assert.Equal(2, f.Blobs.Values.Count); // Original plus quarantined, unreferenced generated copy.
        Assert.True(await Worker(f).ProcessNextAsync(f.Db));
        Assert.Single(await f.Db.SignaturePackages.ToListAsync());
        Assert.Single(await f.Db.SignatureOutbox.Where(x => x.Purpose == "Receipt").ToListAsync());
        Assert.Equal(3, f.Blobs.Values.Count); // Retry never overwrites or silently removes a retained blob.
    }

    [Fact]
    public async Task ChangedRecipientStillGetsRetainedStaffCopyWithoutExternalReceipt()
    {
        await using var f = await Signed();
        var request = await f.Db.SignatureRequests.SingleAsync();
        request.ExternalAccessRevokedAtUtc = f.Time.GetUtcNow().UtcDateTime;
        request.ExternalAccessRevocationReason = "Synthetic signer record change.";
        request.AuthenticationVersion++; request.Revision++;
        await f.Db.SaveChangesAsync();
        Assert.True(await Worker(f).ProcessNextAsync(f.Db));
        Assert.Single(await f.Db.SignaturePackages.ToListAsync());
        Assert.False(await f.Db.SignatureOutbox.AnyAsync(x => x.Purpose == "Receipt"));
        Assert.Equal("Signed", request.State);
    }

    [Fact]
    public async Task ExpiredRequestStillGetsRetainedCopyWithoutSendingDeadReceiptLink()
    {
        await using var f = await Signed();
        f.Time.Advance(TimeSpan.FromDays(4));
        Assert.True(await Worker(f).ProcessNextAsync(f.Db));
        Assert.Single(await f.Db.SignaturePackages.ToListAsync());
        Assert.False(await f.Db.SignatureOutbox.AnyAsync(x => x.Purpose == "Receipt"));
    }

    [Fact]
    public async Task CertificatePaginatesFullFrozenWordingWithoutTruncation()
    {
        await using var f = await Signed();
        var request = await f.Db.SignatureRequests.AsNoTracking().SingleAsync();
        var completion = await f.Db.SignatureCompletions.AsNoTracking().SingleAsync();
        var session = await f.Db.SignatureSessions.AsNoTracking().SingleAsync(x => x.Id == completion.SessionId);
        var consent = await f.Db.SignatureConsents.AsNoTracking().SingleAsync();
        var frozen = await f.Db.FrozenSignatureDocuments.AsNoTracking().SingleAsync();
        var events = await f.Db.SignatureEvents.AsNoTracking().ToListAsync();
        request.DisclosureText = consent.DisclosureText = string.Join("\n", Enumerable.Range(1, 90).Select(i => $"Disclosure paragraph {i}: Synthetic text confirms paper options, access, records and consent. END-DISCLOSURE-{i}."));
        request.IntentText = completion.IntentText = string.Join("\n", Enumerable.Range(1, 35).Select(i => $"Signing statement paragraph {i}: This is a synthetic statement of intent. END-INTENT-{i}."));
        var bytes = new SignaturePackageBuilder().Build(f.Pdf, frozen, request, completion, session, consent, events);
        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount >= 7);
        var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sati", "SignatureValidation", "pdf");
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, "synthetic-signed-evidence.pdf"), bytes);
    }

    [Fact]
    public async Task OtherReceiptSessionsDoNotInflateTheSigningCertificate()
    {
        await using var f = await Signed();
        var request = await f.Db.SignatureRequests.SingleAsync();
        var receiptSession = await f.Db.SignatureSessions.SingleAsync(x => x.Purpose == "Receipt");
        request.Revision++; request.FailedPinAttempts++;
        f.Db.SignatureEvents.Add(new SignatureEvent { AgencyId = request.AgencyId, RequestId = request.Id,
            Kind = "PinRejected", ActorKind = "Signer", Sequence = request.Revision, OccurredAtUtc = f.Time.GetUtcNow().UtcDateTime });
        await f.Db.SaveChangesAsync(); // Same wall-clock instant, later authoritative sequence.
        for (var i = 0; i < 20; i++)
        {
            request.Revision++;
            f.Db.SignatureEvents.Add(new SignatureEvent { AgencyId = request.AgencyId, RequestId = request.Id,
                SessionId = receiptSession.Id, Kind = "ReceiptAuthenticated", ActorKind = "Signer", Sequence = request.Revision,
                OccurredAtUtc = f.Time.GetUtcNow().UtcDateTime });
            await f.Db.SaveChangesAsync();
        }
        Assert.True(await Worker(f).ProcessNextAsync(f.Db));
        Assert.Single(await f.Db.SignaturePackages.ToListAsync());
        Assert.Equal(20, await f.Db.SignatureEvents.CountAsync(x => x.Kind == "ReceiptAuthenticated"));
    }

    [Fact]
    public async Task CertificateRefusesDuplicateAuthenticationEvidence()
    {
        await using var f = await Signed();
        var request = await f.Db.SignatureRequests.AsNoTracking().SingleAsync();
        var completion = await f.Db.SignatureCompletions.AsNoTracking().SingleAsync();
        var session = await f.Db.SignatureSessions.AsNoTracking().SingleAsync(x => x.Id == completion.SessionId);
        var consent = await f.Db.SignatureConsents.AsNoTracking().SingleAsync();
        var frozen = await f.Db.FrozenSignatureDocuments.AsNoTracking().SingleAsync();
        var events = await f.Db.SignatureEvents.AsNoTracking().ToListAsync();
        events.Add(new SignatureEvent { AgencyId = request.AgencyId, RequestId = request.Id, SessionId = session.Id,
            Sequence = 2, Kind = "Authenticated", ActorKind = "Signer", OccurredAtUtc = session.IssuedAtUtc });
        Assert.Throws<SignatureWorkflowException>(() => new SignaturePackageBuilder().Build(f.Pdf, frozen, request, completion, session, consent, events));
    }

    [Fact]
    public async Task CertificateRefusesConsentOrSessionSubstitution()
    {
        await using var f = await Signed();
        var request = await f.Db.SignatureRequests.AsNoTracking().SingleAsync();
        var completion = await f.Db.SignatureCompletions.AsNoTracking().SingleAsync();
        var session = await f.Db.SignatureSessions.AsNoTracking().SingleAsync(x => x.Id == completion.SessionId);
        var consent = await f.Db.SignatureConsents.AsNoTracking().SingleAsync();
        var frozen = await f.Db.FrozenSignatureDocuments.AsNoTracking().SingleAsync();
        var events = await f.Db.SignatureEvents.AsNoTracking().ToListAsync();
        consent.SessionId++;
        Assert.Throws<SignatureWorkflowException>(() => new SignaturePackageBuilder().Build(f.Pdf, frozen, request, completion, session, consent, events));
    }

    private static SignatureCompletionWorker Worker(SignatureWorkflowTests.Fixture f, SignatureOutboxProtector? protector = null) =>
        new(new(new SignatureOptions { Enabled = true, ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests" }), f.Blobs, new(), protector ?? f.Outbox, f.Time);
    private static async Task<SignatureWorkflowTests.Fixture> Signed()
    {
        var f = await SignatureWorkflowTests.Fixture.Create();
        var issued = await f.Issue();
        var auth = await f.Workflow.AuthenticateAsync(issued.Token, SignatureWorkflowTests.Fixture.Pin);
        await f.Workflow.PortalDocumentAsync(auth.SessionToken);
        await f.Workflow.ConsentAsync(auth.SessionToken, true, true);
        await f.Workflow.CompleteAsync(auth.SessionToken, "Synthetic Person", true);
        f.Db.ChangeTracker.Clear();
        return f;
    }
    private sealed class FailWrapKey : ISignatureOutboxKeyWrapper
    {
        public Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken ct = default) => throw new InvalidOperationException("Synthetic key outage.");
        public Task<byte[]> UnwrapAsync(byte[] wrappedKey, string keyId, CancellationToken ct = default) => Task.FromResult(wrappedKey.ToArray());
    }
}
