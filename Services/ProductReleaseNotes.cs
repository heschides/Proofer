namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Visible claims and quiet installs";
    public const string ReleaseDate = "September 6, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Billing shows what will be submitted",
            [
                "The Billing Period selector is now a focused draft queue. After Submit & Lock succeeds, that period leaves the selector and moves forward to 837P generation and Submission Home.",
                "A claim line grid beside the selector shows the client, service date, units, charge, procedure, diagnosis, readiness, and the exact correction needed before submission.",
                "The preview, Submit & Lock action, and 837P generator now use the same exact frozen-claim readiness rule, so a line that cannot generate is stopped and explained earlier."
            ]),
        new(
            "Setup stays out of the command window",
            [
                "Demo and Local installers now keep their internal PowerShell work hidden and show one Sati-branded progress window instead.",
                "If Microsoft LocalDB is missing, Windows may still show its normal permission prompt; that security prompt is intentional."
            ]),
        new(
            "Four new themes",
            [
                "Settings now includes Ironworks Matte, Paisley, Art Nouveau, and Mid-Century Modern.",
                "Their geometric and decorative patterns stay on the outer shell and navigation, while forms, notes, and tables keep calm readable surfaces."
            ]),
        new(
            "Account changes now protect the whole workspace",
            [
                "Before Sati opens an account-change or sign-in window, it covers the entire workspace so information from the outgoing account cannot remain visible.",
                "After a successful account change, Sati clears the outgoing account's clients, notes, approvals, billing, administration, scratchpad, and chat state before installing the new identity.",
                "Slow results that belong to the old account are discarded instead of being allowed to refill a screen after the change.",
                "Canceling an account change safely restores the existing workspace. Re-authenticating the same account preserves in-progress drafts."
            ]),
        new(
            "Demo 837P files can complete a realistic test loop",
            [
                "After generating test 837P files in the current Demo session, billing staff can choose a mock clearinghouse outcome and submit those exact retained files.",
                "The mock records a synthetic transmission and processes the selected 999, 277CA, or 835 response through Sati's normal claim-exchange history.",
                "Submission Home and Remittances refresh after the response so accepted, rejected, adjusted, denied, and paid demonstrations can be reviewed in one workflow.",
                "This tool is Demo-only. It does not transmit a real claim to Office Ally, a payer, or any other clearinghouse."
            ]),
        new(
            "The 1.2.49 workflow improvements remain included",
            [
                "Supervisors can page through newest-first note approvals and filter by case manager, client, service-date range, or search term.",
                "Billing-period choices show case-manager names, claim counts, workflow state, and readiness; Submit & Lock remains separate from 837P generation.",
                "The synthetic Demo caseload refresh keeps showcase dates current, fills ordinary fictional profiles, and preserves a small set of clearly labeled teaching exceptions.",
                "Team chat and electronic signing remain controlled test features with their existing safety restrictions."
            ]),
        new(
            "The Overview now fits the space you have",
            [
                "Sati rearranges the Overview as its window changes size so Notes, deadlines, forms, productivity, and Work Agenda remain available.",
                "Focus note gives the current note the available workspace without creating a new draft, and returning to Overview preserves what you typed.",
                "Easy Eyes remains one switch for larger text and controls, supported by the same responsive layout."
            ]),
        new(
            "Supervisors can work through large approval queues",
            [
                "Pending Approvals loads 10 notes at a time and keeps the selected filters while more results are loaded.",
                "Approve all within threshold defaults to a maximum of 4 units per note; opening, scrolling, filtering, or editing the threshold never approves anything.",
                "Every approval still passes supervision scope, compliance, revision, validity, and service-time conflict checks."
            ]),
        new(
            "Still planned before commercial production",
            [
                "A real clearinghouse transport, authenticated acknowledgements, payer-sourced 835 import, corrected claim workflows, enrollment, and certification.",
                "Dual-control release of a legal hold; today's administrative release remains single-admin.",
                "Production identity and MFA, external monitoring, automated retention and legal-hold enforcement, and the remaining operational controls recorded in the project agenda.",
                "Independent legal, security, accessibility, and program review before real clinical, signing, cross-agency chat, or claims use."
            ])
    ];
}
