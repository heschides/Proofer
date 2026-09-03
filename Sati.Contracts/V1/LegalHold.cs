namespace Sati.Contracts.V1;

/// <summary>
/// Whether a person is clear to delete under rule-3, per HANDOFF_CLIENT_DELETION_POLICY.md's A3.
///
/// <para>
/// Deliberately not a <c>bool</c>. "Not checked" and "no hold found" are different facts, and
/// collapsing them into one false-means-clear boolean is how a destructive command becomes
/// fail-open. <see cref="Unavailable"/> exists so a query failure, timeout, or unconfigured
/// registry reads as "cannot confirm," not as "confirmed clear."
/// </para>
/// </summary>
public enum LegalHoldStatus
{
    Clear = 0,
    Active = 1,
    Unavailable = 2
}

/// <summary>
/// Answers whether rule-3 deletion may proceed for one person.
///
/// <para>
/// Implementations must never return <see cref="LegalHoldStatus.Clear"/> except after
/// successfully confirming no active hold exists for that person. Any exception, timeout, or
/// unconfigured state must be caught and translated to <see cref="LegalHoldStatus.Unavailable"/>
/// inside the implementation — never allowed to propagate as if it meant <c>Clear</c>.
/// </para>
/// </summary>
public interface ILegalHoldRegistry
{
    Task<LegalHoldStatus> GetStatusAsync(
        int agencyId, int personId, CancellationToken cancellationToken = default);
}

/// <summary>Places a new legal hold on one person, blocking rule-3 deletion until released.</summary>
public sealed record PlaceLegalHoldRequest(
    int PersonId,
    string Reason,
    string? CaseReference,
    string? IssuedBy,
    DateTime EffectiveAtUtc);

/// <summary>Releases an existing legal hold. Single-admin for v1 — see <c>LegalHold</c>.</summary>
public sealed record ReleaseLegalHoldRequest(string? ReleaseNote);

public sealed record LegalHoldDto(
    int Id,
    int PersonId,
    string Reason,
    string? CaseReference,
    string? IssuedBy,
    DateTime EffectiveAtUtc,
    int PlacedByUserId,
    DateTime PlacedAtUtc,
    bool IsReleased,
    int? ReleasedByUserId,
    DateTime? ReleasedAtUtc,
    string? ReleaseNote);
