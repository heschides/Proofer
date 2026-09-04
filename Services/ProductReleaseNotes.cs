namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Trading Places";
    public const string ReleaseDate = "September 4, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "The Scratchpad can trade places with the notes list",
            [
                "New Settings option, \"Display Scratchpad in the center of the display\": Today's Work and Tomorrow's Agenda move into the Overview's middle column, and the notes list moves out to the collapsible side panel behind the chevron. Off by default, so an existing user's layout does not change.",
                "The swap applies to the Overview only. On the other Case Management tabs there is no notes list to move, so the side panel keeps showing Today's Work rather than emptying out.",
                "The centered Scratchpad is the same Today's Work it always was, not a second copy — what you type there is what gets saved when Sati closes."
            ]),
        new(
            "Still planned before commercial production",
            [
                "Sorting client lists by last name currently affects only the Notes client picker. AT Requests, client documents, and similar screens keep their own list and are not yet affected.",
                "Dual-control release of a legal hold. Today's release is single-admin.",
                "A way for a case manager to mark their own consumer No Longer Served or Deceased. An Admin can do this for any consumer from the Admin dashboard's new Status control; a case manager still has no path to it from their own caseload.",
                "A count of exactly what a consumer deletion will remove, shown before you confirm rather than only after.",
                "address2 on Credible import — no example export has confirmed Credible's own label for it yet.",
                "Independent legal and program review of the privacy-notice and safety-plan wording before real clinical use.",
                "A structured daily-task record, if users need durable completion, assignment, linking, or reporting beyond the editable scratchpad.",
                "Office Ally transport and authenticated 999, TA1, and 277CA ingestion tied to immutable ClaimLine snapshots.",
                "835 import from the payer/EFT source of truth, raw-segment drill-through, corrected claim frequency 7/8 workflow, and durable audit posting.",
                "Automated retention and legal-hold enforcement; production identity and MFA; external monitoring; payer enrollment and certification."
            ])
    ];
}
