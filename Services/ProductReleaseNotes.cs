namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Clear queues and current demos";
    public const string ReleaseDate = "September 6, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Team chat is ready for controlled Demo testing",
            [
                "Authorized staff can use API-mediated team rooms with membership, tenant, case, audit, concurrency, and reconnect protections.",
                "A collapsible room dock and open-room tabs make it easier to move between conversations while preserving each room's unsent draft.",
                "Chat remains off by default and is restricted to validated Demo and testing identities until agency acceptance is complete."
            ]),
        new(
            "Electronic signing is ready for synthetic testing",
            [
                "Staff can freeze an exact document, issue a protected request, and capture consent, signing intent, decisions, and retained evidence.",
                "The public signing portal, copy preparation, and email notifications remain disabled by default. Real-client use still requires legal, program, accessibility, hosting, and agency approval."
            ]),
        new(
            "The Overview now fits the space you have",
            [
                "Sati now rearranges the Overview automatically as its window gets wider, narrower, taller, or shorter. Notes, deadlines, forms, productivity, and Work Agenda stay available through labeled workspace choices when they cannot fit together.",
                "Work Agenda is the default center workspace for users who have not already chosen a preference. Turning off \"Keep Work Agenda in the center\" keeps Notes as the preferred starting workspace.",
                "Focus note gives the current note the available workspace without making a new draft. Returning to Overview preserves what you typed, including the narrative editor's caret, selection, and undo history.",
                "Easy Eyes remains one switch for larger text and controls. The same responsive layout now supplies the extra room instead of relying on monitor resolution or a startup warning."
            ]),
        new(
            "Case Management has a clearer route to every workspace",
            [
                "Overview, Clients, Notes, Caseload Matrix, Calendar, Statistics, Reviews, Providers, Help, and Documents now share one direct Case Management navigation row.",
                "Guidance and Reference appear under Help. AT Requests, Authorized Rep, and Releases appear under Documents, with the same existing workspaces and permissions behind them.",
                "Empty Overview panels now say which client, filter, date range, or loading state they represent instead of leaving an unexplained blank area."
            ]),
        new(
            "Supervisors can work through large approval queues",
            [
                "Pending Approvals loads 10 notes at a time. Scrolling down or choosing Load more retrieves the next page without losing the selected case-manager filter.",
                "The newest submitted notes now appear first. Filters inside the queue can narrow the work by case manager, client, service-date range, or a search across narrative, client, and case-manager names.",
                "Approve all within threshold is a deliberate button with a default maximum of 4 units per note. Opening the page, scrolling, or editing the threshold never approves anything.",
                "Every batch item still passes supervision scope, compliance, revision, note validity, and service-time conflict checks. Notes that do not qualify remain for individual review."
            ]),
        new(
            "Client save messages distinguish saving from refreshing",
            [
                "If a client save succeeds but the screen refresh fails afterward, Sati now says the record was saved and tells the user to reload the screen without repeating the save.",
                "When a save result is truly unknown, create and edit messages now use the correct wording for that operation and ask the user to verify the record before retrying."
            ]),
        new(
            "Billing periods explain what happens next",
            [
                "Billing-period choices show the case manager's name, claim count, workflow state, and whether the period is ready to submit.",
                "Submit & Lock sits directly below the selected billing period, while 837P generation remains a separate step for submitted periods.",
                "Sati blocks zero-unit, zero-dollar, snapshot-less, or otherwise invalid claims before locking a period or attempting an 837P file."
            ]),
        new(
            "Account administration gives a definite password-reset result",
            [
                "The password-reset panel reports progress, success, validation problems, expired sessions, authorization failures, and uncertain connection results in plain language.",
                "A successful Local password reset is verified against the newly persisted password hash."
            ]),
        new(
            "The synthetic Demo caseload stays current",
            [
                "A daily Azure worker rolls showcase dates forward, completes ordinary fictional client profiles, preserves six clearly labeled teaching exceptions, and keeps the superhero and TV-show humor.",
                "Each refresh validates the Demo identity, ordinary-profile completeness, deliberate exceptions, and synthetic billing readiness before reporting success.",
                "This is a caseload refresh, not yet a full rollback of every demonstration action or a reset of demonstration passwords."
            ]),
        new(
            "Still planned before commercial production",
            [
                "Sorting client lists by last name currently affects only the Notes client picker. AT Requests, client documents, and similar screens keep their own list and are not yet affected.",
                "Optional user-resizable Overview pane widths and a reset action if user testing shows they are needed.",
                "Dual-control release of a legal hold. Today's release is single-admin.",
                "A way for a case manager to mark their own consumer No Longer Served or Deceased. An Admin can do this for any consumer from the Admin dashboard's new Status control; a case manager still has no path to it from their own caseload.",
                "A count of exactly what a consumer deletion will remove, shown before you confirm rather than only after.",
                "address2 on Credible import — no example export has confirmed Credible's own label for it yet.",
                "Independent legal and program review of the privacy-notice and safety-plan wording before real clinical use.",
                "A structured daily-task record, if users need durable completion, assignment, linking, or reporting beyond the editable scratchpad.",
                "Office Ally transport and authenticated 999, TA1, and 277CA ingestion tied to immutable ClaimLine snapshots.",
                "835 import from the payer/EFT source of truth, raw-segment drill-through, corrected claim frequency 7/8 workflow, and durable audit posting.",
                "Automated retention and legal-hold enforcement; production identity and MFA; external monitoring; payer enrollment and certification."
            ])
    ];
}
