namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Room to undo a duplicate";
    public const string ReleaseDate = "September 3, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "A duplicate consumer is no longer a dead end",
            [
                "Bulk Credible import now also checks MaineCareId and name-and-birth-date before deciding a consumer is new, not only the Credible client ID. That gap is why re-importing a consumer who predates Credible could quietly create a second record for the same person.",
                "The Address field is filled in on import again. It had only been reaching the claim-address field under \"Claim Street.\"",
                "An Admin can now delete a consumer created within the last 20 days, for exactly this kind of accident. Confirming requires typing the client's exact name and a stated reason, and the action refuses if a legal hold is on the record or if any of its billing already reached a payer."
            ]),
        new(
            "Safety plans live in Sati now",
            [
                "A new Safety Plan tab on the client screen replaces work that lived outside Sati. Save a draft, submit it for review, and have your supervisor approve or return it.",
                "An approved or returned version stays exactly as it was. Start a new revision to make further changes."
            ]),
        new(
            "Annual documents get a place of their own",
            [
                "A new Annual Documents tab tracks the yearly privacy notice, packet, and medical-records paperwork on a configurable 30-day window, with a reminder on the client's overview as the cycle opens.",
                "Generate the privacy notice, then record the date a client received it or a good-faith effort to provide it. \"Save Annual Documents Locally\" builds a ZIP of available drafts and a manifest — a release already completed and signed still has to come from its own saved copy; this does not replace that.",
                "A medical records request is offered only once the medical release is attested and the client has a current primary-care provider on file. It is a download for staff to send; Sati does not send it for you."
            ]),
        new(
            "Still planned before commercial production",
            [
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
