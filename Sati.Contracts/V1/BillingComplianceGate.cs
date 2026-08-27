namespace Sati.Contracts.V1;

[Flags]
public enum BillingComplianceRequirements
{
    None = 0,
    QuarterlyReviews = 1 << 0,
    Pcp = 1 << 1,
    ComprehensiveAssessment = 1 << 2,
    Reclassification = 1 << 3,
    SafetyPlan = 1 << 4,
    PrivacyPractices = 1 << 5,
    AgencyRelease = 1 << 6,
    DhhsRelease = 1 << 7,
    MedicalRelease = 1 << 8,
    All = QuarterlyReviews | Pcp | ComprehensiveAssessment | Reclassification |
          SafetyPlan | PrivacyPractices | AgencyRelease | DhhsRelease | MedicalRelease
}

public sealed record ComplianceFormSnapshot(
    string Type,
    DateTime DueDate,
    DateTime? CompletedDate);

public sealed record BillingComplianceResult(
    bool Passed,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Shared billing-compliance decision for the desktop client and API. A document
/// blocks only after its due date has passed and only until its completion date.
/// The agency setting controls which document types participate in the gate.
/// </summary>
public static class BillingComplianceGate
{
    public const BillingComplianceRequirements DefaultRequirements =
        BillingComplianceRequirements.QuarterlyReviews |
        BillingComplianceRequirements.Pcp |
        BillingComplianceRequirements.ComprehensiveAssessment |
        BillingComplianceRequirements.Reclassification |
        BillingComplianceRequirements.SafetyPlan;

    public static BillingComplianceResult Evaluate(
        DateTime? effectiveDate,
        IEnumerable<ComplianceFormSnapshot> forms,
        DateTime today,
        string? beingCompleted = null,
        BillingComplianceRequirements requirements = DefaultRequirements)
    {
        // Kept in the shared signature because cycle generation and callers still
        // own an effective date, but its absence is a profile/data-quality issue;
        // it is not an incomplete overdue document and therefore cannot fail this gate.
        _ = effectiveDate;

        var overdue = forms
            .Where(form => IsRequired(form.Type, requirements))
            .Where(form => IsIncompleteAndOverdue(form.DueDate, form.CompletedDate, today))
            .OrderBy(form => form.DueDate)
            .ThenBy(form => form.Type, StringComparer.Ordinal)
            .ToList();

        var exemptionIndex = -1;
        for (var index = overdue.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(overdue[index].Type, beingCompleted, StringComparison.Ordinal))
                continue;
            exemptionIndex = index;
            break;
        }

        var reasons = overdue
            .Where((_, index) => index != exemptionIndex)
            .Select(form => $"{DisplayName(form.Type)} was due {form.DueDate:MMM d, yyyy} and is incomplete.")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new BillingComplianceResult(reasons.Count == 0, reasons);
    }

    public static IReadOnlyList<string> EvaluateBillingWindow(
        IEnumerable<ComplianceFormSnapshot> forms,
        DateTime serviceDate,
        BillingComplianceRequirements requirements = DefaultRequirements)
        => forms
            .Where(form => IsBillingWindowBlocked(
                form.Type, form.DueDate, form.CompletedDate, serviceDate, requirements))
            .OrderBy(form => form.DueDate)
            .ThenBy(form => form.Type, StringComparer.Ordinal)
            .Select(form => $"{DisplayName(form.Type)} was due {form.DueDate:MMM d, yyyy} " +
                            "and was not completed as of this service date.")
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static bool IsBillingWindowBlocked(
        string formType,
        DateTime dueDate,
        DateTime? completedDate,
        DateTime serviceDate,
        BillingComplianceRequirements requirements = DefaultRequirements)
        => IsRequired(formType, requirements) &&
           serviceDate.Date > dueDate.Date &&
           (completedDate is null || serviceDate.Date < completedDate.Value.Date);

    public static bool IsRequired(
        string formType,
        BillingComplianceRequirements requirements)
    {
        var requirement = RequirementFor(formType);
        return requirement != BillingComplianceRequirements.None &&
               (requirements & requirement) == requirement;
    }

    public static bool IsSupported(BillingComplianceRequirements requirements) =>
        (requirements & ~BillingComplianceRequirements.All) == 0;

    private static bool IsIncompleteAndOverdue(
        DateTime dueDate,
        DateTime? completedDate,
        DateTime asOfDate)
        => dueDate.Date < asOfDate.Date &&
           (completedDate is null || completedDate.Value.Date > asOfDate.Date);

    private static BillingComplianceRequirements RequirementFor(string type) => type switch
    {
        "Q1R" or "Q2R" or "Q3R" or "Q4R" => BillingComplianceRequirements.QuarterlyReviews,
        "PCP" => BillingComplianceRequirements.Pcp,
        "ComprehensiveAssessment" => BillingComplianceRequirements.ComprehensiveAssessment,
        "Reclassification" => BillingComplianceRequirements.Reclassification,
        "SafetyPlan" => BillingComplianceRequirements.SafetyPlan,
        "PrivacyPractices" => BillingComplianceRequirements.PrivacyPractices,
        "Release_Agency" => BillingComplianceRequirements.AgencyRelease,
        "Release_DHHS" => BillingComplianceRequirements.DhhsRelease,
        "Release_Medical" => BillingComplianceRequirements.MedicalRelease,
        _ => BillingComplianceRequirements.None
    };

    public static string DisplayName(string type) => type switch
    {
        "PCP" => "PCP",
        "ComprehensiveAssessment" => "Comprehensive Assessment",
        "Reclassification" => "Reclassification",
        "SafetyPlan" => "Safety Plan",
        "PrivacyPractices" => "Privacy Practices",
        "Release_Agency" => "Agency Release",
        "Release_DHHS" => "DHHS Release",
        "Release_Medical" => "Medical Release",
        "Q1R" => "Q1 Review",
        "Q2R" => "Q2 Review",
        "Q3R" => "Q3 Review",
        "Q4R" => "Q4 Review",
        _ => type
    };
}
