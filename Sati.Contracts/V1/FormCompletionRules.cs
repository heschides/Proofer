namespace Sati.Contracts.V1;

/// <summary>
/// Shared integrity rule for dates that close a compliance form. Presentation,
/// Local Production, and the API all call this owner so a client cannot bypass
/// the rule by sending the update directly.
/// </summary>
public static class FormCompletionRules
{
    public const string FutureDateMessage = "A form completion date cannot be in the future.";

    public static string? Validate(DateTime completedOn, DateTime today) =>
        completedOn.Date > today.Date ? FutureDateMessage : null;
}
