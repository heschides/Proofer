namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Clearer Add Person requirements";
    public const string ReleaseDate = "August 27, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Clear requirements at a glance",
            [
                "First name, last name, date of birth, and biography now carry visible required-field asterisks.",
                "A compact completion guide changes each required item from orange to green as meaningful information is entered, while retaining text labels so status is not conveyed by color alone.",
                "All other details are identified as optional; representative-payee income and regular check-request needs become required only when Yes is selected."
            ]),
        new(
            "Email is genuinely optional",
            [
                "The active Add Person editor now includes the email field and clearly labels it optional.",
                "Leaving email blank no longer blocks a save; Sati checks email format only when an address is entered.",
                "Validation and accessibility coverage protect the optional-email behavior and the live requirement guide."
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
