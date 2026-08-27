namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Faster note entry and clearer Visit details";
    public const string ReleaseDate = "August 26, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Personal typing shortcuts",
            [
                "Every user can now map Win+Shift+1 through Win+Shift+0 to ten personal text snippets in Settings.",
                "A shortcut inserts up to 200 characters only into the note narrative or Today's Work and Tomorrow's Agenda Scratchpad editors.",
                "Mappings stay on this Windows profile and are kept separate for each Sati user and for Demo versus Local Production."
            ]),
        new(
            "More complete Visit notes",
            [
                "Visit Setting, Appearance, Participation, and Health/Safety now use checkboxes so more than one observation can be recorded in each group.",
                "Existing Visit notes remain readable, and newly saved notes retain compatibility with their earlier single-choice format.",
                "Local drafting assistance now includes every checked Visit observation while keeping the case manager in control of the final narrative."
            ]),
        new(
            "Clearer date selection",
            [
                "Date fields once again show a themed calendar button inside the picker, aligned vertically with the date text.",
                "The restored button keeps the normal Windows date-picker popup, keyboard operation, and accessible name."
            ]),
        new(
            "Still planned before commercial production",
            [
                "The representative-payee billing-department check-release notification, with its own request, approval, release evidence, audit history, concurrency, and idempotency.",
                "Automated retention and legal-hold enforcement; the current operations panel correctly reports PolicyOnly.",
                "Production identity and MFA, external alert routing, backup/restore drills, payer certification, and a controlled cloud Production deployment.",
                "Clearinghouse acceptance, payer enrollment and rate verification, acknowledgments, rejections, remittances, and reconciliation."
            ])
    ];
}
