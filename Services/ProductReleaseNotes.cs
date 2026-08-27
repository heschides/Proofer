namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Accurate compliance and safer client setup";
    public const string ReleaseDate = "August 27, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Accurate, configurable billing compliance",
            [
                "Only required documents that are both incomplete and past due now block billing; unfinished future-year documents no longer make a client noncompliant early.",
                "Agency Admins can choose whether reviews, PCPs, Comprehensive Assessments, Reclassification, Safety Plans, Privacy Practices, and each release type participate in billing compliance.",
                "Current queues, historical service dates, billing validation, and billing-loss reports now use the same date-based rule."
            ]),
        new(
            "Safer Add Client workflow",
            [
                "Add Client errors stay inside the Clients page instead of terminating Sati.",
                "A failed save now explains what was saved, what went wrong, and the safest fix, including when a connection must be checked before retrying.",
                "Client details, initial forms, lifecycle history, and the audit entry are validated and saved together so a database rejection cannot leave a partial client."
            ]),
        new(
            "Consistent workspaces and more appearance choices",
            [
                "AT Requests, Authorized Rep, and Releases are available together on the main case-manager navigation and remain available under Clients.",
                "Notes filters now align consistently above the notes grid.",
                "Pine Coast, Blueberry Mist, and Harbor Night add three new complete color themes."
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
