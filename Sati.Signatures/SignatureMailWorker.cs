using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Signatures;

/// <summary>
/// API-only mail recovery. Hosts supply a fresh context per call. A persisted lease excludes
/// another worker; a persisted operation GUID makes recovery poll an existing submission.
/// ACS operation completion never establishes delivery to the recipient's inbox.
/// </summary>
public sealed class SignatureMailWorker(SignatureFeature feature, SignatureOptions options,
    SignatureOutboxProtector protector, ISignatureEmailSender sender, TimeProvider clock)
{
    public const int MaximumAttempts = 5;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private sealed record Lease(long RowId, Guid Id, bool Exhausted);

    public async Task<bool> ProcessNextAsync(DbContext db, CancellationToken ct = default)
    {
        feature.RequireEnabled();
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Mail preparation requires its own clean, short-lived database context.");
        var lease = await ClaimAsync(db, ct);
        if (lease is null) return false;
        var pollAttempted = false;
        try
        {
            var row = await OwnedAsync(db, lease, ct);
            if (row is null) return true;
            if (lease.Exhausted)
            {
                await CompleteAsync(db, lease, null, "signature_email_attempt_limit", "NeedsReview", ct);
                return true;
            }
            // Disabled email and unlisted recipients are final, visible suppression, not delivery.
            var request = await RequestAsync(db, row, ct);
            if (row.ProviderOperationId is Guid existing)
            {
                // Poll even when the request subsequently ended: this cannot send new mail and
                // is necessary to report the outcome of the submission already attempted.
                if (!options.EmailEnabled)
                {
                    await CompleteAsync(db, lease, null, "signature_email_polling_disabled", "NeedsReview", ct);
                    return true;
                }
                pollAttempted = true;
                var result = await sender.GetStatusAsync(existing.ToString("D"), ct);
                ValidateOperation(result, existing);
                if (result.State == "Suppressed")
                    await CompleteAsync(db, lease, null, "signature_email_polling_disabled", "NeedsReview", ct, polled: true);
                else await CompleteAsync(db, lease, result, null, null, ct, polled: true);
                return true;
            }
            if (!options.EmailEnabled || request is null || options.AllowedTestRecipients?.Contains(request.DeliveryEmail, StringComparer.Ordinal) != true)
            {
                await CompleteAsync(db, lease, new("Suppressed"), "signature_email_suppressed", null, ct);
                return true;
            }
            if (!await CanSubmitAsync(db, row, request, ct))
            {
                await CompleteAsync(db, lease, null, "signature_email_stale", "Stale", ct);
                return true;
            }
            // Key access occurs outside the database transaction. The payload itself is immutable
            // and authenticated against this agency/request/outbox row/purpose/generation.
            var email = await protector.UnprotectAsync(row, ct);
            ValidatePayload(row, request, email);
            var operation = await PrepareSubmissionAsync(db, lease, email, ct);
            if (operation is null) return true;
            await SubmitAsync(db, lease, operation.Value, email, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Keep the committed lease/operation. The next worker recovers after lease expiry.
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is not DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var integrity = exception is CryptographicException || exception is SignatureWorkflowException { Code: "signature_email_payload" };
            await CompleteAsync(db, lease, null, integrity ? "signature_email_integrity" : "signature_email_provider_unavailable",
                integrity ? "Failed" : null, ct, polled: pollAttempted);
            return true;
        }
    }

    private async Task<Lease?> ClaimAsync(DbContext db, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var now = Now;
            var row = await db.Set<SignatureOutbox>().Where(x => x.CompletedAtUtc == null && x.NextAttemptAtUtc <= now &&
                    (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
                .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (row is null) { await transaction.CommitAsync(ct); return null; }
            row.LeaseId = Guid.NewGuid();
            row.LeaseUntilUtc = now.Add(LeaseDuration);
            var exhausted = row.Attempts >= MaximumAttempts;
            if (!exhausted) row.Attempts++;
            row.State = "Processing";
            row.Revision++;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            var lease = new Lease(row.Id, row.LeaseId.Value, exhausted);
            db.ChangeTracker.Clear();
            return lease;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return null;
        }
    }

    private Task<SignatureOutbox?> OwnedAsync(DbContext db, Lease lease, CancellationToken ct)
    {
        var now = Now;
        return db.Set<SignatureOutbox>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == lease.RowId &&
            x.LeaseId == lease.Id && x.LeaseUntilUtc > now && x.CompletedAtUtc == null, ct);
    }
    private static Task<SignatureRequest?> RequestAsync(DbContext db, SignatureOutbox row, CancellationToken ct) =>
        db.Set<SignatureRequest>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.RequestId && x.AgencyId == row.AgencyId, ct);

    private async Task<bool> CanSubmitAsync(DbContext db, SignatureOutbox row, SignatureRequest request, CancellationToken ct)
    {
        if (request.ExternalAccessRevokedAtUtc is not null || request.ExpiresAtUtc <= Now || request.LockedAtUtc is not null || request.FailedPinAttempts >= 5) return false;
        if (await db.Set<SignatureOutbox>().AnyAsync(x => x.RequestId == row.RequestId && x.AgencyId == row.AgencyId && x.Purpose == row.Purpose && x.Generation > row.Generation, ct)) return false;
        if (row.Purpose == "Receipt")
            return request.State == "Signed" && request.CompletedAtUtc is not null &&
                await db.Set<SignaturePackage>().AnyAsync(x => x.RequestId == row.RequestId && x.AgencyId == row.AgencyId, ct);
        if (row.Purpose != "Invitation" || !SignatureRules.IsOpen(request.State)) return false;
        var frozen = await db.Set<FrozenSignatureDocument>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.FrozenDocumentId && x.AgencyId == request.AgencyId && x.PersonId == request.PersonId, ct);
        return frozen is not null && await db.Set<SignatureSourceDocument>().AnyAsync(x => x.Id == frozen.DocumentArtifactId && x.AgencyId == request.AgencyId && x.PersonId == request.PersonId && x.SupersededByArtifactId == null, ct);
    }

    private void ValidatePayload(SignatureOutbox row, SignatureRequest request, SignatureEmail email)
    {
        if (email.Purpose != row.Purpose || email.Recipient != request.DeliveryEmail ||
            !Uri.TryCreate(options.PortalBaseUri, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttps || origin.Port != 443 ||
            origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0 ||
            !Uri.TryCreate(email.Link, UriKind.Absolute, out var link) || link.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority) ||
            link.Query.Length != 0 || link.Fragment.Length != 0 || link.UserInfo.Length != 0 || email.Link.Contains('%') || email.Link.Contains('\\'))
            throw InvalidPayload();
        var token = link.Segments.Last();
        var prefix = row.Purpose == "Invitation" ? "/s/" : "/r/";
        if (!SignatureSecrets.IsToken(token) || link.AbsolutePath != prefix + token || SignatureSecrets.Hash(token) != request.TokenSha256)
            throw InvalidPayload();
    }
    private static SignatureWorkflowException InvalidPayload() => new("signature_email_payload", "The retained signature delivery details could not be verified.", 503);

    private async Task<Guid?> PrepareSubmissionAsync(DbContext db, Lease lease, SignatureEmail email, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await OwnedAsync(db, lease, ct);
        if (row is null) { await transaction.CommitAsync(ct); return null; }
        var request = await RequestAsync(db, row, ct);
        if (request is null || !await CanSubmitAsync(db, row, request, ct))
        {
            await transaction.CommitAsync(ct);
            await CompleteAsync(db, lease, null, "signature_email_stale", "Stale", ct);
            return null;
        }
        ValidatePayload(row, request, email);
        if (row.ProviderOperationId is not null) { await transaction.CommitAsync(ct); return null; }
        db.Attach(row);
        row.ProviderOperationId = Guid.NewGuid();
        row.SubmittedAtUtc = Now; // Submission attempt prepared; this is not provider acceptance.
        row.ProviderStatus = "Unknown";
        row.Revision++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return row.ProviderOperationId;
    }

    private async Task SubmitAsync(DbContext db, Lease lease, Guid operation, SignatureEmail email, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var row = await OwnedAsync(db, lease, ct);
            if (row is null) { await transaction.CommitAsync(ct); return; }
            var request = await RequestAsync(db, row, ct);
            if (row.ProviderOperationId != operation || request is null || !await CanSubmitAsync(db, row, request, ct))
            {
                await transaction.CommitAsync(ct);
                await CompleteAsync(db, lease, null, "signature_email_stale", "Stale", ct);
                return;
            }
            ValidatePayload(row, request, email);
            // Retain read locks during this bounded call. A staff revocation cannot commit before
            // the final validation and then be followed by a stale POST from this worker.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(35));
            var result = await sender.SendAsync(operation, email, timeout.Token);
            ValidateOperation(result, operation);
            Apply(db, row, request, result, null, null, polled: false);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static void ValidateOperation(SignatureEmailResult result, Guid operation)
    {
        if (result.State == "Suppressed" && result.OperationId is null) return;
        if (!Guid.TryParseExact(result.OperationId, "D", out var supplied) || supplied != operation ||
            result.State is not ("Queued" or "Sending" or "Sent" or "Failed" or "Canceled" or "Unknown") || result.RetryAfterSeconds is < 0 or > 86400)
            throw AzureSignatureTransport.Unavailable();
    }

    private async Task CompleteAsync(DbContext db, Lease lease, SignatureEmailResult? result, string? error, string? terminal, CancellationToken ct, bool polled = false)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await OwnedAsync(db, lease, ct);
        if (row is null) { await transaction.CommitAsync(ct); return; }
        var request = await RequestAsync(db, row, ct);
        if (request is null) throw InvalidPayload();
        Apply(db, row, request, result, error, terminal, polled);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }

    private void Apply(DbContext db, SignatureOutbox row, SignatureRequest request, SignatureEmailResult? result, string? error, string? terminal, bool polled)
    {
        db.Attach(row);
        db.Attach(request);
        var now = Now;
        if (result is not null) row.ProviderStatus = result.State;
        if (polled) row.LastPolledAtUtc = now;
        row.LastErrorCode = error;
        row.State = terminal ?? (result?.State switch
        {
            "Sent" => "Sent", "Suppressed" => "Suppressed", "Failed" => "Failed", "Canceled" => "Canceled",
            _ => row.Attempts >= MaximumAttempts ? "NeedsReview" : row.ProviderOperationId is null ? "Retry" : "Polling"
        });
        if (row.State is "Sent" or "Suppressed" or "Failed" or "Canceled" or "Stale" or "NeedsReview") row.CompletedAtUtc = now;
        else row.NextAttemptAtUtc = now.AddSeconds(Math.Max(30 * (1 << Math.Min(4, row.Attempts - 1)), result?.RetryAfterSeconds ?? 0));
        row.LeaseId = null;
        row.LeaseUntilUtc = null;
        row.Revision++;
        request.Revision++;
        db.Add(new SignatureEvent { AgencyId = request.AgencyId, RequestId = request.Id, Sequence = request.Revision,
            Kind = "EmailStatusRecorded", ActorKind = "System", OccurredAtUtc = now,
            DetailJson = JsonSerializer.Serialize(new { outboxId = row.Id, purpose = row.Purpose, queueState = row.State, providerStatus = row.ProviderStatus, errorCode = error }) });
    }
}
