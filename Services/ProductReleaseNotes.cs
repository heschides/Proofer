namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Bring a caseload with you";
    public const string ReleaseDate = "September 1, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Start a consumer from a Credible export",
            [
                "The Add Client form has a From Credible button. Point it at a client's print view saved from Credible and Sati fills the form in for you.",
                "Nothing is saved until you press Add Client, exactly as when you type a client in by hand. Before that you see every field Sati found, what the export actually said, and which section of the export it came from, and you can untick anything you do not want.",
                "Fields Sati could not use are listed too, so you can see what did not come across rather than having to notice its absence.",
                "Save the print view as a web page. A printed PDF loses which value belongs to which field, so Sati refuses one and says so."
            ]),
        new(
            "Import a whole caseload during onboarding",
            [
                "Supervisors have an Import from Credible screen. Choose a folder of saved print views and Sati reads all of them and reports what it found before creating anything.",
                "The report says how many are ready, how many are already in Sati, and which files it could not read. Nothing is written until you say so.",
                "A consumer already in your agency is listed and skipped, never merged into. Running the same folder twice does not create duplicates.",
                "The exports stay on your computer. Sati reads them where they are and never uploads them."
            ]),
        new(
            "Hand consumers to a case manager",
            [
                "Imported consumers land on your own caseload first. A new Distribute caseload screen moves them to the case managers you supervise, several at a time.",
                "Each consumer moves on its own, so one that cannot be moved does not stop the rest, and you are told which and why.",
                "Imported consumers arrive without an effective date, so no compliance records are generated for them yet. Set the effective date when the case manager who will carry the consumer picks them up."
            ]),
        new(
            "What this release does not do yet",
            [
                "A Social Security number in an export is shown but not saved. Type it on the SSN panel after the consumer is created; Sati says so on the row rather than appearing to take it.",
                "Only demographics, diagnosis, insurance identifiers and guardian details are read. Notes, medications, treatment plans and authorizations in an export are ignored."
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
