namespace Sati.Contracts.V1;

/// <summary>
/// Authoritative owner of case-note workflow boundaries. The persisted ordinal
/// values mirror <c>Sati.Models.NoteStatus</c> without coupling contracts to the
/// desktop persistence model.
/// </summary>
public static class NoteWorkflow
{
    public static bool IsCaseManagerWritableStatus(int? status) => status is
        0 or // Scheduled
        1 or // Pending
        2 or // Logged (submission)
        3 or // HeldForCompliance
        4 or // Cancelled
        5 or // Delayed
        9;   // ComplianceBlocked

    public static bool CanCaseManagerEdit(int? currentStatus) => currentStatus is not (2 or 6);

    public static bool CanCaseManagerDelete(int? currentStatus) => currentStatus is 0 or 1 or 4 or 5;
}
