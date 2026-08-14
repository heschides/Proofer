namespace Sati.Contracts.V1;

public sealed record ComplianceFormSnapshot(
    string Type,
    DateTime DueDate,
    bool IsCompliant);

public sealed record BillingComplianceResult(
    bool Passed,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Shared billing-compliance decision for the desktop client and API. Returning
/// reasons with the decision prevents a screen from showing only "non-compliant"
/// while another layer applies the actual rules.
/// </summary>
public static class BillingComplianceGate
{
    private static readonly string[] RequiredAnnualTypes =
    [
        "PCP", "ComprehensiveAssessment", "Reclassification", "SafetyPlan"
    ];

    private static readonly HashSet<string> ReviewTypes =
        ["Q1R", "Q2R", "Q3R", "Q4R"];

    public static BillingComplianceResult Evaluate(
        DateTime? effectiveDate,
        IEnumerable<ComplianceFormSnapshot> forms,
        DateTime today,
        string? beingCompleted = null)
    {
        if (effectiveDate is null)
        {
            return new BillingComplianceResult(false,
            [
                "No active compliance cycle was found because the client has no effective date."
            ]);
        }

        var yearsElapsed = today.Year - effectiveDate.Value.Year;
        if (today < effectiveDate.Value.AddYears(yearsElapsed))
            yearsElapsed--;
        var cycleStart = effectiveDate.Value.AddYears(yearsElapsed);
        var cycleEnd = effectiveDate.Value.AddYears(yearsElapsed + 1);
        var currentForms = forms
            .Where(form => form.DueDate > cycleStart && form.DueDate <= cycleEnd)
            .ToList();
        var reasons = new List<string>();

        foreach (var type in RequiredAnnualTypes)
        {
            if (string.Equals(type, beingCompleted, StringComparison.Ordinal))
                continue;
            var form = currentForms
                .Where(candidate => string.Equals(candidate.Type, type, StringComparison.Ordinal))
                .OrderByDescending(candidate => candidate.DueDate)
                .FirstOrDefault();
            if (form is null || !form.IsCompliant)
                reasons.Add($"{DisplayName(type)} is not marked compliant for the current cycle.");
        }

        foreach (var review in currentForms
                     .Where(form => ReviewTypes.Contains(form.Type) && form.DueDate.Date <= today.Date)
                     .OrderBy(form => form.DueDate))
        {
            if (string.Equals(review.Type, beingCompleted, StringComparison.Ordinal) || review.IsCompliant)
                continue;
            reasons.Add($"{DisplayName(review.Type)} was due {review.DueDate:MMM d, yyyy} and is not marked compliant.");
        }

        return new BillingComplianceResult(reasons.Count == 0, reasons.Distinct().ToList());
    }

    public static string DisplayName(string type) => type switch
    {
        "PCP" => "PCP",
        "ComprehensiveAssessment" => "Comprehensive Assessment",
        "Reclassification" => "Reclassification",
        "SafetyPlan" => "Safety Plan",
        "Q1R" => "Q1 Review",
        "Q2R" => "Q2 Review",
        "Q3R" => "Q3 Review",
        "Q4R" => "Q4 Review",
        _ => type
    };
}
