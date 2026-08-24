namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "One note panel and uninterrupted sessions";
    public const string ReleaseDate = "August 23, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "One panel for reading and writing a note",
            [
                "A selected note is shown in one place. The separate read-only Note Detail panel is gone, so a note can no longer be displayed twice with nothing keeping the two copies agreeing.",
                "Viewing is a locked mode of the same panel. The heading reads New Note, View Note, or Edit Note and is announced, so the mode is stated rather than only drawn with a padlock.",
                "Locked text stays selectable, scrollable, and copyable. Fields become read-only instead of disabled, so reading a note is easier than it was before, not harder.",
                "The lock is a mistake-guard, not a permission. The server still decides who may change a note.",
                "Selecting or double-clicking another note first asks before replacing unsaved work in the panel, and declining restores the note that was open rather than leaving the grid and the panel describing different records.",
                "The notes log and the dashboard now share one definition of what a double-click does, so the two screens cannot drift apart."
            ]),
        new(
            "Sessions that last as long as you are working",
            [
                "Sati now renews a Demo session on its own schedule, so an app left open during a meeting or a phone call no longer loses its session while you are still at your desk.",
                "An unattended workstation still lapses. Renewal follows real activity, so a machine nobody has touched signs itself out rather than staying open all day.",
                "An expired session asks for your password in place instead of requiring Sati to be restarted. Signing back in as yourself keeps everything on screen, including unsaved agenda text, which then saves normally.",
                "Switch User now says when a session has expired instead of showing an empty account list, which read as though no other accounts existed.",
                "A refused renewal is reported once as an ended session rather than as a separate unexplained failure on every screen that happened to be loading."
            ]),
        new(
            "Journal reminders",
            [
                "A Reminder is written straight to the client's journal through the server, under the same agency check and history as any other change.",
                "The transitional path that wrote the whole journal back when the Demo server was older than this client has been removed, along with the warning band it raised. The Demo API now has the journal-entries route everywhere it is needed.",
                "A client whose journal has never been written opens as an empty journal instead of reporting that the journal could not be loaded."
            ]),
        new(
            "Connectivity and recovery",
            [
                "Demo requests retry only proven DNS failures that could not have reached the API; timeouts and ambiguous write failures are never automatically repeated.",
                "A failed Today's Work save keeps the note text visible and gives safer, more specific recovery guidance without placing note text in diagnostics.",
                "Client/server compatibility fingerprints important persistence-contract shapes as well as routes, so an older API cannot silently discard newly added Profile fields.",
                "The service-day bar no longer reports a binding failure for every band it draws."
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
