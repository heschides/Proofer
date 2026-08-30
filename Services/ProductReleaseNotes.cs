namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Database change handling and schema drift detection";
    public const string ReleaseDate = "August 30, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Nothing changes in daily use",
            [
                "This release is platform work. Caseloads, notes, scheduling, compliance, billing, and the provider directory behave exactly as they did in 1.2.31.",
                "One defect was fixed that had not yet caused a visible failure: the server treated a consumer's first name, last name, and a claim line's units as optional while the database required them, which could have refused a save at the wrong moment."
            ]),
        new(
            "Sati can now report how its own database differs from what it expects",
            [
                "An administrator can ask the server which tables and columns it expects but does not find, rather than discovering the gap as a failed screen.",
                "The check reports in both directions, so objects the database carries that no current version knows about are visible too.",
                "It reads only table and column names. It is a detector and changes nothing on its own."
            ]),
        new(
            "Database updates no longer need a hole opened in the firewall",
            [
                "Applying a schema change to the hosted Demo database used to mean temporarily granting a laptop direct access to it. That step is gone; the work now runs inside the hosted service, which already has the access it needs.",
                "Each run reports what it would do before it is allowed to do anything, refuses when the database does not match what it expects, and records what it changed.",
                "The Demo database's migration history had drifted from the record of changes applied to it, which is why every schema update had required its own hand-written script. That history has been reconciled."
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
