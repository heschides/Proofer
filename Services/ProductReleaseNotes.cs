namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "A better fit";
    public const string ReleaseDate = "September 2, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "A clearer calendar at every size",
            [
                "The year calendar now changes its column count with the available width instead of squeezing twelve fixed cards into unreadable shapes.",
                "Month cards have clearer spacing, stronger headings, and consistent event badges while keeping every date keyboard-reachable.",
                "Sati now starts with a compact layout on 1080p and smaller displays without shrinking text or click targets."
            ]),
        new(
            "Consumer pages stay reachable",
            [
                "The consumer list, profile overview, section navigation, entry form, and editor now keep visible vertical scrollbars when their content is taller than the window.",
                "Notes and Journal keep usable working height, with forms before them and reference panels following below.",
                "Both the full client list and compact selector now use the same clear Add Consumer action."
            ]),
        new(
            "Carefully update an existing profile from Credible",
            [
                "Agency administrators can enable existing-profile Credible updates in Settings. The option is off by default.",
                "When enabled, open one consumer for editing, review the Credible fields individually, and then use the ordinary Save changes action. Import never saves automatically.",
                "Different nonblank Credible client IDs are refused before any field changes. Bulk folder imports continue to skip existing consumers."
            ]),
        new(
            "Track Vocational Rehabilitation assignments",
            [
                "A consumer marked Open with VR can now record the assigned Vocational Rehabilitation Counselor and assistant.",
                "The assistant label defaults to VSA. An agency administrator can change that title in Settings without rewriting the assigned person's name.",
                "Turning off Open with VR hides the assignments but keeps them available if the VR case reopens."
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
