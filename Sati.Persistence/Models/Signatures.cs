namespace Sati.Models;

/// <summary>The exact retained original. A derived signature package never replaces it.</summary>
public sealed class FrozenSignatureDocument
{
    public int Id { get; set; }
    public int AgencyId { get; set; }
    public int PersonId { get; set; }
    public int DocumentArtifactId { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public long ByteCount { get; set; }
    public string BlobPath { get; set; } = string.Empty;
    public DateTime StoredAtUtc { get; set; }
    public int StoredByUserId { get; set; }
}

/// <summary>One immutable invitation and signer identity; only its guarded workflow can advance.</summary>
public sealed class SignatureRequest
{
    public int Id { get; set; }
    public int AgencyId { get; set; }
    public int PersonId { get; set; }
    public int FrozenDocumentId { get; set; }
    public Guid ClientRequestId { get; set; }
    public string SignerCapacity { get; set; } = string.Empty;
    public int? SignerContactId { get; set; }
    public string SignerName { get; set; } = string.Empty;
    public string DeliveryEmail { get; set; } = string.Empty;
    public string? AuthorityEvidence { get; set; }
    public string TokenSha256 { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public string PinSalt { get; set; } = string.Empty;
    public int PinIterations { get; set; }
    public byte[] PinPepperWrapped { get; set; } = [];
    public string PinKeyId { get; set; } = string.Empty;
    public int FailedPinAttempts { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public int AuthenticationVersion { get; set; } = 1;
    public string State { get; set; } = "Issued";
    public long Revision { get; set; } = 1;
    public string DisclosureVersion { get; set; } = string.Empty;
    public string DisclosureText { get; set; } = string.Empty;
    public string IntentText { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public int IssuedByUserId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? TerminalReason { get; set; }
    public int? ReplacesRequestId { get; set; }
    public DateTime? AuthorizationRevokedAtUtc { get; set; }
    public string? AuthorizationRevocationReason { get; set; }
    public DateTime? ExternalAccessRevokedAtUtc { get; set; }
    public string? ExternalAccessRevocationReason { get; set; }
}

/// <summary>Only a token hash is persisted. Every action rechecks this request-bound lease.</summary>
public sealed class SignatureSession
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int RequestId { get; set; }
    public string Purpose { get; set; } = "Signing";
    public string TokenSha256 { get; set; } = string.Empty;
    public int AuthenticationVersion { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? DocumentReleasedAtUtc { get; set; }
    public DateTime? AccessAcknowledgedAtUtc { get; set; }
    public long Revision { get; set; } = 1;
}

/// <summary>Signer acceptance captured after PIN authentication, never staff-assumed consent.</summary>
public sealed class SignatureConsent
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int RequestId { get; set; }
    public long SessionId { get; set; }
    public string DisclosureVersion { get; set; } = string.Empty;
    public string DisclosureText { get; set; } = string.Empty;
    public DateTime AcceptedAtUtc { get; set; }
}

public sealed class SignatureEvent
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int RequestId { get; set; }
    public long Sequence { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ActorKind { get; set; } = string.Empty;
    public int? ActorUserId { get; set; }
    public long? SessionId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string DetailJson { get; set; } = "{}";
}

/// <summary>Immutable signing decision. PDF generation may finish later without changing it.</summary>
public sealed class SignatureCompletion
{
    public int Id { get; set; }
    public int AgencyId { get; set; }
    public int RequestId { get; set; }
    public int FrozenDocumentId { get; set; }
    public long SessionId { get; set; }
    public long ConsentId { get; set; }
    public string TypedSignerName { get; set; } = string.Empty;
    public string IntentText { get; set; } = string.Empty;
    public DateTime SignedAtUtc { get; set; }
}

public sealed class SignaturePackage
{
    public int Id { get; set; }
    public int AgencyId { get; set; }
    public int RequestId { get; set; }
    public int CompletionId { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public long ByteCount { get; set; }
    public string BlobPath { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Worker-only recovery queue. Invitation delivery secrets are encrypted, never plaintext.</summary>
public sealed class SignatureOutbox
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int RequestId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public int Generation { get; set; } = 1;
    public byte[]? PayloadCiphertext { get; set; }
    public byte[]? PayloadNonce { get; set; }
    public byte[]? PayloadTag { get; set; }
    public byte[]? PayloadWrappedKey { get; set; }
    public string? PayloadKeyId { get; set; }
    public string State { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public Guid? ProviderOperationId { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? LastPolledAtUtc { get; set; }
    public long Revision { get; set; } = 1;
}

/// <summary>Read-only projection; the portal does not map or query the clinical artifact entity.</summary>
public sealed class SignatureSourceDocument
{
    public int Id { get; set; }
    public int AgencyId { get; set; }
    public int PersonId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public DateTime CycleStart { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string? ContentSha256 { get; set; }
    public long? ByteCount { get; set; }
    public string BlankFieldsJson { get; set; } = "[]";
    public int? SupersededByArtifactId { get; set; }
}

/// <summary>Only environment metadata, not the underlying identity table or any person record.</summary>
public sealed class SignatureDatabaseEnvironment
{
    public string DatabaseName { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
}
