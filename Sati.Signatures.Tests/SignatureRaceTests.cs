using Microsoft.EntityFrameworkCore;
using Sati.Models;
using Xunit;
using Fixture = Sati.Signatures.Tests.SignatureWorkflowTests.Fixture;

namespace Sati.Signatures.Tests;

public sealed class SignatureRaceTests
{
    [Fact]
    public async Task Session_expiry_during_completion_cannot_create_an_unusable_signed_record()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        var session = await f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin);
        await f.Workflow.PortalDocumentAsync(session.SessionToken);
        await f.Workflow.ConsentAsync(session.SessionToken, true, true);
        var expiry = new DateTimeOffset(DateTime.SpecifyKind(session.ExpiresAtUtc, DateTimeKind.Utc));
        var clock = new CrossDeadlineClock(expiry, 3);
        var options = Options();
        var workflow = new SignatureWorkflow(f.Db, new(options), options, f.Blobs, f.Pins, f.Outbox, clock);
        var error = await Assert.ThrowsAsync<SignatureWorkflowException>(() => workflow.CompleteAsync(session.SessionToken, "Synthetic Person", true));
        Assert.Equal(404, error.StatusCode);
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.SignatureCompletions.ToListAsync());
        Assert.Empty(await f.Db.SignatureEvents.Where(x => x.Kind == "Signed").ToListAsync());
        Assert.Equal("Viewed", (await f.Db.SignatureRequests.SingleAsync()).State);
        Assert.Empty(await f.Db.SignatureSessions.Where(x => x.Purpose == "Receipt").ToListAsync());
    }

    [Fact]
    public async Task Link_expiry_during_pin_verification_is_a_neutral_refusal_without_a_session()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        var deadline = new DateTimeOffset(DateTime.SpecifyKind(issued.Dto.ExpiresAtUtc, DateTimeKind.Utc));
        var options = Options();
        var workflow = new SignatureWorkflow(f.Db, new(options), options, f.Blobs, f.Pins, f.Outbox, new CrossDeadlineClock(deadline, 1));
        var error = await Assert.ThrowsAsync<SignatureWorkflowException>(() => workflow.AuthenticateAsync(issued.Token, Fixture.Pin));
        Assert.Equal(404, error.StatusCode);
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.SignatureSessions.ToListAsync());
        Assert.Equal("Issued", (await f.Db.SignatureRequests.SingleAsync()).State);
    }

    [Theory]
    [InlineData("state", 2)]
    [InlineData("document", 3)]
    [InlineData("consent", 3)]
    [InlineData("extend", 3)]
    public async Task A_deadline_crossed_during_a_read_or_choice_does_not_release_or_extend_access(string action, int validReads)
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        var auth = await f.Workflow.AuthenticateAsync(issued.Token, Fixture.Pin);
        if (action == "consent") await f.Workflow.PortalDocumentAsync(auth.SessionToken);
        var deadline = new DateTimeOffset(DateTime.SpecifyKind(auth.ExpiresAtUtc, DateTimeKind.Utc));
        var options = Options();
        var workflow = new SignatureWorkflow(f.Db, new(options), options, f.Blobs, f.Pins, f.Outbox, new CrossDeadlineClock(deadline, validReads));
        await Assert.ThrowsAsync<SignatureWorkflowException>(async () =>
        {
            if (action == "state") await workflow.DetailsAsync(auth.SessionToken);
            else if (action == "document") await workflow.PortalDocumentAsync(auth.SessionToken);
            else if (action == "consent") await workflow.ConsentAsync(auth.SessionToken, true, true);
            else await workflow.ExtendSessionAsync(auth.SessionToken);
        });
        f.Db.ChangeTracker.Clear();
        var retained = await f.Db.SignatureSessions.SingleAsync();
        Assert.Equal(auth.ExpiresAtUtc, retained.ExpiresAtUtc);
        if (action != "consent") Assert.Null(retained.DocumentReleasedAtUtc);
        Assert.Null(retained.AccessAcknowledgedAtUtc);
        Assert.Empty(await f.Db.SignatureConsents.ToListAsync());
        Assert.Empty(await f.Db.SignatureEvents.Where(x => x.Kind == "SessionExtended").ToListAsync());
    }

    [Fact]
    public async Task Stopped_external_access_refuses_old_receipt_links_and_sessions_but_keeps_staff_copies()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        var receipt = await SignAndPrepare(f, issued.Token);
        var authenticated = await f.Workflow.AuthenticateReceiptAsync(issued.Token, Fixture.Pin);
        await StopExternalAccess(f);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.AuthenticateReceiptAsync(issued.Token, Fixture.Pin));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.DetailsAsync(receipt.SessionToken, true));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.DetailsAsync(authenticated.SessionToken, true));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.PortalDocumentAsync(authenticated.SessionToken, true));
        Assert.Equal(f.Pdf, await f.Workflow.DownloadAsync(f.Actor, issued.Dto.Id, false));
        Assert.Equal(f.Pdf, await f.Workflow.DownloadAsync(f.Actor, issued.Dto.Id, true));
        var retained = await f.Db.SignatureRequests.AsNoTracking().SingleAsync();
        Assert.Equal("Signed", retained.State);
        Assert.Null(retained.AuthorizationRevokedAtUtc);
        Assert.Null(retained.LockedAtUtc);
        Assert.Equal(0, retained.FailedPinAttempts);
        Assert.Single(await f.Db.SignatureCompletions.ToListAsync());
        Assert.Single(await f.Db.SignatureEvents.Where(x => x.Kind == "Signed").ToListAsync());
    }

    [Fact]
    public async Task External_access_refusal_applies_even_to_an_otherwise_matching_receipt_session()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        await SignAndPrepare(f, issued.Token);
        await StopExternalAccess(f);
        var request = await f.Db.SignatureRequests.AsNoTracking().SingleAsync();
        // Represent a restored matching-version session. External access policy must be checked
        // independently rather than assuming version mismatch is its only enforcement.
        var token = SignatureSecrets.NewToken();
        f.Db.Add(new SignatureSession { AgencyId = request.AgencyId, RequestId = request.Id, Purpose = "Receipt",
            TokenSha256 = SignatureSecrets.Hash(token), AuthenticationVersion = request.AuthenticationVersion,
            IssuedAtUtc = f.Time.GetUtcNow().UtcDateTime, ExpiresAtUtc = f.Time.GetUtcNow().AddMinutes(10).UtcDateTime });
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.DetailsAsync(token, true));
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => f.Workflow.PortalDocumentAsync(token, true));
    }

    [Fact]
    public async Task Stopped_external_access_prevents_a_new_receipt_email()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        await SignAndPrepare(f, issued.Token);
        await QueueReceipt(f, issued.Token);
        await StopExternalAccess(f);
        var options = Options(); var sender = new RecordingSender();
        var worker = new SignatureMailWorker(new(options), options, f.Outbox, sender, f.Time);
        await worker.ProcessNextAsync(f.Db); // Retire the already-ended signing invitation.
        await worker.ProcessNextAsync(f.Db);
        Assert.Equal(0, sender.Sends);
        Assert.Equal("Stale", (await f.Db.SignatureOutbox.AsNoTracking().SingleAsync(x => x.Purpose == "Receipt")).State);
        Assert.Equal("Signed", (await f.Db.SignatureRequests.AsNoTracking().SingleAsync()).State);
        var displayed = Assert.Single(await f.Workflow.ListAsync(f.Actor, issued.Dto.PersonId));
        Assert.Equal("Stale", displayed.ReceiptDeliveryState);
        Assert.NotNull(displayed.ExternalAccessRevokedAtUtc);
    }

    [Fact]
    public async Task Stopped_external_access_still_allows_recording_an_existing_email_operation_outcome()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        await SignAndPrepare(f, issued.Token);
        await QueueReceipt(f, issued.Token);
        var receipt = await f.Db.SignatureOutbox.SingleAsync(x => x.Purpose == "Receipt");
        receipt.ProviderOperationId = Guid.NewGuid(); receipt.SubmittedAtUtc = f.Time.GetUtcNow().UtcDateTime;
        receipt.ProviderStatus = "Sending"; receipt.State = "Polling"; receipt.Revision++;
        await f.Db.SaveChangesAsync();
        await StopExternalAccess(f);
        var options = Options(); var sender = new RecordingSender();
        var worker = new SignatureMailWorker(new(options), options, f.Outbox, sender, f.Time);
        await worker.ProcessNextAsync(f.Db);
        await worker.ProcessNextAsync(f.Db);
        Assert.Equal(0, sender.Sends);
        Assert.Equal(1, sender.Polls);
        Assert.Equal("Sent", (await f.Db.SignatureOutbox.AsNoTracking().SingleAsync(x => x.Purpose == "Receipt")).State);
    }

    private static SignatureOptions Options() => new() { Enabled = true, ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests",
        PortalBaseUri = "https://sign.example.test/", EmailEnabled = true, AllowedTestRecipients = ["synthetic@example.test"] };
    private static async Task<SignatureAuthentication> SignAndPrepare(Fixture f, string token)
    {
        var auth = await f.Workflow.AuthenticateAsync(token, Fixture.Pin);
        await f.Workflow.PortalDocumentAsync(auth.SessionToken);
        await f.Workflow.ConsentAsync(auth.SessionToken, true, true);
        var receipt = await f.Workflow.CompleteAsync(auth.SessionToken, "Synthetic Person", true);
        var completion = await f.Db.SignatureCompletions.SingleAsync();
        const string path = "synthetic-copy.pdf";
        f.Blobs.Values[path] = f.Pdf;
        f.Db.Add(new SignaturePackage { AgencyId = completion.AgencyId, RequestId = completion.RequestId, CompletionId = completion.Id,
            BlobPath = path, ByteCount = f.Pdf.LongLength, ContentSha256 = SignatureSecrets.Hash(f.Pdf), CreatedAtUtc = f.Time.GetUtcNow().UtcDateTime });
        await f.Db.SaveChangesAsync();
        return receipt;
    }
    private static async Task StopExternalAccess(Fixture f)
    {
        var request = await f.Db.SignatureRequests.SingleAsync();
        request.ExternalAccessRevokedAtUtc = f.Time.GetUtcNow().UtcDateTime;
        request.ExternalAccessRevocationReason = "The recorded recipient changed.";
        request.AuthenticationVersion++; request.Revision++;
        await f.Db.SaveChangesAsync();
    }
    private static async Task QueueReceipt(Fixture f, string token)
    {
        var request = await f.Db.SignatureRequests.SingleAsync();
        var row = new SignatureOutbox { AgencyId = request.AgencyId, RequestId = request.Id, Purpose = "Receipt", NextAttemptAtUtc = f.Time.GetUtcNow().UtcDateTime };
        f.Db.Add(row); await f.Db.SaveChangesAsync();
        await f.Outbox.ProtectAsync(row, new(request.DeliveryEmail, "https://sign.example.test/r/" + token, "Receipt"));
        row.Revision++; await f.Db.SaveChangesAsync();
    }
    private sealed class CrossDeadlineClock(DateTimeOffset deadline, int validReads = 2) : TimeProvider
    {
        private int reads;
        public override DateTimeOffset GetUtcNow() => ++reads <= validReads ? deadline.AddMilliseconds(-1) : deadline.AddMilliseconds(1);
    }
    private sealed class RecordingSender : ISignatureEmailSender
    {
        public int Sends { get; private set; }
        public int Polls { get; private set; }
        public Task<SignatureEmailResult> SendAsync(Guid operationId, SignatureEmail email, CancellationToken cancellationToken = default)
        { Sends++; return Task.FromResult(new SignatureEmailResult("Sent", operationId.ToString("D"))); }
        public Task<SignatureEmailResult> GetStatusAsync(string operationId, CancellationToken cancellationToken = default)
        { Polls++; return Task.FromResult(new SignatureEmailResult("Sent", operationId)); }
    }
}
