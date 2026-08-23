namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Responsive database waits and payee profiles";
    public const string ReleaseDate = "August 23, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Representative-payee profiles",
            [
                "A consumer's Profile now records an explicit Yes or No for whether the case manager serves as representative payee.",
                "When Yes is selected, the Profile records validated monthly income and a description of regular check-request needs; selecting No clears those subordinate details.",
                "Representative-payee changes participate in tenant authorization, optimistic concurrency, immutable Person history, and the same validation in Local Production and Demo.",
                "This information is profile context only. It does not request, approve, authorize, or release a check; the later billing notification remains a separate audited workflow.",
                "Representative-payee financial details are not sent to local AI and are not written to operational logs."
            ]),
        new(
            "Responsive database waits",
            [
                "The colorful Bodhi leaf now spins while Sati is waiting for Demo API or Local Production database activity.",
                "After eight seconds of uninterrupted activity, a modern patience window thanks the user and closes automatically when the final overlapping request finishes.",
                "The shell reserves enough height for the animation, preventing the surrounding row from resizing when the spinner starts.",
                "Settings includes a 12-second visual preview that exercises both loading stages without querying a database or accessing client information.",
                "The shared activity tracker is payload-free and records no SQL, routes, request bodies, notes, or other client information."
            ]),
        new(
            "Connectivity and recovery",
            [
                "Demo requests retry only proven DNS failures that could not have reached the API; timeouts and ambiguous write failures are never automatically repeated.",
                "A failed Today's Work save keeps the note text visible and now gives safer, more specific recovery guidance without placing note text in diagnostics.",
                "Expected cancellation of short spinner delays no longer appears as a debugger exception.",
                "Client/server compatibility now fingerprints important persistence-contract shapes as well as routes, so an older API cannot silently discard newly added Profile fields."
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
