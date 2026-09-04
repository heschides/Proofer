namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Last, First";
    public const string ReleaseDate = "September 4, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Client names read \"Last, First\" when sorted that way",
            [
                "The \"Sort client lists by last name\" Settings option now also changes how names are printed, not only the order they appear in. Turn it on and the Notes client picker reads \"Doe, John\" instead of \"John Doe\"; turn it off and it goes back.",
                "Confirmed against a real rendered screen, not just the setting's stored value — the client picker's displayed text was checked directly, both on and off."
            ]),
        new(
            "The dashboard's selected-tab color is lighter in the newest two themes",
            [
                "Blue-Gray Pearl and Cedar Grove's sub-navigation pill — Overview, Clients, Notes Log, and the rest — filled with a darker orange than the buttons that were lightened last release, so it read as muddy next to them. It now uses the same lighter fill and dark text as those buttons. Every other theme is unchanged."
            ]),
        new(
            "An Admin can now archive a consumer that can't be deleted",
            [
                "The Admin dashboard has a new Status control: any consumer can be set to Active, No Longer Served, Deceased, or Ghost, regardless of the 20-day delete window or the test-data marker. Archiving is non-destructive and reversible — nothing is removed.",
                "This is the answer for a consumer neither delete tool reaches — most commonly one that predates last release's creation-date tracking, and can therefore never qualify for the 20-day window no matter how recently it was actually imported.",
                "The People list now shows each consumer's status and creation date, including \"Predates change tracking\" for anything backfilled rather than genuinely recent, so duplicates are distinguishable at a glance instead of by revision number — which counts edits, not copies.",
                "The message explaining why a delete tool doesn't apply now says which of the two different reasons it is, and points at the Status control as the alternative, instead of one message that read the same way for both."
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
