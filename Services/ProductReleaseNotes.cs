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
                "Made billing submission and EDI generation safe to retry without creating duplicate successful results."
            ]),
        new(
            "Demo and support",
            [
                "Added repeatable Demo packaging, health preflight, canonical local-data reset, and company-demo operating guidance.",
                "Unexpected errors now show a short support reference instead of a developer stack trace.",
                "Expanded automated coverage across authorization, integration, migration, reporting, and domain behavior."
            ]),
        new(
            "Still planned before commercial production",
            [
                "Automated retention and legal-hold enforcement; the current dashboard correctly reports PolicyOnly.",
                "Production identity and MFA, external alert routing, backup/restore drills, payer certification, and controlled production deployment.",
                "Broader immutable clinical-document versions, amendments, signatures, and mobile/web clients."
            ])
    ];
}
