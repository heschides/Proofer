namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Sati starts again";
    public const string ReleaseDate = "September 1, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Fixes a 1.2.36 install that would not open",
            [
                "Sati 1.2.36 stopped at startup on computers using a local database, reporting that part of its update was already present. Nothing was written and no records were changed; the check that stops a risky update was reading one step incorrectly.",
                "That check now recognises the step properly, and the update proceeds normally: back up, tidy duplicate compliance records, then update.",
                "If a computer is still on 1.2.36 and will not open, installing 1.2.37 resolves it. Nothing needs to be undone first."
            ]),
        new(
            "Everything from 1.2.36 arrives with this release",
            [
                "A completed 90-day review that kept appearing in the billing alert, because the client held three copies of the same record and only one was completed. Duplicates are merged on first launch, with an audit entry for every row removed, and the database now refuses to store a second copy.",
                "A form is complete when it has a completion date. The separate complete/not-complete marker is gone, so the two can no longer disagree.",
                "Compliance records are generated for every annual cycle from a client's effective date onward. Closed years are left open rather than assumed complete."
            ]),
        new(
            "What to expect on the first launch after updating",
            [
                "The first launch on a computer holding real records takes noticeably longer. Sati backs the database up, merges duplicate compliance records, and applies both schema updates before the sign-in window appears.",
                "Quarterly reviews previously tracked only through review evidence, and historical years on any client entered with a backdated effective date, may appear open. Review them individually; do not bulk-close them or supply guessed dates.",
                "If anything is wrong, Sati stops before writing and names the backup rather than starting against a half-updated database."
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
