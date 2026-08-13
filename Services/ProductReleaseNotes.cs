namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Demo readiness and governance";
    public const string ReleaseDate = "August 13, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Admin and audit",
            [
                "Added an Admin operations view with database status, retained audit and EDI counts, and explicit retention-policy visibility.",
                "Added a reason-gated, agency-scoped audit activity CSV export and records each export in the audit trail.",
                "Added a Person lifecycle timeline and auditor-friendly PDF showing who changed each field and when."
            ]),
        new(
            "Safety and reliability",
            [
                "Expanded agency, assignment, author, and reviewer authorization checks across cloud and local-development workflows.",
                "Added conflict protection for assessments, People, notes, AT requests, agency settings, and scratchpads.",
                "Made billing submission and EDI generation safe to retry without creating duplicate successful results.",
                "Fixed calendar-day selection so note details render without repeated error dialogs."
            ]),
        new(
            "Billing pipeline",
            [
                "Billing administrators can configure the procedure, modifier, unit rate, submitter, payer, and contact values for their agency.",
                "The billing queue now rechecks approval, current compliance, historical billing gaps, member/provider identifiers, structured claim addresses, and EDI configuration before promotion.",
                "Section 13 service time now retains partial units after the one-unit minimum, and claim charges are calculated separately from units.",
                "837P files require a submitted billing period and use immutable claim snapshots, structural envelope checks, subscriber addresses, and retry-safe generation."
            ]),
        new(
            "Demo and support",
            [
                "Added repeatable Demo packaging, health preflight, canonical local-data reset, and company-demo operating guidance.",
                "Unexpected errors now show a short support reference instead of a developer stack trace.",
                "Agency Admins can review a PHI-minimized incident table and explainable 30-day Incident Health score; a separately controlled platform operator sees audited cross-agency health.",
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
