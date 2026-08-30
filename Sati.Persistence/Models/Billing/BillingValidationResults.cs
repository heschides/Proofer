namespace Sati.Models.Billing
{
    public record BillingValidationResult(
        bool IsValid,
        Note Note,
        IReadOnlyList<string> Errors);
}