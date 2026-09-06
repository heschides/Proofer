using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;
using Fixture = Sati.Signatures.Tests.SignatureWorkflowTests.Fixture;

namespace Sati.Signatures.Tests;

public sealed class SignatureMailWorkerTests
{
    private static SignatureOptions Options() => new()
    {
        Enabled = true, ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests",
        EmailEnabled = true, PortalBaseUri = "https://sign.example.test/", AllowedTestRecipients = ["synthetic@example.test"]
    };
    private static SignatureMailWorker Worker(Fixture f, ISignatureEmailSender sender, SignatureOptions? options = null, SignatureOutboxProtector? protector = null)
    {
        options ??= Options();
        return new(new(options), options, protector ?? f.Outbox, sender, f.Time);
    }
    private static SignatureDbContext OtherContext(Fixture f) => new(new DbContextOptionsBuilder<SignatureDbContext>().UseSqlite(f.Db.Database.GetDbConnection()).Options);

    [Fact]
    public async Task Operation_and_lease_are_durable_before_submission_and_only_status_is_polled_afterward()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        var sender = new FakeSender();
        sender.Send = async (operation, _) =>
        {
            var saved = await f.Db.SignatureOutbox.AsNoTracking().SingleAsync();
            Assert.Equal(operation, saved.ProviderOperationId);
            Assert.NotNull(saved.SubmittedAtUtc);
            Assert.NotNull(saved.LeaseId);
            Assert.Equal(1, saved.Attempts);
            return new("Sending", operation.ToString("D"));
        };
        var worker = Worker(f, sender);
        Assert.True(await worker.ProcessNextAsync(f.Db));
        var first = await f.Db.SignatureOutbox.AsNoTracking().SingleAsync();
        Assert.Equal("Polling", first.State);
        Assert.Equal("Sending", first.ProviderStatus);
        Assert.Null(first.CompletedAtUtc);
        Assert.False(await worker.ProcessNextAsync(f.Db));
        f.Time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(await worker.ProcessNextAsync(f.Db));
        var completed = await f.Db.SignatureOutbox.AsNoTracking().SingleAsync();
        Assert.Equal(first.ProviderOperationId, completed.ProviderOperationId);
        Assert.Equal("Sent", completed.State);
        Assert.NotNull(completed.CompletedAtUtc);
        Assert.NotNull(completed.LastPolledAtUtc);
        Assert.Single(sender.Submissions);
        Assert.Equal(first.ProviderOperationId!.Value.ToString("D"), Assert.Single(sender.Polls));
        Assert.Equal(2, await f.Db.SignatureEvents.CountAsync(x => x.Kind == "EmailStatusRecorded"));
        Assert.DoesNotContain(await f.Db.SignatureEvents.Select(x => x.DetailJson).ToListAsync(), x => x.Contains("Delivered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Uncertain_submission_recovers_with_the_same_operation_and_never_sends_again()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        var sender = new FakeSender { Send = (_, _) => throw new HttpRequestException("synthetic private response and token") };
        var worker = Worker(f, sender);
        await worker.ProcessNextAsync(f.Db);
        var uncertain = await f.Db.SignatureOutbox.AsNoTracking().SingleAsync();
        Assert.Equal("Polling", uncertain.State);
        Assert.Equal("Unknown", uncertain.ProviderStatus);
        Assert.Null(uncertain.LastPolledAtUtc);
        Assert.Equal("signature_email_provider_unavailable", uncertain.LastErrorCode);
        f.Time.Advance(TimeSpan.FromMinutes(2));
        await worker.ProcessNextAsync(f.Db);
        Assert.Equal("Sent", (await f.Db.SignatureOutbox.AsNoTracking().SingleAsync()).State);
        Assert.Single(sender.Submissions);
        Assert.Equal(uncertain.ProviderOperationId!.Value.ToString("D"), Assert.Single(sender.Polls));
        Assert.DoesNotContain(await f.Db.SignatureEvents.Select(x => x.DetailJson).ToListAsync(), x => x.Contains("synthetic private", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_result_stops_after_five_attempts_and_requires_review_instead_of_resending()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        var sender = new FakeSender { Send = (_, _) => throw new HttpRequestException(), Poll = id => Task.FromResult(new SignatureEmailResult("Unknown", id)) };
        var worker = Worker(f, sender);
        for (var i = 0; i < 5; i++) { Assert.True(await worker.ProcessNextAsync(f.Db)); f.Time.Advance(TimeSpan.FromMinutes(15)); }
        Assert.False(await worker.ProcessNextAsync(f.Db));
        var row = await f.Db.SignatureOutbox.AsNoTracking().SingleAsync();
        Assert.Equal(5, row.Attempts);
        Assert.Equal("NeedsReview", row.State);
        Assert.Equal("Unknown", row.ProviderStatus);
        Assert.NotNull(row.CompletedAtUtc);
        Assert.Single(sender.Submissions);
        Assert.Equal(4, sender.Polls.Count);
        Assert.Single(sender.Polls.Distinct());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_or_unlisted_email_is_suppressed_without_decrypting(bool unlisted)
    {
        await using var f = await Fixture.Create(); await f.Issue();
        var options = Options();
        if (unlisted) options.AllowedTestRecipients = ["different@example.test"]; else options.EmailEnabled = false;
        var key = new BlockingKey { Refuse = true };
        var sender = new FakeSender();
        await Worker(f, sender, options, new(key)).ProcessNextAsync(f.Db);
        var row = await f.Db.SignatureOutbox.AsNoTracking().SingleAsync();
        Assert.Equal("Suppressed", row.State);
        Assert.Null(row.ProviderOperationId);
        Assert.Equal(0, key.Calls);
        Assert.Empty(sender.Submissions);
        Assert.Empty(sender.Polls);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("locked")]
    [InlineData("replaced_document")]
    public async Task Stale_invitation_never_reaches_the_sender(string reason)
    {
        await using var f = await Fixture.Create(); await f.Issue();
        if (reason == "expired") f.Time.Advance(TimeSpan.FromDays(4));
        else if (reason == "replaced_document") await f.Db.Database.ExecuteSqlRawAsync("UPDATE TestSources SET SupersededByArtifactId=4");
        else
        {
            var request = await f.Db.SignatureRequests.SingleAsync();
            if (reason == "locked")
            {
                for (var i = 1; i <= 5; i++)
                {
                    request.FailedPinAttempts = i;
                    if (i == 5) request.LockedAtUtc = f.Time.GetUtcNow().UtcDateTime;
                    request.Revision++; await f.Db.SaveChangesAsync();
                }
            }
            else { request.State = "Revoked"; request.CompletedAtUtc = f.Time.GetUtcNow().UtcDateTime; request.Revision++; await f.Db.SaveChangesAsync(); }
        }
        var sender = new FakeSender();
        await Worker(f, sender).ProcessNextAsync(f.Db);
        Assert.Equal("Stale", (await f.Db.SignatureOutbox.AsNoTracking().SingleAsync()).State);
        Assert.Empty(sender.Submissions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receipt_requires_a_signed_decision_and_prepared_copy(bool signed)
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        await SuppressInvitation(f);
        if (signed) await Sign(f, issued.Token);
        await AddMail(f, issued.Token, "Receipt");
        var sender = new FakeSender();
        await Worker(f, sender).ProcessNextAsync(f.Db);
        Assert.Equal("Stale", (await f.Db.SignatureOutbox.AsNoTracking().SingleAsync(x => x.Purpose == "Receipt")).State);
        Assert.Empty(sender.Submissions);
    }

    [Fact]
    public async Task Prepared_receipt_sends_a_private_copy_link_without_reopening_signing()
    {
        await using var f = await Fixture.Create(); var issued = await f.Issue();
        await SuppressInvitation(f);
        await Sign(f, issued.Token);
        var completion = await f.Db.SignatureCompletions.SingleAsync();
        f.Db.Add(new SignaturePackage { AgencyId = 1, RequestId = issued.Dto.Id, CompletionId = completion.Id,
            ContentSha256 = SignatureSecrets.Hash([1]), ByteCount = 1, BlobPath = "synthetic-package.pdf", CreatedAtUtc = f.Time.GetUtcNow().UtcDateTime });
        await f.Db.SaveChangesAsync();
        await AddMail(f, issued.Token, "Receipt");
        var sender = new FakeSender();
        await Worker(f, sender).ProcessNextAsync(f.Db);
        Assert.Equal("https://sign.example.test/r/" + issued.Token, Assert.Single(sender.Submissions).Email.Link);
        Assert.Equal("Signed", (await f.Db.SignatureRequests.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task Revocation_committed_while_key_access_waits_is_rechecked_before_submission()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        var key = new BlockingKey();
        var sender = new FakeSender();
        var pending = Worker(f, sender, protector: new(key)).ProcessNextAsync(f.Db);
        await key.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using (var other = OtherContext(f))
        {
            var request = await other.SignatureRequests.SingleAsync();
            request.State = "Revoked"; request.CompletedAtUtc = f.Time.GetUtcNow().UtcDateTime; request.Revision++;
            await other.SaveChangesAsync();
        }
        key.Release.TrySetResult();
        Assert.True(await pending);
        Assert.Empty(sender.Submissions);
        var row = await f.Db.SignatureOutbox.AsNoTracking().SingleAsync();
        Assert.Equal("Stale", row.State);
        Assert.Null(row.ProviderOperationId);
    }

    [Fact]
    public async Task Active_lease_excludes_a_second_worker_and_expired_owner_cannot_send_or_record_results()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        var key = new BlockingKey();
        var firstSender = new FakeSender();
        var secondSender = new FakeSender();
        var first = Worker(f, firstSender, protector: new(key)).ProcessNextAsync(f.Db);
        await key.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var other = OtherContext(f);
        Assert.False(await Worker(f, secondSender).ProcessNextAsync(other));
        f.Time.Advance(TimeSpan.FromMinutes(6));
        Assert.True(await Worker(f, secondSender).ProcessNextAsync(other));
        key.Release.TrySetResult();
        Assert.True(await first);
        Assert.Empty(firstSender.Submissions);
        Assert.Single(secondSender.Submissions);
        Assert.Equal(1, await f.Db.SignatureEvents.CountAsync(x => x.Kind == "EmailStatusRecorded"));
    }

    [Fact]
    public async Task Retained_payload_must_match_request_token_even_when_encryption_is_valid()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        await AddMail(f, SignatureSecrets.NewToken(), "Invitation", generation: 2);
        var sender = new FakeSender();
        var worker = Worker(f, sender);
        await worker.ProcessNextAsync(f.Db); // Earlier generation is no longer eligible.
        await worker.ProcessNextAsync(f.Db);
        var rows = await f.Db.SignatureOutbox.AsNoTracking().OrderBy(x => x.Generation).ToListAsync();
        Assert.Equal("Stale", rows[0].State);
        Assert.Equal("Failed", rows[1].State);
        Assert.Equal("signature_email_integrity", rows[1].LastErrorCode);
        Assert.Empty(sender.Submissions);
    }

    [Fact]
    public async Task Interrupted_submission_keeps_its_operation_and_recovers_after_lease_expiry()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        using var cancellation = new CancellationTokenSource();
        var sender = new FakeSender { Send = (_, _) => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); } };
        var worker = Worker(f, sender);
        await Assert.ThrowsAsync<OperationCanceledException>(() => worker.ProcessNextAsync(f.Db, cancellation.Token));
        var prior = await f.Db.SignatureOutbox.AsNoTracking().SingleAsync();
        Assert.NotNull(prior.ProviderOperationId);
        Assert.NotNull(prior.LeaseId);
        Assert.False(await worker.ProcessNextAsync(f.Db));
        f.Time.Advance(TimeSpan.FromMinutes(6));
        Assert.True(await worker.ProcessNextAsync(f.Db));
        Assert.Equal(prior.ProviderOperationId!.Value.ToString("D"), Assert.Single(sender.Polls));
        Assert.Single(sender.Submissions);
    }

    [Fact]
    public async Task Already_submitted_invitation_is_only_polled_after_the_request_is_revoked()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        var sender = new FakeSender { Send = (id, _) => Task.FromResult(new SignatureEmailResult("Sending", id.ToString("D"))) };
        var worker = Worker(f, sender);
        await worker.ProcessNextAsync(f.Db);
        var request = await f.Db.SignatureRequests.SingleAsync();
        request.State = "Revoked"; request.CompletedAtUtc = f.Time.GetUtcNow().UtcDateTime; request.Revision++;
        await f.Db.SaveChangesAsync();
        f.Time.Advance(TimeSpan.FromMinutes(1));
        await worker.ProcessNextAsync(f.Db);
        Assert.Single(sender.Polls);
        Assert.Single(sender.Submissions);
        Assert.Equal("Revoked", (await f.Db.SignatureRequests.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task Provider_retry_delay_is_preserved_in_the_durable_schedule()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        var sender = new FakeSender { Send = (id, _) => Task.FromResult(new SignatureEmailResult("Sending", id.ToString("D"), 600)) };
        var worker = Worker(f, sender);
        await worker.ProcessNextAsync(f.Db);
        f.Time.Advance(TimeSpan.FromMinutes(9));
        Assert.False(await worker.ProcessNextAsync(f.Db));
        f.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await worker.ProcessNextAsync(f.Db));
        Assert.Single(sender.Polls);
    }

    [Fact]
    public async Task A_different_provider_operation_cannot_be_recorded_as_this_invitation_sent()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        var sender = new FakeSender { Send = (_, _) => Task.FromResult(new SignatureEmailResult("Sent", Guid.NewGuid().ToString("D"))) };
        await Worker(f, sender).ProcessNextAsync(f.Db);
        var row = await f.Db.SignatureOutbox.AsNoTracking().SingleAsync();
        Assert.Equal("Polling", row.State);
        Assert.Equal("Unknown", row.ProviderStatus);
        Assert.Null(row.CompletedAtUtc);
    }

    [Fact]
    public async Task Disabling_email_after_submission_never_relabels_the_attempt_as_suppressed()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        var options = Options();
        var sender = new FakeSender { Send = (id, _) => Task.FromResult(new SignatureEmailResult("Sending", id.ToString("D"))) };
        var worker = Worker(f, sender, options);
        await worker.ProcessNextAsync(f.Db);
        options.EmailEnabled = false;
        f.Time.Advance(TimeSpan.FromMinutes(1));
        await worker.ProcessNextAsync(f.Db);
        var row = await f.Db.SignatureOutbox.AsNoTracking().SingleAsync();
        Assert.Equal("NeedsReview", row.State);
        Assert.Equal("Sending", row.ProviderStatus);
        Assert.Equal("signature_email_polling_disabled", row.LastErrorCode);
        Assert.Empty(sender.Polls);
        Assert.Single(sender.Submissions);
    }

    [Fact]
    public async Task Production_environment_cannot_claim_or_send_even_with_email_options_enabled()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        var options = Options(); options.ExpectedEnvironment = "Production"; options.ExpectedDatabaseName = "SatiProduction";
        var sender = new FakeSender();
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => Worker(f, sender, options).ProcessNextAsync(f.Db));
        Assert.Equal(0, (await f.Db.SignatureOutbox.AsNoTracking().SingleAsync()).Attempts);
        Assert.Empty(sender.Submissions);
    }

    [Fact]
    public async Task A_late_status_response_cannot_overwrite_the_worker_that_reclaimed_its_lease()
    {
        await using var f = await Fixture.Create(); await f.Issue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSender = new FakeSender
        {
            Send = (id, _) => Task.FromResult(new SignatureEmailResult("Sending", id.ToString("D"))),
            Poll = async id => { entered.TrySetResult(); await release.Task; return new("Sent", id); }
        };
        var worker = Worker(f, firstSender);
        await worker.ProcessNextAsync(f.Db);
        f.Time.Advance(TimeSpan.FromMinutes(1));
        var late = worker.ProcessNextAsync(f.Db);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Time.Advance(TimeSpan.FromMinutes(6));
        await using var other = OtherContext(f);
        var secondSender = new FakeSender { Poll = id => Task.FromResult(new SignatureEmailResult("Sending", id)) };
        await Worker(f, secondSender).ProcessNextAsync(other);
        release.TrySetResult();
        Assert.True(await late);
        var retained = await f.Db.SignatureOutbox.AsNoTracking().SingleAsync();
        Assert.Equal("Sending", retained.ProviderStatus);
        Assert.Equal("Polling", retained.State);
        Assert.Null(retained.CompletedAtUtc);
        Assert.Equal(2, await f.Db.SignatureEvents.CountAsync(x => x.Kind == "EmailStatusRecorded"));
    }

    private static async Task SuppressInvitation(Fixture f)
    {
        var options = Options(); options.EmailEnabled = false;
        await Worker(f, new FakeSender(), options).ProcessNextAsync(f.Db);
    }
    private static async Task Sign(Fixture f, string token)
    {
        var auth = await f.Workflow.AuthenticateAsync(token, Fixture.Pin);
        await f.Workflow.PortalDocumentAsync(auth.SessionToken);
        await f.Workflow.ConsentAsync(auth.SessionToken, true, true);
        await f.Workflow.CompleteAsync(auth.SessionToken, "Synthetic Person", true);
    }
    private static async Task AddMail(Fixture f, string token, string purpose, int generation = 1)
    {
        var request = await f.Db.SignatureRequests.AsNoTracking().SingleAsync();
        var row = new SignatureOutbox { AgencyId = request.AgencyId, RequestId = request.Id, Purpose = purpose, Generation = generation, NextAttemptAtUtc = f.Time.GetUtcNow().UtcDateTime };
        f.Db.Add(row); await f.Db.SaveChangesAsync();
        await f.Outbox.ProtectAsync(row, new(request.DeliveryEmail, "https://sign.example.test/" + (purpose == "Invitation" ? "s/" : "r/") + token, purpose));
        row.Revision++; await f.Db.SaveChangesAsync();
    }

    private sealed class FakeSender : ISignatureEmailSender
    {
        public Func<Guid, SignatureEmail, Task<SignatureEmailResult>> Send { get; set; } = (id, _) => Task.FromResult(new SignatureEmailResult("Sent", id.ToString("D")));
        public Func<string, Task<SignatureEmailResult>> Poll { get; set; } = id => Task.FromResult(new SignatureEmailResult("Sent", id));
        public List<(Guid Operation, SignatureEmail Email)> Submissions { get; } = [];
        public List<string> Polls { get; } = [];
        public Task<SignatureEmailResult> SendAsync(Guid operationId, SignatureEmail email, CancellationToken cancellationToken = default)
        { Submissions.Add((operationId, email)); return Send(operationId, email); }
        public Task<SignatureEmailResult> GetStatusAsync(string operationId, CancellationToken cancellationToken = default)
        { Polls.Add(operationId); return Poll(operationId); }
    }
    private sealed class BlockingKey : ISignatureOutboxKeyWrapper
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public bool Refuse { get; init; }
        public Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<byte[]> UnwrapAsync(byte[] wrappedKey, string keyId, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Refuse) throw new InvalidOperationException("Key must not be requested.");
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return wrappedKey.ToArray();
        }
    }
}
