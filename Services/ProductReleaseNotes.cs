namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Startup resource safety and concurrency";
    public const string ReleaseDate = "August 14, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Admin and audit",
            [
                "User profiles now identify the assigned supervisor by name in both the administrative Users view and My Account.",
                "Added an Admin operations view with database status, retained audit and EDI counts, and explicit retention-policy visibility.",
                "Added a reason-gated, agency-scoped audit activity CSV export and records each export in the audit trail.",
                "Added a Person lifecycle timeline and auditor-friendly PDF showing who changed each field and when."
            ]),
        new(
            "Safety and reliability",
            [
                "Corrected Demo startup resource addresses so the renamed Sati.Demo executable can always load its icon and watercolor branding without looking for a nonexistent Sati assembly.",
                "Completed a focused concurrency audit and added deterministic overlap tests for scratchpad saves, calendar navigation, billing loads, and stale client-note responses.",
                "Serialized scratchpad, billing, and account-switch operations so repeated clicks, timers, window closing, and session transitions cannot compete for the same state.",
                "Calendar and client-note screens now publish only the newest applicable network response when requests finish out of order.",
                "Settings startup and abandoned-note background maintenance now observe their own failures instead of surfacing surprise application-level errors.",
                "Separated personal Settings from Admin-only agency configuration, and converted rejected Settings saves into clear inline guidance instead of crash dialogs.",
                "Agency settings now change only through the explicit Save Settings action; closing the window no longer launches an unexpected cloud save.",
                "Prevented overlapping sign-in completions from closing the login dialog twice or leaving an orphaned error message behind the application.",
                "Fixed repeated close requests so the main and Settings windows cannot crash while completing their save-on-close work.",
                "Corrected the final save-on-close handoff so Windows fully finishes the original close request before Sati closes the saved window.",
                "Journal autosaves and account-switch flushes now run one at a time, preventing overlapping cloud updates from crashing Switch User.",
                "Incident reports are now saved to a durable local outbox and retried after sign-in when a connection or process interruption prevented delivery.",
                "An unclean prior shutdown is reported on the next matching sign-in, including terminations that an in-process exception handler cannot observe.",
                "An empty incident feed now says No telemetry instead of incorrectly reporting a perfect health score.",
                "Expanded agency, assignment, author, and reviewer authorization checks across cloud and local-development workflows.",
                "Added conflict protection for assessments, People, notes, AT requests, agency settings, and scratchpads.",
                "Made billing submission and EDI generation safe to retry without creating duplicate successful results.",
                "Fixed calendar-day selection so note details render without repeated error dialogs."
            ]),
        new(
            "Billing pipeline",
            [
                "Note entry now labels Pending as Save as Draft and Logged as Submit for Supervisor Review, with clearly spaced guidance explaining each status's review and billing consequences.",
                "Non-compliant supervisor cards and client profiles now show the exact forms or reviews blocking billing, and affected clients are visibly marked in the roster.",
                "Added an end-to-end verification covering draft, supervisory submission, approval, billing-period submission, and test 837P generation.",
                "Billing administrators can configure the procedure, modifier, unit rate, submitter, payer, and contact values for their agency.",
                "The billing queue now rechecks approval, current compliance, historical billing gaps, member/provider identifiers, structured claim addresses, and EDI configuration before promotion.",
                "Section 13 service time now retains partial units after the one-unit minimum, and claim charges are calculated separately from units.",
                "837P files require a submitted billing period and use immutable claim snapshots, structural envelope checks, subscriber addresses, and retry-safe generation."
            ]),
        new(
            "Demo and support",
            [
                "The login and splash screens now use the same bright watercolor Bodhi leaf as the application icon, with a newly composed high-resolution splash layout.",
                "Placed the installed release number in a dedicated, higher-contrast login footer so it remains visible at Windows display scaling.",
                "A bright geometric watercolor Bodhi-leaf application icon with a softly rounded ivory container improves recognition in Windows, title bars, and installer shortcuts.",
                "The login window now shows the installed release number, and installer upgrades refresh recognized Sati taskbar pins with a version-specific icon.",
                "Accessibility safeguards now label icon controls and checkboxes and give overdue matrix cells a visible text status.",
                "Runtime smoke coverage renders the feature views, and installer acceptance repeatedly proves responsive, zero-code normal window shutdown.",
                "Theme resources now resolve relative to Sati itself so hosted views do not depend on the entry executable.",
                "Added repeatable Demo packaging, health preflight, canonical local-data reset, and company-demo operating guidance.",
                "Unexpected errors now show a short support reference instead of a developer stack trace.",
                "Global Admin desktop failures are accepted as platform-scoped incidents without exposing agency business routes or blaming an individual tenant.",
                "Global Admin can change its own password while remaining blocked from agency user-management and business-data endpoints.",
                "Switching away from Global Admin now uses neutral username and password entry instead of attempting to enumerate an agency directory.",
                "Agency Admins can review a PHI-minimized incident table and explainable 30-day Incident Health score; a separately controlled platform operator sees audited cross-agency health.",
                "Incident reports now aggregate safely during simultaneous failures, and Admins can search, filter, investigate, resolve, and see explicit alert thresholds.",
                "Expanded automated coverage across authorization, integration, migration, reporting, and domain behavior."
            ]),
        new(
            "Still planned before commercial production",
            [
                "Automated retention and legal-hold enforcement; the current dashboard correctly reports PolicyOnly.",
                "Production identity and MFA, external alert routing, backup/restore drills, payer certification, and controlled production deployment.",
                "Clearinghouse test-file acceptance, payer enrollment/rate verification, acknowledgments, rejections, remittances, and reconciliation.",
                "Broader immutable clinical-document versions, amendments, signatures, and mobile/web clients."
            ])
    ];
}
