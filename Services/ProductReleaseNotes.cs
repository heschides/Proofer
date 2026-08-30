namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Sati repairs its own update record";
    public const string ReleaseDate = "August 30, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Sati starts again on machines where 1.2.32 refused",
            [
                "Version 1.2.32 would not start on some machines, stopping with a message about a column name being specified more than once. Sati was right to stop: an earlier update had reached the database without leaving a record that it ran, so it was about to repeat work that was already done.",
                "Sati now checks that before it changes anything. When every part of an update is already in place, it simply records that the update ran and carries on.",
                "Nothing about your records changes. The repair writes a single line to Sati's own list of applied updates."
            ]),
        new(
            "When it still stops, it says something you can act on",
            [
                "If only part of an update is present, Sati still refuses, because which part is missing decides what should happen and that needs a person.",
                "The message now names the update involved and states plainly that nothing was changed, instead of reporting a database error.",
                "A backup is still taken before any change, whenever the database holds records."
            ]),
        new(
            "Nothing else changes in daily use",
            [
                "Caseloads, notes, scheduling, compliance, billing, and the provider directory behave exactly as they did in 1.2.32.",
                "This release adds no new database changes of its own."
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
