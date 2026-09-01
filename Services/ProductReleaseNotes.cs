namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "One record, one answer";
    public const string ReleaseDate = "September 1, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "A completed form no longer blocks billing",
            [
                "A client could show a completed 90-day review on every screen while the billing alert kept naming that same review. Both displays were reading the record correctly; the client simply had three copies of it, and only one had been completed.",
                "The duplicates were created by an old startup fault that has not occurred since July, but the records it left behind were never cleaned up. Sati now merges them on first launch and keeps an audit entry for every row it removes.",
                "The database now refuses to store a second copy of the same form for the same client and due date, so this cannot recur."
            ]),
        new(
            "A completion date is what makes a form complete",
            [
                "A form used to carry both a completion date and a separate complete/not-complete marker, and the two could disagree. When they did, the form read as finished on screen while billing still treated it as outstanding.",
                "There is now one fact instead of two. A form is complete when it has a completion date, and that state cannot be recorded any other way.",
                "Records that claimed completion without a date are given the date their own annual cycle began, which is what the cycle starting already meant. Quarterly reviews are never given an assumed date, because a review is an attestation that work happened."
            ]),
        new(
            "Screens and billing agree about timing",
            [
                "A completion date entered for a day that has not arrived yet is recorded, but the document is not yet in force. Billing has always treated it that way.",
                "The caseload grid, upcoming items, and task rows now ask the same question billing asks, so a form cannot appear finished on one screen while blocking on another."
            ]),
        new(
            "Compliance records keep pace with the caseload",
            [
                "Sati generates each client's compliance forms for every annual cycle from their effective date onward. A client added with a past effective date previously had no records at all for the years in between, and a form that does not exist cannot be flagged.",
                "Only the cycle currently under way is treated as already satisfied. A closed year is left open rather than assumed complete, because Sati has no record of whether those documents were renewed.",
                "Expect open historical documents on any client entered with a backdated effective date. Review them individually; do not bulk-close them or supply guessed dates."
            ]),
        new(
            "Still planned before commercial production",
            [
                "An in-app way to record an exact completion date for annual documents, as the 90-Day Reviews workspace already allows for quarterly attestations.",
                "A structured daily-task record, if users need durable completion, assignment, linking, or reporting beyond the editable scratchpad.",
                "A real legal-hold registry and governed archive lifecycle for ordinary clients.",
                "Office Ally transport and authenticated 999, TA1, and 277CA ingestion tied to immutable ClaimLine snapshots.",
                "835 import from the payer/EFT source of truth, raw-segment drill-through, corrected claim frequency 7/8 workflow, and durable audit posting.",
                "Denial return to the originating note, fee-schedule underpayment detection, authorization/unit alerts, forecasting, and cross-agency benchmarking.",
                "Automated retention and legal-hold enforcement; production identity and MFA; external monitoring; payer enrollment and certification."
            ])
    ];
}
