using Sati.Contracts.V1;

namespace Sati.Models;

public enum FormAttestationKind
{
    Attested,
    Revoked
}

public sealed class FormAttestation
{
    public long Id { get; private set; }
    public int FormId { get; private set; }
    public Form Form { get; private set; } = null!;
    public FormAttestationKind Kind { get; private set; }
    public DateTime? CompletedOn { get; private set; }
    public AttestationActorKind ActorKind { get; private set; }
    public int? ActorUserId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public int? EvidenceNoteId { get; private set; }
    public string? PrerequisiteStateJson { get; private set; }
    public string? Reason { get; private set; }

    private FormAttestation() { }

    public static FormAttestation Attested(
        DateTime completedOn,
        AttestationActorKind actorKind,
        int? actorUserId,
        DateTime recordedAtUtc,
        int? evidenceNoteId = null,
        string? prerequisiteStateJson = null,
        string? reason = null)
    {
        EnsureActor(actorKind, actorUserId, reason);
        return new FormAttestation
        {
            Kind = FormAttestationKind.Attested,
            CompletedOn = completedOn.Date,
            ActorKind = actorKind,
            ActorUserId = actorUserId,
            RecordedAtUtc = DateTime.SpecifyKind(recordedAtUtc, DateTimeKind.Utc),
            EvidenceNoteId = evidenceNoteId,
            PrerequisiteStateJson = prerequisiteStateJson,
            Reason = Normalize(reason)
        };
    }

    public static FormAttestation Revoked(
        AttestationActorKind actorKind,
        int? actorUserId,
        DateTime recordedAtUtc,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required to revoke an attestation.", nameof(reason));
        EnsureActor(actorKind, actorUserId, reason);
        return new FormAttestation
        {
            Kind = FormAttestationKind.Revoked,
            ActorKind = actorKind,
            ActorUserId = actorUserId,
            RecordedAtUtc = DateTime.SpecifyKind(recordedAtUtc, DateTimeKind.Utc),
            Reason = reason.Trim()
        };
    }

    private static void EnsureActor(
        AttestationActorKind actorKind,
        int? actorUserId,
        string? reason)
    {
        if (actorKind == AttestationActorKind.System)
        {
            if (actorUserId is not null || string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A system attestation requires a reason and no user actor.");
            return;
        }

        if (actorUserId is null)
            throw new ArgumentException("A human attestation requires an actor user id.");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
