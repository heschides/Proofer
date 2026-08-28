namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Safer note reassignment and themed scratchpads";
    public const string ReleaseDate = "August 28, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Correct the client without duplicating the note",
            [
                "An editable saved note can now be moved to the correct client with the existing Client selector.",
                "Sati names both clients in an explicit confirmation before changing the record, and No safely restores the original selection.",
                "The same note is moved rather than copied, so correcting the client does not create a duplicate clinical record."
            ]),
        new(
            "Protected and traceable in Local and Demo",
            [
                "A note can move only between clients on the signed-in case manager's own caseload and in the same agency.",
                "Stale edits and submitted-record locks still apply; logged or approved notes must be returned before correction.",
                "Each successful reassignment is audited with record IDs only, keeping client names and note text out of the general audit envelope."
            ]),
        new(
            "Scratchpad text follows the active theme",
            [
                "Today's Work and Tomorrow's Agenda now use the theme's primary text color instead of falling back to dark system text.",
                "The typing caret follows the same dynamic theme color, including Harbor Night and the other dark themes.",
                "Rendered-view coverage checks both scratchpad tabs under a dark theme."
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
