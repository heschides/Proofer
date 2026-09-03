namespace Sati.Models;

/// <summary>
/// One legal hold blocking rule-3 consumer deletion for a specific person.
///
/// <para>
/// Deliberately narrower than OPERATIONS.md's full record-class/scope hold model — this exists
/// only to gate <c>ConsumerDeletionRules</c>'s deletion-window command, not as a general-purpose
/// purge-job registry. Release is single-admin for v1, a documented shortfall against
/// OPERATIONS.md's dual-control requirement — see DECISIONS.md and AGENDA.md.
/// </para>
/// </summary>
public sealed class LegalHold
{
    public int Id { get; set; }
    public int AgencyId { get; set; }
    public int PersonId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? CaseReference { get; set; }
    public string? IssuedBy { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public int PlacedByUserId { get; set; }
    public DateTime PlacedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsReleased { get; set; }
    public int? ReleasedByUserId { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public string? ReleaseNote { get; set; }
}
