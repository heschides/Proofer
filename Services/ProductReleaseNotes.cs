namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "A clean slate, clearly seen";
    public const string ReleaseDate = "September 6, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Demo administrators can restore the approved baseline",
            [
                "A Demo-only Admin action restores the complete approved superhero and television-themed dataset, including users, clients, notes, billing records, and demonstration passwords.",
                "The reset rolls showcase dates forward after restoration and refuses to run if the database structure no longer matches the reviewed baseline.",
                "Demo changes are paused during restoration, and every existing Demo session is signed out so an old screen cannot continue against replaced data.",
                "The reset is not available in Local Production. It does not read, copy, or change Production data."
            ]),
        new(
            "Dark themes keep controls and identity readable",
            [
                "Menus, selected navigation, workflow buttons, dialogs, and status labels now use explicit foreground and background pairs instead of borrowing the window background as a text color.",
                "The Hello badge uses a readable raised surface with an accent outline, including on the darkest themes.",
                "Strong success and warning actions have dedicated contrasting text colors in both light and dark palettes."
            ]),
        new(
            "Decorative patterns step back from working content",
            [
                "Paisley, Art Nouveau, and Mid-Century Modern softly blur their pattern behind navigable content so labels and field boundaries remain clear.",
                "Navigation decoration remains crisp, and Sati never blurs the controls, case notes, forms, or tables themselves."
            ]),
        new(
            "Submitted claims now have a visible staging lane",
            [
                "Submit & Lock moves a billing period out of the draft queue and into a visible 837 staging grid.",
                "Billing staff can choose staged periods for generation, and successfully generated periods leave staging immediately with a clear confirmation.",
                "A submitted historical period that fails today's exact claim gate is quarantined before 837 generation and explains what must be corrected.",
                "Before any exchange history exists, an authorized billing user can return a blocked period to Draft for correction; generated or transmitted work cannot be rewound."
            ]),
        new(
            "The daily Demo refresh repairs synthetic claim snapshots",
            [
                "Synthetic Demo claims now receive complete billing identifiers, procedure details, units, charges, place of service, diagnosis values, and matching frozen snapshots.",
                "The deliberately incomplete diagnosis teaching profile receives a fictional F89 billing fallback so the lesson remains visible without breaking the claims pipeline.",
                "The refresh verifies every repaired claim after writing it and fails visibly if an invalid synthetic claim remains."
            ]),
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
