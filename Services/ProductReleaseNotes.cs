namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "A calmer start to the workday";
    public const string ReleaseDate = "September 1, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Today's work is ready when you sign in",
            [
                "Case managers now receive a daily agenda after sign-in, once per day on each computer. It brings together overdue records, work coming due, and a quiet-period Comprehensive Assessment suggestion.",
                "Nothing is selected automatically. Choose the useful rows to append plain, editable lines to Today's Work, or skip without changing anything.",
                "The personal Appearance setting can turn the agenda off for one Sati account without changing it for anyone else at the agency."
            ]),
        new(
            "Overdue records stay honest",
            [
                "The agenda shows the oldest five incomplete overdue forms and also states the true total, so a large inherited backlog is visible without becoming an unusable wall of rows.",
                "A separate text cue identifies overdue forms that also block billing; the display never bulk-completes a form or invents a completion date.",
                "Opening an item takes you to the existing client or form workspace, where the real status and completion date remain explicit."
            ]),
        new(
            "Quarterly evidence and attestation are separate",
            [
                "The 90-Day Reviews workspace now shows both the provider evidence status and the separate Q1R–Q4R attestation status.",
                "Completing an attestation requires the actual, non-future completion date. Logging the last evidence item never silently marks the quarterly review complete.",
                "Quarters previously tracked only through review evidence may therefore still appear open. Review them individually; do not bulk-close them or supply guessed dates."
            ]),
        new(
            "Suggested follow-ups remain your decision",
            [
                "The note panel can show the selected client's next due item as a suggested follow-up on both Notes surfaces.",
                "Sati adds it to the narrative only when you choose Accept suggestion. That explicit action makes it your documented follow-up rather than an automated clinical inference.",
                "After a note saves successfully, Notes search, filters, and date selectors clear for the next entry unless you deliberately choose Keep filters."
            ]),
        new(
            "Deletion remains conservative",
            [
                "No new ordinary-client deletion path ships in this release. The proposed short cleanup window remains blocked until Sati can obtain an affirmative legal-hold result from a real registry.",
                "Unknown legal-hold state fails closed. Administrative convenience cannot erase a record that may have a retention obligation.",
                "Existing test-consumer deletion keeps its narrower audited safeguards."
            ]),
        new(
            "Still planned before commercial production",
            [
                "A structured daily-task record, if users need durable completion, assignment, linking, or reporting beyond the editable scratchpad.",
                "A real legal-hold registry and governed archive lifecycle for ordinary clients.",
                "Office Ally transport and authenticated 999, TA1, and 277CA ingestion tied to immutable ClaimLine snapshots.",
                "835 import from the payer/EFT source of truth, raw-segment drill-through, corrected claim frequency 7/8 workflow, and durable audit posting.",
                "Denial return to the originating note, fee-schedule underpayment detection, authorization/unit alerts, forecasting, and cross-agency benchmarking.",
                "Automated retention and legal-hold enforcement; production identity and MFA; external monitoring; payer enrollment and certification."
            ])
    ];
}
