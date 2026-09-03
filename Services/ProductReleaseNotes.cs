namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "A quieter screen";
    public const string ReleaseDate = "September 3, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Build a case note from what you checked",
            [
                "A Build Case Note Template button under the narrative turns the meeting facts you ticked into a structured note with Meeting Details, Observations, and Discussion and Activity headings.",
                "Anything already in the narrative box is kept exactly as written and moved below a Meeting Narrative header. Nothing is overwritten or rewritten.",
                "Every line comes from a control you selected. The template invents nothing and needs no AI model.",
                "The Format with Local AI button is withdrawn for now. The drafting pipeline itself is unchanged and can return."
            ]),
        new(
            "The next form is suggested again",
            [
                "The suggested follow-up row below the narrative box now names the client's next outstanding form, not only a form already inside its open window.",
                "Because most quarterly reviews are only 'open' on their exact due date, that row had been blank almost all the time.",
                "Accepting the suggestion still appends one plain follow-up line, and a form already satisfied is never suggested."
            ]),
        new(
            "A screen that covers itself",
            [
                "After ten minutes with no keyboard or mouse activity, Sati blurs its window behind a Paused card so an unattended screen is not readable across the room.",
                "Any key or click brings it straight back, and that first key or click is used only to wake Sati so it cannot press a control you could not see.",
                "Settings offers delays from one minute to an hour, or Never. The choice is personal and saved for your Sati account on this computer.",
                "This hides the screen only. It does not lock Windows and asks for no password."
            ]),
        new(
            "Lighter buttons in the orange palettes",
            [
                "Blue-Gray Pearl and Cedar Grove now fill their buttons with a much lighter orange that keeps the same hue.",
                "Orange accent text is unchanged. Button fill is now a separate palette value, so one can be adjusted without moving the other.",
                "Every other theme looks exactly as it did."
            ]),
        new(
            "Still planned before commercial production",
            [
                "An in-app way to record an exact completion date for annual documents, as the 90-Day Reviews workspace already allows for quarterly attestations.",
                "A structured daily-task record, if users need durable completion, assignment, linking, or reporting beyond the editable scratchpad.",
                "A real legal-hold registry and governed archive lifecycle for ordinary clients.",
                "Office Ally transport and authenticated 999, TA1, and 277CA ingestion tied to immutable ClaimLine snapshots.",
                "835 import from the payer/EFT source of truth, raw-segment drill-through, corrected claim frequency 7/8 workflow, and durable audit posting.",
                "Automated retention and legal-hold enforcement; production identity and MFA; external monitoring; payer enrollment and certification."
            ])
    ];
}
