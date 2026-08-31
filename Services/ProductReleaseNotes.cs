namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Permissions you grant one at a time";
    public const string ReleaseDate = "August 31, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Billing access no longer means administrator access",
            [
                "Until now, letting somebody work on billing meant making them an administrator, which also handed over user management, audit exports, agency settings, and the ability to delete test records. There was no way to describe a case manager who also bills.",
                "Permissions are now granted one at a time: case management, supervision, agency-wide supervision, administration, and billing. Tick the ones a person actually needs.",
                "A permission change takes effect on that person's next action rather than waiting for them to sign out and back in."
            ]),
        new(
            "Your existing accounts keep what they had",
            [
                "Everyone keeps the access they had before this update. No account has to be set up again.",
                "Directors are the one place worth a look. A Director could always review every case manager's notes across the agency, but never had administrator tools, and that stays true: agency-wide review is now its own permission, so a Director does not quietly gain the audit export or agency settings.",
                "Where somebody does need more than they had, an administrator can now grant exactly that instead of the whole administrator bundle."
            ]),
        new(
            "Claim responses come back into Sati",
            [
                "When a clearinghouse answers a submitted claim, Sati reads the response and shows what happened to each claim line, instead of leaving you to open the file yourself.",
                "Submitted work is grouped by what a biller would do about it next.",
                "A billing period stays visible while it is still a draft, so work in progress no longer disappears from the list before it is submitted."
            ]),
        new(
            "Nothing else changes in daily use",
            [
                "Caseloads, notes, scheduling, compliance, and the provider directory behave exactly as they did in 1.2.33.",
                "This release adds one database change: a permissions column on user accounts. Sati applies it at startup, and a backup is still taken before any change whenever the database holds records."
            ]),
        new(
            "Still planned before commercial production",
            [
                "Office Ally transport and authenticated 999, TA1, and 277CA ingestion tied to immutable ClaimLine snapshots.",
                "835 import from the payer/EFT source of truth, raw-segment drill-through, corrected claim frequency 7/8 workflow, and durable audit posting.",
                "Denial return to the originating note, fee-schedule underpayment detection, authorization/unit alerts, forecasting, and cross-agency benchmarking.",
                "Automated retention and legal-hold enforcement; production identity and MFA; external monitoring; payer enrollment and certification."
            ])
    ];
}
