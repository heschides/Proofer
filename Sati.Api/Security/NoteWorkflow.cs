namespace Sati.Api.Security;

internal static class NoteWorkflow
{
    // Status ordinals mirror Models.NoteStatus without taking a dependency on the
    // desktop persistence model. Approval, return, and abandonment are owned by
    // dedicated server workflows and may never be asserted by a case-manager DTO.
    public static bool IsCaseManagerWritableStatus(int? status) => status is
        0 or // Scheduled
        1 or // Pending
        2 or // Logged (submission)
        3 or // HeldForCompliance
        4 or // Cancelled
        5 or // Delayed
        9;   // ComplianceBlocked

    // Logged notes have entered supervisory review and approved notes are final.
    // Returned notes become editable again so the author can correct and resubmit.
    public static bool CanCaseManagerEdit(int? currentStatus) => currentStatus is not (2 or 6);

    // Only unsubmitted scheduling/draft records may be physically removed.
    public static bool CanCaseManagerDelete(int? currentStatus) => currentStatus is 0 or 1 or 4 or 5;
}
