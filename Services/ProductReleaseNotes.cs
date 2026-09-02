namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Easy on the eyes";
    public const string ReleaseDate = "September 2, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "An easier view when you need it",
            [
                "Easy Eyes is a personal, off-by-default setting that enlarges the working interface by about 30 percent.",
                "Easy Eyes hides narrative columns in note lists without removing note content and uses the horizontal client selector.",
                "The preference is remembered separately for each Sati account and Demo or Production environment on this computer."
            ]),
        new(
            "Two new warm palettes",
            [
                "Blue-Gray Pearl combines warm slate, cloud light, and a subtle champagne sheen.",
                "Cedar Grove combines pale bark, lichen, and soft forest tones.",
                "Both themes use the requested orange accent, while Local AI actions retain a distinct color."
            ]),
        new(
            "Clearer calendar controls",
            [
                "The previous-year and next-year controls now use crisp vector arrows rather than font characters that could disappear.",
                "The rounded arrow buttons provide hover, pressed, focus, tooltip, and screen-reader feedback.",
                "All existing year-navigation commands and keyboard behavior remain in place."
            ]),
        new(
            "Safer, roomier daily work",
            [
                "Closing Sati now asks for confirmation and explains that Today's Work and Tomorrow's Work will be saved.",
                "A client profile in edit mode expands vertically inside the main overview instead of adding a second cramped scrollbar.",
                "The surrounding overview remains scrollable, so every edit field stays reachable on smaller displays."
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
