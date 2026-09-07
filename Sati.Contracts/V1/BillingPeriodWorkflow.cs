namespace Sati.Contracts.V1;

/// <summary>
/// Owns the official transition that returns a locked billing period to draft before an
/// 837 has been generated. Once exchange history exists, the financial record has moved
/// forward and must use a correction/amendment workflow instead.
/// </summary>
public static class BillingPeriodWorkflow
{
    public static IReadOnlyList<string> ValidateReturnToDraft(
        bool isSubmitted,
        bool hasExchangeHistory)
    {
        var errors = new List<string>();
        if (!isSubmitted)
            errors.Add("Only a submitted and locked billing period can be returned to draft.");
        if (hasExchangeHistory)
            errors.Add("This billing period already has 837 or clearinghouse history and cannot be returned to draft.");
        return errors;
    }
}
