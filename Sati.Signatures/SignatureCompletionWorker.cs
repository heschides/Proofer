using System.Data;
using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Signatures;

/// <summary>API-only package preparation. Hosts provide a fresh, single-attempt context per call.</summary>
public sealed class SignatureCompletionWorker(SignatureFeature feature, ISignatureBlobStore blobs,
    SignaturePackageBuilder builder, SignatureOutboxProtector outbox, TimeProvider clock)
{
    public async Task<bool> ProcessNextAsync(DbContext db, CancellationToken ct = default)
    {
        feature.RequireEnabled();
        var id = await db.Set<SignatureCompletion>().AsNoTracking()
            .Where(x => !db.Set<SignaturePackage>().Any(p => p.CompletionId == x.Id))
            .OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (id is null) return false;
        await BuildAsync(db, id.Value, ct);
        return true;
    }

    public async Task<SignaturePackage> BuildAsync(DbContext db, int completionId, CancellationToken ct = default)
    {
        feature.RequireEnabled();
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Package preparation requires its own short-lived transaction.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var existing = await db.Set<SignaturePackage>().AsNoTracking().SingleOrDefaultAsync(x => x.CompletionId == completionId, ct);
            if (existing is not null) { await transaction.CommitAsync(ct); return existing; }
            var completion = await db.Set<SignatureCompletion>().AsNoTracking().SingleAsync(x => x.Id == completionId, ct);
            var request = await db.Set<SignatureRequest>().SingleAsync(x => x.Id == completion.RequestId && x.AgencyId == completion.AgencyId, ct);
            var frozen = await db.Set<FrozenSignatureDocument>().AsNoTracking().SingleAsync(x => x.Id == completion.FrozenDocumentId && x.AgencyId == completion.AgencyId, ct);
            var session = await db.Set<SignatureSession>().AsNoTracking().SingleAsync(x => x.Id == completion.SessionId && x.AgencyId == completion.AgencyId && x.RequestId == request.Id, ct);
            var consent = await db.Set<SignatureConsent>().AsNoTracking().SingleAsync(x => x.Id == completion.ConsentId && x.SessionId == session.Id && x.AgencyId == completion.AgencyId && x.RequestId == request.Id, ct);
            var signedSequence = await db.Set<SignatureEvent>().AsNoTracking().Where(x => x.RequestId == request.Id &&
                x.AgencyId == request.AgencyId && x.SessionId == session.Id && x.Kind == "Signed").Select(x => x.Sequence).SingleAsync(ct);
            // The certificate carries the selected signing episode, not an unbounded
            // history of every successful authentication or session extension.
            var events = await db.Set<SignatureEvent>().AsNoTracking().Where(x => x.RequestId == request.Id && x.AgencyId == request.AgencyId &&
                    (x.Kind == "Issued" ||
                     (x.SessionId == session.Id && (x.Kind == "Authenticated" || x.Kind == "DocumentReleased" || x.Kind == "ElectronicConsent" || x.Kind == "Signed")) ||
                     ((x.Kind == "PinRejected" || x.Kind == "PinLocked") && x.Sequence < signedSequence)))
                .OrderBy(x => x.Sequence).Take(11).ToListAsync(ct);
            var original = await blobs.ReadAsync(frozen.BlobPath, ct);
            var bytes = builder.Build(original, frozen, request, completion, session, consent, events);
            var now = clock.GetUtcNow().UtcDateTime;
            var package = new SignaturePackage { AgencyId = request.AgencyId, RequestId = request.Id, CompletionId = completion.Id,
                ContentSha256 = SignatureSecrets.Hash(bytes), ByteCount = bytes.LongLength, CreatedAtUtc = now,
                BlobPath = $"agency/{request.AgencyId}/signature-packages/{Guid.NewGuid():N}.pdf" };
            // A failed database commit can leave an unreferenced immutable blob. Never overwrite
            // or automatically delete it; reviewed retention/recovery handles such objects.
            await blobs.WriteOnceAsync(package.BlobPath, bytes, ct);
            db.Add(package);

            SignatureOutbox? receipt = null;
            SignatureEmail? receiptEmail = null;
            if (request.ExpiresAtUtc > now && request.LockedAtUtc is null && request.ExternalAccessRevokedAtUtc is null)
            {
                var invitation = await db.Set<SignatureOutbox>().AsNoTracking().SingleAsync(x => x.RequestId == request.Id && x.AgencyId == request.AgencyId && x.Purpose == "Invitation", ct);
                var email = await outbox.UnprotectAsync(invitation, ct);
                var address = new Uri(email.Link, UriKind.Absolute);
                var token = address.Segments.Last();
                if (email.Purpose != "Invitation" || !string.Equals(email.Recipient, request.DeliveryEmail, StringComparison.OrdinalIgnoreCase) ||
                    address.Scheme != Uri.UriSchemeHttps || address.Query.Length != 0 || address.Fragment.Length != 0 || address.UserInfo.Length != 0 ||
                    !SignatureSecrets.IsToken(token) || SignatureSecrets.Hash(token) != request.TokenSha256 ||
                    !address.AbsolutePath.EndsWith("/s/" + token, StringComparison.Ordinal))
                    throw new SignatureWorkflowException("signature_integrity_failed", "The retained invitation could not be verified.", 503);
                var link = new UriBuilder(address) { Path = address.AbsolutePath[..^(token.Length + 3)] + "/r/" + token }.Uri.AbsoluteUri;
                receiptEmail = new SignatureEmail(email.Recipient, link, "Receipt");
                receipt = new SignatureOutbox { AgencyId = request.AgencyId, RequestId = request.Id, Purpose = "Receipt", NextAttemptAtUtc = now };
                db.Add(receipt);
            }
            request.Revision++;
            db.Add(new SignatureEvent { AgencyId = request.AgencyId, RequestId = request.Id, Sequence = request.Revision,
                Kind = "PackagePrepared", ActorKind = "System", OccurredAtUtc = now });
            await db.SaveChangesAsync(ct);
            if (receipt is not null)
            {
                await outbox.ProtectAsync(receipt, receiptEmail!, ct);
                receipt.Revision++;
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return package;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
