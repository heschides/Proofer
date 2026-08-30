namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Billing submission home and remittance reconciliation";
    public const string ReleaseDate = "August 30, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "A permanent home for outstanding submissions",
            [
                "Billing staff can filter the inclusive billing-month range used to generate separate 837P files.",
                "The submission home groups event history into one batch row with claim count, charge value, clearinghouse-send time, latest status, and an outstanding filter.",
                "Generated, transmitted, transport-failed, 999, and 277CA-style states remain visible as an append-only timeline source."
            ]),
        new(
            "Remittance outcomes that explain themselves",
            [
                "A denial and unpaid worklist is filterable by claim reference, payer, date, status, and 30/60/90/120+ aging buckets.",
                "Common CARC group codes are shown in plain language and grouped by provider responsibility (CO), patient responsibility (PR), and other adjustment (OA).",
                "Deposit reconciliation makes the 835 amount, provider-level (PLB) adjustments, EFT amount, difference, and match state visible together. A batch is not considered tied out until the amounts match to the penny."
            ]),
        new(
            "Synthetic data stays visibly synthetic",
            [
                "Demo seed rows cover accepted, rejected, partial, denied, reversed, unmatched, needs-review, EFT mismatch, pending EFT, and PLB scenarios.",
                "The synthetic exchange test consumes a test 837P and produces representative 999/277CA/835 responses without contacting a clearinghouse or payer.",
                "This release does not claim live Office Ally transport, production acknowledgments, payer certification, or real remittance ingestion."
            ]),
        new(
            "Still planned before commercial production",
            [
                "Office Ally transport and authenticated 999, TA1, and 277CA ingestion tied to immutable ClaimLine snapshots.",
                "835 import from the payer/EFT source of truth, raw-segment drill-through, corrected claim frequency 7/8 workflow, and durable audit posting.",
                "Denial return to the originating note, fee-schedule underpayment detection, authorization/unit alerts, forecasting, and cross-agency benchmarking.",
                "Automated retention and legal-hold enforcement; production identity and MFA; external monitoring; payer enrollment and certification."
            ])
    ];
}
