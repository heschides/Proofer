namespace Sati.Contracts.V1;

/// <summary>
/// One API-safe choice shown by a distributed case-note client. Value is the exact contract token;
/// label and guidance are presentation text. Workflow-owned statuses and the journal-only Reminder
/// type are deliberately absent. Contact remains an accepted legacy contract token, but new notes
/// identify Phone and Email separately.
/// </summary>
public sealed record CaseNoteEntryOption(string Value, string Label, string Guidance);

public static class CaseNoteEntryOptions
{
    public static IReadOnlyList<CaseNoteEntryOption> CaseManagerStatuses { get; } =
    [
        new("Scheduled", "Scheduled", "Planned work; not submitted for review or billing."),
        new("Pending", "Draft", "Save privately in your work queue so you can finish it later."),
        new("Logged", "Submit for review", "Send the completed note to the supervisor review queue."),
        new("Cancelled", "Cancelled", "The planned service did not occur."),
        new("Delayed", "Delayed", "The planned service was postponed.")
    ];

    public static IReadOnlyList<CaseNoteEntryOption> NoteTypes { get; } =
    [
        new("Visit", "Visit", "An in-person or documented visit contact."),
        new("Phone", "Phone", "A phone call or voice contact."),
        new("Email", "Email", "An email or other written electronic contact."),
        new("Form", "Form", "Documentation of a specific compliance form."),
        new("Other", "Other", "Case-management documentation that does not fit another type.")
    ];

    public static IReadOnlyList<CaseNoteEntryOption> FormTypes { get; } =
    [
        new("Q1R", "Quarter 1 90-Day Review", "First quarterly review."),
        new("Q2R", "Quarter 2 90-Day Review", "Second quarterly review."),
        new("Q3R", "Quarter 3 90-Day Review", "Third quarterly review."),
        new("Q4R", "Quarter 4 90-Day Review", "Fourth quarterly review."),
        new("PCP", "Person-Centered Plan", "Person-centered plan documentation."),
        new("ComprehensiveAssessment", "Comprehensive Assessment", "Comprehensive assessment documentation."),
        new("Reclassification", "Reclassification", "Reclassification documentation."),
        new("SafetyPlan", "Safety Plan", "Safety-plan documentation."),
        new("PrivacyPractices", "Privacy Practices", "Privacy-practices documentation."),
        new("Release_Agency", "Agency Release", "Agency release documentation."),
        new("Release_DHHS", "DHHS Release", "DHHS release documentation."),
        new("Release_Medical", "Medical Release", "Medical release documentation.")
    ];
}
