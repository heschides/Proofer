namespace Sati.Contracts.V1;

/// <summary>
/// Who may move a consumer between <c>Active</c>, <c>NoLongerServed</c>, <c>Deceased</c>, and
/// <c>Ghost</c>. See HANDOFF_CLIENT_DELETION_POLICY.md's archive semantics.
///
/// <para>
/// Archival is non-destructive and routine, so a case manager may set <c>NoLongerServed</c> or
/// <c>Deceased</c> on a consumer in their own caseload. Only an Admin may set <c>Ghost</c>,
/// because that status asserts the record is not a real person — the same claim the rule-3
/// deletion attestation makes, and it must not be reachable at a lower privilege than deletion
/// itself. Status names are strings, not the desktop-only <c>PersonStatus</c> enum, because this
/// rule is shared with the server, which stores status as a plain int and must not take a
/// dependency on the desktop model.
/// </para>
/// </summary>
public static class PersonStatusRules
{
    public const string Active = "Active";
    public const string NoLongerServed = "NoLongerServed";
    public const string Deceased = "Deceased";
    public const string Ghost = "Ghost";

    public static readonly string[] AllStatuses = [Active, NoLongerServed, Deceased, Ghost];

    public const string UnknownStatusMessage = "That is not a recognized consumer status.";
    public const string OnlyAdminMayGhostMessage =
        "Only an Admin may mark a consumer as Ghost — that status asserts the record is not a real person.";
    public const string OnlyOwnCaseloadMessage =
        "You may only change the status of a consumer on your own caseload.";

    /// <summary>
    /// The reason an actor may not make this status change, or null when it is permitted.
    /// Pure — the caller has already loaded <paramref name="actorIsAdmin"/> and
    /// <paramref name="actorOwnsPerson"/> from the database rather than trusting a claim.
    /// </summary>
    public static string? Describe(bool actorIsAdmin, bool actorOwnsPerson, string targetStatus)
    {
        if (!AllStatuses.Contains(targetStatus, System.StringComparer.Ordinal))
            return UnknownStatusMessage;
        if (string.Equals(targetStatus, Ghost, System.StringComparison.Ordinal) && !actorIsAdmin)
            return OnlyAdminMayGhostMessage;
        if (!actorIsAdmin && !actorOwnsPerson)
            return OnlyOwnCaseloadMessage;
        return null;
    }

    public static bool CanSetStatus(bool actorIsAdmin, bool actorOwnsPerson, string targetStatus) =>
        Describe(actorIsAdmin, actorOwnsPerson, targetStatus) is null;
}
