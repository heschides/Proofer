using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

/// <summary>Canonical signing schema and durable write invariants shared by every server context.</summary>
public static class SignaturePersistenceModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<SignatureDatabaseEnvironment>(e =>
        {
            e.HasNoKey().ToView("SignatureDatabaseEnvironment");
            e.Property(x => x.DatabaseName).HasMaxLength(128);
            e.Property(x => x.EnvironmentName).HasMaxLength(20);
        });
        builder.Entity<SignatureSourceDocument>(e =>
        {
            e.ToView("SignatureSourceDocuments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(40);
            e.Property(x => x.Origin).HasMaxLength(30);
            e.Property(x => x.CycleStart).HasColumnType("date");
            e.Property(x => x.ContentSha256).HasColumnType("char(64)");
            e.Property(x => x.BlankFieldsJson).HasMaxLength(4000);
        });
        builder.Entity<FrozenSignatureDocument>(e =>
        {
            e.ToTable("FrozenSignatureDocuments", t => t.HasCheckConstraint("CK_FrozenSignatureDocuments_Bytes", $"[ByteCount] > 0 AND [ByteCount] <= {SignatureRules.MaximumPdfBytes}"));
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.AgencyId, x.PersonId, x.Id });
            e.HasIndex(x => x.DocumentArtifactId).IsUnique();
            e.Property(x => x.ContentSha256).IsRequired().HasColumnType("char(64)");
            e.Property(x => x.BlobPath).IsRequired().HasMaxLength(400);
        });
        builder.Entity<SignatureRequest>(e =>
        {
            e.ToTable("SignatureRequests", t =>
            {
                t.HasCheckConstraint("CK_SignatureRequests_Counters", "[Revision] > 0 AND [AuthenticationVersion] > 0 AND [FailedPinAttempts] BETWEEN 0 AND 5 AND [PinIterations] BETWEEN 100000 AND 2000000");
                t.HasCheckConstraint("CK_SignatureRequests_Expiry", "[ExpiresAtUtc] > [IssuedAtUtc]");
                t.HasCheckConstraint("CK_SignatureRequests_State", "[State] IN ('Issued','Viewed','Signed','Declined','ChangesRequested','Expired','Revoked')");
                t.HasCheckConstraint("CK_SignatureRequests_Terminal", "([State] IN ('Issued','Viewed') AND [CompletedAtUtc] IS NULL) OR ([State] IN ('Signed','Declined','ChangesRequested','Expired','Revoked') AND [CompletedAtUtc] IS NOT NULL)");
                t.HasCheckConstraint("CK_SignatureRequests_Lock", "([FailedPinAttempts] < 5 AND [LockedAtUtc] IS NULL) OR ([FailedPinAttempts] = 5 AND [LockedAtUtc] IS NOT NULL)");
                t.HasCheckConstraint("CK_SignatureRequests_AuthorizationWithdrawal", "([AuthorizationRevokedAtUtc] IS NULL AND [AuthorizationRevocationReason] IS NULL) OR ([State] = 'Signed' AND [AuthorizationRevokedAtUtc] IS NOT NULL AND [AuthorizationRevocationReason] IS NOT NULL)");
                t.HasCheckConstraint("CK_SignatureRequests_ExternalAccess", "([ExternalAccessRevokedAtUtc] IS NULL AND [ExternalAccessRevocationReason] IS NULL) OR ([State] = 'Signed' AND [ExternalAccessRevokedAtUtc] IS NOT NULL AND [ExternalAccessRevocationReason] IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.AgencyId, x.Id });
            e.HasAlternateKey(x => new { x.AgencyId, x.Id, x.FrozenDocumentId });
            e.HasIndex(x => x.TokenSha256).IsUnique();
            e.HasIndex(x => new { x.AgencyId, x.IssuedByUserId, x.ClientRequestId }).IsUnique();
            e.HasIndex(x => new { x.AgencyId, x.PersonId, x.State });
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.Property(x => x.SignerCapacity).IsRequired().HasMaxLength(32);
            e.Property(x => x.SignerName).IsRequired().HasMaxLength(120);
            e.Property(x => x.DeliveryEmail).IsRequired().HasMaxLength(254);
            e.Property(x => x.AuthorityEvidence).HasMaxLength(500);
            e.Property(x => x.TokenSha256).IsRequired().HasColumnType("char(64)");
            e.Property(x => x.PinHash).IsRequired().HasMaxLength(64);
            e.Property(x => x.PinSalt).IsRequired().HasMaxLength(32);
            e.Property(x => x.PinPepperWrapped).IsRequired().HasMaxLength(512);
            e.Property(x => x.PinKeyId).IsRequired().HasMaxLength(400);
            e.Property(x => x.State).IsRequired().HasMaxLength(32);
            e.Property(x => x.DisclosureVersion).IsRequired().HasMaxLength(32);
            e.Property(x => x.DisclosureText).IsRequired().HasMaxLength(8000);
            e.Property(x => x.IntentText).IsRequired().HasMaxLength(4000);
            e.Property(x => x.TerminalReason).HasMaxLength(500);
            e.Property(x => x.AuthorizationRevocationReason).HasMaxLength(500);
            e.Property(x => x.ExternalAccessRevocationReason).HasMaxLength(500);
            e.HasOne<FrozenSignatureDocument>().WithMany().HasForeignKey(x => new { x.AgencyId, x.PersonId, x.FrozenDocumentId })
                .HasPrincipalKey(x => new { x.AgencyId, x.PersonId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SignatureRequest>().WithMany().HasForeignKey(x => new { x.AgencyId, x.ReplacesRequestId })
                .HasPrincipalKey(x => new { x.AgencyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SignatureSession>(e =>
        {
            e.ToTable("SignatureSessions", t =>
            {
                t.HasCheckConstraint("CK_SignatureSessions_Counters", "[Revision] > 0 AND [AuthenticationVersion] > 0");
                t.HasCheckConstraint("CK_SignatureSessions_Expiry", "[ExpiresAtUtc] > [IssuedAtUtc]");
                t.HasCheckConstraint("CK_SignatureSessions_Access", "[AccessAcknowledgedAtUtc] IS NULL OR [DocumentReleasedAtUtc] IS NOT NULL");
                t.HasCheckConstraint("CK_SignatureSessions_Purpose", "[Purpose] IN ('Signing','Receipt')");
            });
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.AgencyId, x.RequestId, x.Id });
            e.HasIndex(x => x.TokenSha256).IsUnique();
            e.Property(x => x.TokenSha256).IsRequired().HasColumnType("char(64)");
            e.Property(x => x.Purpose).IsRequired().HasMaxLength(16).HasDefaultValue("Signing");
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasOne<SignatureRequest>().WithMany().HasForeignKey(x => new { x.AgencyId, x.RequestId })
                .HasPrincipalKey(x => new { x.AgencyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SignatureConsent>(e =>
        {
            e.ToTable("SignatureConsents");
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.AgencyId, x.RequestId, x.Id });
            e.HasIndex(x => x.SessionId).IsUnique();
            e.HasAlternateKey(x => new { x.AgencyId, x.RequestId, x.SessionId, x.Id });
            e.Property(x => x.DisclosureVersion).IsRequired().HasMaxLength(32);
            e.Property(x => x.DisclosureText).IsRequired().HasMaxLength(8000);
            e.HasOne<SignatureSession>().WithMany().HasForeignKey(x => new { x.AgencyId, x.RequestId, x.SessionId })
                .HasPrincipalKey(x => new { x.AgencyId, x.RequestId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SignatureEvent>(e =>
        {
            e.ToTable("SignatureEvents", t =>
            {
                t.HasCheckConstraint("CK_SignatureEvents_Sequence", "[Sequence] > 0");
                t.HasCheckConstraint("CK_SignatureEvents_Actor", "([ActorKind] = 'Staff' AND [ActorUserId] IS NOT NULL) OR ([ActorKind] IN ('Signer','System') AND [ActorUserId] IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.RequestId, x.Sequence }).IsUnique();
            e.Property(x => x.Kind).IsRequired().HasMaxLength(32);
            e.Property(x => x.ActorKind).IsRequired().HasMaxLength(16);
            e.Property(x => x.DetailJson).IsRequired().HasMaxLength(4000);
            e.HasOne<SignatureRequest>().WithMany().HasForeignKey(x => new { x.AgencyId, x.RequestId })
                .HasPrincipalKey(x => new { x.AgencyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SignatureSession>().WithMany().HasForeignKey(x => new { x.AgencyId, x.RequestId, x.SessionId })
                .HasPrincipalKey(x => new { x.AgencyId, x.RequestId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SignatureCompletion>(e =>
        {
            e.ToTable("SignatureCompletions");
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.AgencyId, x.RequestId, x.Id });
            e.HasIndex(x => x.RequestId).IsUnique();
            e.Property(x => x.TypedSignerName).IsRequired().HasMaxLength(120);
            e.Property(x => x.IntentText).IsRequired().HasMaxLength(4000);
            e.HasOne<SignatureRequest>().WithMany().HasForeignKey(x => new { x.AgencyId, x.RequestId, x.FrozenDocumentId })
                .HasPrincipalKey(x => new { x.AgencyId, x.Id, x.FrozenDocumentId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SignatureSession>().WithMany().HasForeignKey(x => new { x.AgencyId, x.RequestId, x.SessionId })
                .HasPrincipalKey(x => new { x.AgencyId, x.RequestId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SignatureConsent>().WithMany().HasForeignKey(x => new { x.AgencyId, x.RequestId, x.SessionId, x.ConsentId })
                .HasPrincipalKey(x => new { x.AgencyId, x.RequestId, x.SessionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SignaturePackage>(e =>
        {
            e.ToTable("SignaturePackages", t => t.HasCheckConstraint("CK_SignaturePackages_Bytes", $"[ByteCount] > 0 AND [ByteCount] <= {SignatureRules.MaximumPdfBytes * 2}"));
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RequestId).IsUnique();
            e.HasIndex(x => x.CompletionId).IsUnique();
            e.Property(x => x.ContentSha256).IsRequired().HasColumnType("char(64)");
            e.Property(x => x.BlobPath).IsRequired().HasMaxLength(400);
            e.HasOne<SignatureCompletion>().WithMany().HasForeignKey(x => new { x.AgencyId, x.RequestId, x.CompletionId })
                .HasPrincipalKey(x => new { x.AgencyId, x.RequestId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SignatureOutbox>(e =>
        {
            e.ToTable("SignatureOutbox", t =>
            {
                t.HasCheckConstraint("CK_SignatureOutbox_Counters", "[Revision] > 0 AND [Generation] > 0 AND [Attempts] >= 0");
                t.HasCheckConstraint("CK_SignatureOutbox_Lease", "([LeaseId] IS NULL AND [LeaseUntilUtc] IS NULL) OR ([LeaseId] IS NOT NULL AND [LeaseUntilUtc] IS NOT NULL)");
                t.HasCheckConstraint("CK_SignatureOutbox_Payload", "([PayloadCiphertext] IS NULL AND [PayloadNonce] IS NULL AND [PayloadTag] IS NULL AND [PayloadWrappedKey] IS NULL AND [PayloadKeyId] IS NULL) OR ([PayloadCiphertext] IS NOT NULL AND [PayloadNonce] IS NOT NULL AND [PayloadTag] IS NOT NULL AND [PayloadWrappedKey] IS NOT NULL AND [PayloadKeyId] IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.RequestId, x.Purpose, x.Generation }).IsUnique();
            e.HasIndex(x => new { x.State, x.NextAttemptAtUtc });
            e.HasIndex(x => x.ProviderOperationId).IsUnique();
            e.Property(x => x.Purpose).IsRequired().HasMaxLength(32);
            e.Property(x => x.State).IsRequired().HasMaxLength(32);
            e.Property(x => x.PayloadCiphertext).HasMaxLength(16000);
            e.Property(x => x.PayloadNonce).HasMaxLength(12);
            e.Property(x => x.PayloadTag).HasMaxLength(16);
            e.Property(x => x.PayloadWrappedKey).HasMaxLength(512);
            e.Property(x => x.PayloadKeyId).HasMaxLength(400);
            e.Property(x => x.LastErrorCode).HasMaxLength(64);
            e.Property(x => x.ProviderStatus).HasMaxLength(32);
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasOne<SignatureRequest>().WithMany().HasForeignKey(x => new { x.AgencyId, x.RequestId })
                .HasPrincipalKey(x => new { x.AgencyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        // SQL datetime2 has no timezone marker. These columns contain UTC instants;
        // preserve that meaning when materialized so JSON and browser clocks agree.
        var utc = new ValueConverter<DateTime, DateTime>(
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        foreach (var entity in builder.Model.GetEntityTypes().Where(e => e.ClrType.Namespace == typeof(SignatureRequest).Namespace &&
                     (e.ClrType.Name.StartsWith("Signature", StringComparison.Ordinal) || e.ClrType == typeof(FrozenSignatureDocument))))
            foreach (var property in entity.GetProperties().Where(p => p.Name.EndsWith("AtUtc", StringComparison.Ordinal) &&
                         (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?))))
                property.SetValueConverter(utc);
    }

    /// <summary>Clinical principals belong only to the full migration/API model, never the portal.</summary>
    public static void ConfigureClinicalRelationships<TArtifact, TAgency, TUser, TPerson, TContact>(ModelBuilder builder)
        where TArtifact : class where TAgency : class where TUser : class where TPerson : class where TContact : class
    {
        builder.Entity<TArtifact>().HasAlternateKey("AgencyId", "PersonId", "Id");
        builder.Entity<TContact>().HasAlternateKey("PersonId", "Id");
        builder.Entity<FrozenSignatureDocument>().HasOne<TArtifact>().WithMany()
            .HasForeignKey(x => new { x.AgencyId, x.PersonId, x.DocumentArtifactId })
            .HasPrincipalKey("AgencyId", "PersonId", "Id").OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FrozenSignatureDocument>().HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FrozenSignatureDocument>().HasOne<TPerson>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FrozenSignatureDocument>().HasOne<TUser>().WithMany().HasForeignKey(x => x.StoredByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SignatureRequest>().HasOne<TUser>().WithMany().HasForeignKey(x => x.IssuedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SignatureRequest>().HasOne<TContact>().WithMany().HasForeignKey(x => new { x.PersonId, x.SignerContactId })
            .HasPrincipalKey("PersonId", "Id").OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SignatureEvent>().HasOne<TUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }

    public static void ProtectDocumentArtifacts<TArtifact>(ChangeTracker tracker) where TArtifact : class
    {
        foreach (var e in tracker.Entries<TArtifact>())
        {
            if (e.State != EntityState.Modified) continue;
            Only(e, "SupersededByArtifactId");
            var superseded = e.Property("SupersededByArtifactId");
            var id = (int)e.Property("Id").CurrentValue!;
            if (superseded.CurrentValue is not int successor || successor <= 0 ||
                superseded.OriginalValue is int original && original != id)
                throw new InvalidOperationException("Document metadata is immutable; replacement must retain the previous version.");
        }
    }

    public static void ProtectWrites(ChangeTracker tracker)
    {
        if (tracker.Entries<FrozenSignatureDocument>().Any(Changed) || tracker.Entries<SignatureConsent>().Any(Changed) ||
            tracker.Entries<SignatureEvent>().Any(Changed) || tracker.Entries<SignatureCompletion>().Any(Changed) ||
            tracker.Entries<SignaturePackage>().Any(Changed) || tracker.Entries<SignatureSourceDocument>().Any(e => e.State is not (EntityState.Unchanged or EntityState.Detached)))
            throw new InvalidOperationException("Frozen documents, signing decisions, consent and evidence are immutable.");

        foreach (var e in tracker.Entries<SignatureRequest>())
        {
            NoDeletion(e);
            if (e.State != EntityState.Modified) continue;
            Only(e, nameof(SignatureRequest.State), nameof(SignatureRequest.Revision), nameof(SignatureRequest.FailedPinAttempts),
                nameof(SignatureRequest.LockedAtUtc), nameof(SignatureRequest.AuthenticationVersion), nameof(SignatureRequest.CompletedAtUtc),
                nameof(SignatureRequest.TerminalReason), nameof(SignatureRequest.AuthorizationRevokedAtUtc), nameof(SignatureRequest.AuthorizationRevocationReason),
                nameof(SignatureRequest.ExternalAccessRevokedAtUtc), nameof(SignatureRequest.ExternalAccessRevocationReason));
            Advance(e);
            var prior = e.Property(x => x.State).OriginalValue;
            var current = e.Entity;
            if (SignatureRules.IsTerminal(prior))
            {
                if (prior == "Signed")
                    Only(e, nameof(SignatureRequest.Revision), nameof(SignatureRequest.AuthorizationRevokedAtUtc),
                        nameof(SignatureRequest.AuthorizationRevocationReason), nameof(SignatureRequest.AuthenticationVersion),
                        nameof(SignatureRequest.FailedPinAttempts), nameof(SignatureRequest.LockedAtUtc),
                        nameof(SignatureRequest.ExternalAccessRevokedAtUtc), nameof(SignatureRequest.ExternalAccessRevocationReason));
                else
                    Only(e, nameof(SignatureRequest.Revision), nameof(SignatureRequest.AuthenticationVersion));
                if (current.State != prior) throw new InvalidOperationException("A terminal signature request cannot reopen or change its outcome.");
            }
            else if (!SignatureRules.IsOpen(current.State) && !SignatureRules.IsTerminal(current.State))
                throw new InvalidOperationException("Unknown signature request state.");
            var attempts = e.Property(x => x.FailedPinAttempts);
            if (attempts.CurrentValue < attempts.OriginalValue || attempts.CurrentValue > SigningPinRules.MaximumAttempts || attempts.CurrentValue > attempts.OriginalValue + 1)
                throw new InvalidOperationException("PIN failures cannot be erased or skip an attempt.");
            Once(e.Property(x => x.LockedAtUtc));
            if ((current.FailedPinAttempts == SigningPinRules.MaximumAttempts) != (current.LockedAtUtc is not null))
                throw new InvalidOperationException("Five PIN failures require a durable lock.");
            var auth = e.Property(x => x.AuthenticationVersion);
            if (auth.CurrentValue < auth.OriginalValue || auth.CurrentValue > auth.OriginalValue + 1)
                throw new InvalidOperationException("Authentication versions may advance once, never decrease.");
            if (SignatureRules.IsTerminal(current.State) != (current.CompletedAtUtc is not null))
                throw new InvalidOperationException("Terminal decisions require their timestamp.");
            Once(e.Property(x => x.CompletedAtUtc));
            Once(e.Property(x => x.AuthorizationRevokedAtUtc));
            if ((current.AuthorizationRevokedAtUtc is null) != (current.AuthorizationRevocationReason is null) ||
                current.AuthorizationRevokedAtUtc is not null && current.State != "Signed")
                throw new InvalidOperationException("Authorization withdrawal must retain its signed decision and reason.");
            if (e.Property(x => x.AuthorizationRevokedAtUtc).OriginalValue is not null &&
                e.Property(x => x.AuthorizationRevocationReason).IsModified)
                throw new InvalidOperationException("An authorization-withdrawal reason is immutable.");
            Once(e.Property(x => x.ExternalAccessRevokedAtUtc));
            if ((current.ExternalAccessRevokedAtUtc is null) != (current.ExternalAccessRevocationReason is null) ||
                current.ExternalAccessRevokedAtUtc is not null && current.State != "Signed")
                throw new InvalidOperationException("External-access withdrawal must retain its signed decision and reason.");
            if (e.Property(x => x.ExternalAccessRevokedAtUtc).OriginalValue is not null &&
                e.Property(x => x.ExternalAccessRevocationReason).IsModified)
                throw new InvalidOperationException("An external-access withdrawal reason is immutable.");
            if (e.Property(x => x.ExternalAccessRevokedAtUtc).OriginalValue is null && current.ExternalAccessRevokedAtUtc is not null &&
                auth.CurrentValue != auth.OriginalValue + 1)
                throw new InvalidOperationException("External-access withdrawal must invalidate existing sessions.");
        }
        foreach (var e in tracker.Entries<SignatureSession>())
        {
            NoDeletion(e);
            if (e.State != EntityState.Modified) continue;
            Only(e, nameof(SignatureSession.Revision), nameof(SignatureSession.DocumentReleasedAtUtc), nameof(SignatureSession.AccessAcknowledgedAtUtc), nameof(SignatureSession.ExpiresAtUtc));
            Advance(e);
            var expiry = e.Property(x => x.ExpiresAtUtc);
            if (expiry.IsModified && (e.Entity.Purpose != "Signing" || expiry.CurrentValue <= expiry.OriginalValue ||
                expiry.CurrentValue > expiry.OriginalValue.AddMinutes(SignatureRules.SessionMinutes)))
                throw new InvalidOperationException("Only an explicit bounded extension may lengthen a signing session.");
            Once(e.Property(x => x.DocumentReleasedAtUtc));
            Once(e.Property(x => x.AccessAcknowledgedAtUtc));
            if (e.Entity.AccessAcknowledgedAtUtc is not null && e.Entity.DocumentReleasedAtUtc is null)
                throw new InvalidOperationException("Format-access acknowledgment requires an actual document release in this session.");
        }
        foreach (var e in tracker.Entries<SignatureOutbox>())
        {
            NoDeletion(e);
            if (e.State != EntityState.Modified) continue;
            Only(e, nameof(SignatureOutbox.Revision), nameof(SignatureOutbox.State), nameof(SignatureOutbox.Attempts),
                nameof(SignatureOutbox.NextAttemptAtUtc), nameof(SignatureOutbox.LeaseId), nameof(SignatureOutbox.LeaseUntilUtc),
                nameof(SignatureOutbox.CompletedAtUtc), nameof(SignatureOutbox.LastErrorCode),
                nameof(SignatureOutbox.ProviderOperationId), nameof(SignatureOutbox.ProviderStatus), nameof(SignatureOutbox.SubmittedAtUtc), nameof(SignatureOutbox.LastPolledAtUtc),
                nameof(SignatureOutbox.PayloadCiphertext), nameof(SignatureOutbox.PayloadNonce), nameof(SignatureOutbox.PayloadTag),
                nameof(SignatureOutbox.PayloadWrappedKey), nameof(SignatureOutbox.PayloadKeyId));
            Advance(e);
            if (e.Property(x => x.Attempts).CurrentValue < e.Property(x => x.Attempts).OriginalValue)
                throw new InvalidOperationException("Outbox attempt history cannot decrease.");
            Once(e.Property(x => x.CompletedAtUtc));
            Once(e.Property(x => x.SubmittedAtUtc));
            var operation = e.Property(x => x.ProviderOperationId);
            if (operation.OriginalValue is not null && operation.CurrentValue != operation.OriginalValue)
                throw new InvalidOperationException("An uncertain email delivery must retain its original provider operation identifier.");
            if (e.Property(x => x.CompletedAtUtc).OriginalValue is not null)
                throw new InvalidOperationException("Completed outbox work cannot be rewritten.");
            var payloadChanges = e.Properties.Where(p => p.IsModified && p.Metadata.Name.StartsWith("Payload", StringComparison.Ordinal));
            if (payloadChanges.Any() && e.Property(x => x.PayloadCiphertext).OriginalValue is not null)
                throw new InvalidOperationException("An outbox delivery payload cannot be replaced.");
        }
    }

    private static bool Changed(EntityEntry e) => e.State is EntityState.Modified or EntityState.Deleted;
    private static void NoDeletion(EntityEntry e)
    {
        if (e.State == EntityState.Deleted) throw new InvalidOperationException("Signature records are retained; deletion is unavailable.");
    }
    private static void Only(EntityEntry e, params string[] names)
    {
        if (e.Properties.Any(p => p.IsModified && !names.Contains(p.Metadata.Name, StringComparer.Ordinal)))
            throw new InvalidOperationException("Signing identity, document, secrets and frozen wording cannot change.");
    }
    private static void Advance(EntityEntry e)
    {
        var p = e.Property("Revision");
        var previous = (long)p.OriginalValue!;
        if (previous == long.MaxValue || (long)p.CurrentValue! != previous + 1)
            throw new InvalidOperationException("Every signature workflow change must advance its revision once.");
    }
    private static void Once(PropertyEntry p)
    {
        if (p.OriginalValue is not null && !Equals(p.CurrentValue, p.OriginalValue))
            throw new InvalidOperationException("A recorded signing timestamp cannot be removed or rewritten.");
    }
}
