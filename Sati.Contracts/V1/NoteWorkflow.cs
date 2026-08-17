namespace Sati.Contracts.V1;

/// <summary>
/// Authoritative owner of case-note workflow boundaries. The persisted ordinal
/// values mirror <c>Sati.Models.NoteStatus</c> without coupling contracts to the
/// desktop persistence model.
/// </summary>
/// <remarks>
/// <para>
/// Membership in a writable set is not the same question as whether a particular
/// move is legal. Both the desktop client and the API previously asked only
/// "is the target status one a case manager may write?", which let a note jump
/// between unrelated states — an aged-out note straight back into the review
/// queue, for one — without passing through documentation again. The transition
/// table below is the single answer to that question for both paths.
/// </para>
/// <para>
/// The invariants that matter for money and for the clinical record are:
/// no case-manager move reaches <see cref="Approved"/>, <see cref="Returned"/>,
/// or <see cref="Abandoned"/>; nothing at all leaves <see cref="Approved"/>;
/// and the only way into <see cref="Approved"/> is a supervisor acting on a
/// <see cref="Logged"/> note.
/// </para>
/// </remarks>
public static class NoteWorkflow
{
    public const int Scheduled = 0;
    public const int Pending = 1;
    public const int Logged = 2;
    public const int HeldForCompliance = 3;
    public const int Cancelled = 4;
    public const int Delayed = 5;
    public const int Approved = 6;
    public const int Returned = 7;
    public const int Abandoned = 8;
    public const int ComplianceBlocked = 9;

    /// <summary>Statuses a case manager may author directly.</summary>
    private static readonly int[] CaseManagerAuthored =
        [Scheduled, Pending, Logged, HeldForCompliance, Cancelled, Delayed, ComplianceBlocked];

    /// <summary>
    /// Legal case-manager moves, keyed by the note's current status. A status
    /// absent from this table is under a server workflow and cannot be moved by
    /// its author at all.
    /// </summary>
    /// <remarks>
    /// Three groups. Work in progress moves freely, because scheduling, drafting,
    /// holding for compliance, and correcting a returned note are all the same
    /// kind of unfinished state and the note-entry screen offers them together.
    /// Closed work reopens as a draft first, so a cancelled or aged-out narrative
    /// cannot land back in front of a supervisor without an intervening edit.
    /// Submitted and approved work is not the author's to move.
    /// <para>
    /// A returned note may be re-dispositioned into any status its author is
    /// allowed to assign, but it cannot be saved as Returned, because Returned is
    /// a supervisor's word about the note and not the author's to write.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<int, int[]> CaseManagerTransitions = new()
    {
        // Work in progress.
        [Scheduled] = CaseManagerAuthored,
        [Pending] = CaseManagerAuthored,
        [Delayed] = CaseManagerAuthored,
        [HeldForCompliance] = CaseManagerAuthored,
        [ComplianceBlocked] = CaseManagerAuthored,
        [Returned] = CaseManagerAuthored,

        // Closed work reopens as a draft.
        [Cancelled] = [Pending, Cancelled],
        [Abandoned] = [Pending]
    };

    /// <summary>Legal supervisor moves. Review acts only on a submitted note.</summary>
    private static readonly Dictionary<int, int[]> SupervisorTransitions = new()
    {
        [Logged] = [Approved, Returned]
    };

    /// <summary>
    /// Statuses a case manager may assign when creating a note, or carry on an
    /// update. Approval, return, and abandonment are owned by dedicated
    /// workflows and may never be asserted by a case-manager DTO.
    /// </summary>
    public static bool IsCaseManagerWritableStatus(int? status) =>
        status is int value && Array.IndexOf(CaseManagerAuthored, value) >= 0;

    /// <summary>
    /// Logged notes have entered supervisory review and approved notes are final.
    /// Returned notes become editable again so the author can correct and resubmit.
    /// </summary>
    public static bool CanCaseManagerEdit(int? currentStatus) => currentStatus is not (Logged or Approved);

    /// <summary>Only unsubmitted scheduling/draft records may be physically removed.</summary>
    public static bool CanCaseManagerDelete(int? currentStatus) =>
        currentStatus is Scheduled or Pending or Cancelled or Delayed;

    /// <summary>
    /// Whether a case manager may move a note from <paramref name="currentStatus"/>
    /// to <paramref name="targetStatus"/>. A note whose stored status predates the
    /// status column is treated as a draft so it can still be corrected.
    /// </summary>
    public static bool CanCaseManagerTransition(int? currentStatus, int? targetStatus)
    {
        if (targetStatus is not int target || !IsCaseManagerWritableStatus(target))
            return false;
        if (currentStatus is not int current)
            return Array.IndexOf(CaseManagerAuthored, target) >= 0;
        return CaseManagerTransitions.TryGetValue(current, out var allowed) &&
            Array.IndexOf(allowed, target) >= 0;
    }

    /// <summary>
    /// Whether a supervisor may move a note from <paramref name="currentStatus"/>
    /// to <paramref name="targetStatus"/>. Approval and return are the only
    /// supervisory transitions, and both require a submitted note.
    /// </summary>
    public static bool CanSupervisorTransition(int? currentStatus, int? targetStatus)
    {
        if (currentStatus is not int current || targetStatus is not int target)
            return false;
        return SupervisorTransitions.TryGetValue(current, out var allowed) &&
            Array.IndexOf(allowed, target) >= 0;
    }

    /// <summary>
    /// Whether the system's overdue sweep may abandon a note. Only an unfinished
    /// draft ages out; scheduled, submitted, and reviewed work never does.
    /// </summary>
    public static bool CanSystemAbandon(int? currentStatus) => currentStatus is Pending;

    public static string StatusName(int? status) => status switch
    {
        Scheduled => "Scheduled",
        Pending => "Pending",
        Logged => "Logged",
        HeldForCompliance => "Held for compliance",
        Cancelled => "Cancelled",
        Delayed => "Delayed",
        Approved => "Approved",
        Returned => "Returned",
        Abandoned => "Abandoned",
        ComplianceBlocked => "Compliance blocked",
        _ => "Unknown"
    };

    /// <summary>
    /// A caller-facing explanation of a rejected case-manager transition, so the
    /// desktop and the API describe the same refusal the same way.
    /// </summary>
    public static string DescribeRejectedTransition(int? currentStatus, int? targetStatus)
    {
        if (targetStatus is not int target || !IsCaseManagerWritableStatus(target))
            return "That note status is controlled by a supervisor workflow.";
        return $"A {StatusName(currentStatus)} note cannot be changed to " +
            $"{StatusName(targetStatus)}. " + NextStepFor(currentStatus);
    }

    private static string NextStepFor(int? currentStatus) => currentStatus switch
    {
        Cancelled => "Move it back to Pending first if the service needs to be documented again.",
        Abandoned => "Move it back to Pending first to document it again.",
        Logged => "A supervisor must return it before it can be changed.",
        Approved => "An approved note is part of the official record and cannot be changed.",
        _ => "Move it to Pending first."
    };
}
