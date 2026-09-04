namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "A button that explains itself";
    public const string ReleaseDate = "September 4, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "The rule-3 delete button was going quiet, not broken",
            [
                "The \"Delete consumer (created in error)\" button on the Admin dashboard used to disable itself with no explanation whenever the Reason field was empty. It now stays clickable and tells you what's missing instead of doing nothing.",
                "When neither delete tool applies to a selected consumer — not marked test data, and not created in the last 20 days — the Admin dashboard now says so, instead of leaving two grayed-out buttons with no explanation."
            ]),
        new(
            "Sort your client list by last name",
            [
                "A new Settings option, \"Sort client lists by last name,\" reorders the client picker in Notes by surname instead of first name. Off by default, so no one's current list order changes without asking.",
                "This is a personal setting saved to your Windows account, the same way Easy Eyes mode and the inactivity screen are."
            ]),
        new(
            "Still planned before commercial production",
            [
                "Sorting client lists by last name currently reorders only the Notes client picker. AT Requests, client documents, and similar screens keep their own list and are not yet affected.",
                "Dual-control release of a legal hold. Today's release is single-admin.",
                "An in-app way to mark a consumer No Longer Served or Deceased. The rule and the record exist; there is no screen to use it from yet.",
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
