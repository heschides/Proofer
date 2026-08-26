namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Calendar reminders and consumer filters";
    public const string ReleaseDate = "August 26, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Consumer responsibilities",
            [
                "A consumer profile can now record whether the assigned case manager is the consumer's DHHS representative and whether the consumer uses Modivcare.",
                "Consumer email is now available alongside phone and address, with email-format validation before it is saved.",
                "Representative-payee support remains available with its separate monthly-income and recurring-needs profile; these new flags do not authorize a payment or release of funds.",
                "All profile changes travel through the same tenant checks, revision handling, and consumer lifecycle history as the rest of the consumer record."
            ]),
        new(
            "Caseload filtering",
            [
                "The Consumers list can now filter to representative-payee or DHHS-representative responsibilities, Modivcare, VR, home supports, community supports, shared living, day programs, and employment supports.",
                "Filtering changes only what is displayed in the caseload list; it does not expand who may view or edit a consumer."
            ]),
        new(
            "A steadier, more useful calendar",
            [
                "Selecting a calendar date now opens a focused day view with the notes logged for that date, their client, narrative, service time, duration, status, and daily totals.",
                "Calendar loading and exemption changes now show a retryable message when something goes wrong instead of allowing one failed action to cascade into an application crash.",
                "Fast year changes cannot let an older response replace the year you most recently selected, and refreshed calendar data keeps the selected date in focus."
            ]),
        new(
            "Future reminders",
            [
                "Choosing a future date while writing a note converts it to a Scheduled Reminder and places it on that calendar date.",
                "A scheduled reminder keeps the date and narrative but cannot carry service time, minutes, visit facts, review status, productivity, or billing.",
                "Undated reminders continue to use the client's journal, while dated reminders remain searchable in note history and upcoming work."
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
