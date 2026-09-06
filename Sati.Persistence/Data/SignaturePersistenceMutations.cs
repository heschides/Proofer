using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

/// <summary>Cascade owner for source and signer changes in both full server contexts.</summary>
public static class SignaturePersistenceMutations
{
    public static async Task RevokeOpenForArtifactAsync(DbContext db, int artifactId, int? staffUserId,
        DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var requests = await db.Set<SignatureRequest>().Where(request =>
            (request.State == "Issued" || request.State == "Viewed") &&
            db.Set<FrozenSignatureDocument>().Any(document => document.Id == request.FrozenDocumentId &&
                document.AgencyId == request.AgencyId && document.PersonId == request.PersonId && document.DocumentArtifactId == artifactId))
            .ToListAsync(cancellationToken);
        if (requests.Count == 0) return;
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Replacing a signed-source document requires one database transaction.");
        foreach (var request in requests)
        {
            if (!SignatureRules.IsOpen(request.State)) continue;
            request.State = "Revoked";
            request.CompletedAtUtc = nowUtc;
            request.TerminalReason = "The document was replaced. Review the new version through a new request.";
            request.AuthenticationVersion = checked(request.AuthenticationVersion + 1);
            request.Revision = checked(request.Revision + 1);
            db.Set<SignatureEvent>().Add(new SignatureEvent
            {
                AgencyId = request.AgencyId, RequestId = request.Id, Sequence = request.Revision,
                Kind = "ArtifactSuperseded", ActorKind = staffUserId is null ? "System" : "Staff",
                ActorUserId = staffUserId, OccurredAtUtc = nowUtc, DetailJson = "{}"
            });
        }
        // The existing replacement save writes this evidence in the same transaction.
    }

    public static async Task RevokeOpenForSignerAsync(DbContext db, int personId, int? signerContactId,
        int? staffUserId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var requests = await db.Set<SignatureRequest>().Where(request => request.PersonId == personId &&
            request.SignerContactId == signerContactId && (signerContactId != null || request.SignerCapacity == "Consumer") &&
            (request.State == "Issued" || request.State == "Viewed" ||
             (request.State == "Signed" && request.ExternalAccessRevokedAtUtc == null))).ToListAsync(cancellationToken);
        if (requests.Count == 0) return;
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Changing a signer record requires one database transaction.");
        foreach (var request in requests)
        {
            var isSigned = request.State == "Signed";
            if (isSigned)
            {
                if (request.ExternalAccessRevokedAtUtc is not null) continue;
                request.ExternalAccessRevokedAtUtc = nowUtc;
                request.ExternalAccessRevocationReason = "The signer record changed. External access ended pending a new identity and delivery review.";
            }
            else
            {
                if (!SignatureRules.IsOpen(request.State)) continue;
                request.State = "Revoked";
                request.CompletedAtUtc = nowUtc;
                request.TerminalReason = "The signer record changed. Confirm the current identity and delivery address before issuing a new invitation.";
            }
            request.AuthenticationVersion = checked(request.AuthenticationVersion + 1);
            request.Revision = checked(request.Revision + 1);
            db.Set<SignatureEvent>().Add(new SignatureEvent
            {
                AgencyId = request.AgencyId, RequestId = request.Id, Sequence = request.Revision,
                Kind = isSigned ? "ExternalAccessRevoked" : "SignerRecordChanged", ActorKind = staffUserId is null ? "System" : "Staff",
                ActorUserId = staffUserId, OccurredAtUtc = nowUtc, DetailJson = "{}"
            });
        }
        // Caller saves both the changed signer and this revocation in the same transaction.
    }
}
