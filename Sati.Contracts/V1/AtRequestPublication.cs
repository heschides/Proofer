namespace Sati.Contracts.V1;

/// <summary>
/// One line of an AT request, reduced to the fields publication cares about.
/// Deliberately not the entity or the DTO: this rule is referenced by the
/// desktop client and the API, and neither should have to hand the other its
/// own shape to ask a question about completeness.
/// </summary>
public readonly record struct AtRequestLine(string? Name, decimal ItemCost, int Quantity);

/// <summary>
/// The single owner of what it means to PUBLISH an AT request: which requests
/// are complete enough to publish, what the case manager is attesting to, and
/// whether a published request may still be edited.
///
/// This lives in Sati.Contracts because both the desktop client and Sati.Api
/// enforce it. A published request that the desktop refuses to edit but the API
/// happily accepts a PUT for is not locked; it only looks locked.
///
/// SCOPE — read this before extending it. Publication records WHO published the
/// request and WHEN, and freezes the statement they were shown. That is an
/// attestation, not an electronic signature: no credential is re-challenged, no
/// signing certificate is involved, and no state or federal e-signature standard
/// is claimed. REGULATORY_CONCERNS.md holds the open question of whether OADS
/// requires one; if the answer is yes, the replacement belongs here.
/// </summary>
public static class AtRequestPublication
{
    /// <summary>
    /// The statement the case manager affirms. Frozen onto the request at
    /// publication rather than looked up at render: if this wording is ever
    /// revised, requests published under the old wording must keep showing the
    /// old wording. A signature that floats to whatever the current text happens
    /// to be attests to nothing in particular.
    /// </summary>
    public const string AttestationStatement =
        "I attest that the information in this request is accurate and complete to the best of my " +
        "knowledge, and that the items or services requested support the assessed needs of the client " +
        "named above.";

    /// <summary>
    /// Printed on the generated PDF beneath the attestation block. States plainly
    /// what the attestation is and is not, so a reader downstream does not read
    /// more into it than Sati actually captured.
    /// </summary>
    public const string AttestationScopeNotice =
        "This attestation records the identity of the Sati user who published this request and the " +
        "date and time of publication. It is not an electronic signature under any state or federal " +
        "electronic-signature standard, and does not by itself satisfy a signature requirement that " +
        "specifies one.";

    /// <summary>
    /// True once the request has been published. Both halves are required because
    /// a name without a timestamp (or the reverse) is a half-written attestation,
    /// which should be treated as no attestation rather than as a valid one.
    /// </summary>
    public static bool IsPublished(string? signedByName, DateTime? signedAtUtc) =>
        !string.IsNullOrWhiteSpace(signedByName) && signedAtUtc.HasValue;

    /// <summary>
    /// A published request is closed to edits. Correcting one means reopening it,
    /// which discards the attestation — see the note on the reopen path. This
    /// exists as a named rule rather than as scattered <c>!IsPublished</c> checks
    /// so the reason is greppable.
    /// </summary>
    public static bool AllowsEdit(string? signedByName, DateTime? signedAtUtc) =>
        !IsPublished(signedByName, signedAtUtc);

    /// <summary>
    /// Everything standing between this request and publication, phrased for the
    /// case manager reading it. Empty means publishable.
    ///
    /// These are completeness checks, not authorization checks. Whether this user
    /// may publish this client's request is a tenancy question, answered by
    /// TenantAccess on the server before this is ever consulted.
    /// </summary>
    public static IReadOnlyList<string> FindBlockers(
        string? vendorName,
        string? vendorBillingLocation,
        IEnumerable<AtRequestLine> lines,
        bool alreadyPublished)
    {
        var blockers = new List<string>();

        if (alreadyPublished)
        {
            // Returned as a blocker rather than thrown: the caller is usually a
            // disabled button asking "why", not an error path.
            blockers.Add("This request has already been published.");
            return blockers;
        }

        if (string.IsNullOrWhiteSpace(vendorName))
            blockers.Add("A vendor or provider name is required.");

        if (string.IsNullOrWhiteSpace(vendorBillingLocation))
            blockers.Add("The vendor's billing location (EIS Location) is required.");

        var rows = lines as IReadOnlyList<AtRequestLine> ?? lines.ToList();

        if (rows.Count == 0)
        {
            blockers.Add("At least one item or service is required.");
            return blockers;
        }

        // Reported once each rather than per row: a request with six unnamed rows
        // produces one actionable sentence, not six identical ones.
        if (rows.Any(line => string.IsNullOrWhiteSpace(line.Name)))
            blockers.Add("Every item needs a name.");

        if (rows.Any(line => line.Quantity < 1))
            blockers.Add("Every item needs a quantity of at least one.");

        if (rows.Any(line => line.ItemCost < 0))
            blockers.Add("An item cost cannot be negative.");

        // A zero-dollar request is almost certainly an unfinished one. Individual
        // zero-cost lines are allowed — a bundled or donated item alongside priced
        // ones is legitimate — but the request as a whole must ask for something.
        if (rows.Sum(line => line.ItemCost * line.Quantity) <= 0)
            blockers.Add("The request total must be greater than zero.");

        return blockers;
    }
}
