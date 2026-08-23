namespace Sati.Contracts.V1;

/// <summary>
/// Shared integrity rules for the representative-payee portion of a consumer profile.
/// This profile describes recurring needs only; it does not authorize or initiate a check.
/// </summary>
public static class RepresentativePayeeRules
{
    public const int MaxRegularCheckRequestNeedsLength = 2_000;
    public const decimal MaxMonthlyIncome = 9_999_999_999_999_999.99m;

    public static IReadOnlyDictionary<string, string[]> Validate(
        bool caseManagerIsRepPayee,
        decimal? monthlyIncome,
        string? regularCheckRequestNeeds)
    {
        var errors = new Dictionary<string, string[]>();

        if (!caseManagerIsRepPayee)
        {
            if (monthlyIncome.HasValue || !string.IsNullOrWhiteSpace(regularCheckRequestNeeds))
            {
                errors["representativePayeeDetails"] =
                    ["Monthly income and check-request needs must be empty when the case manager is not the representative payee."];
            }

            return errors;
        }

        if (monthlyIncome is null || monthlyIncome <= 0)
        {
            errors["repPayeeMonthlyIncome"] =
                ["Enter the consumer's monthly income as an amount greater than zero."];
        }
        else if (decimal.Round(monthlyIncome.Value, 2) != monthlyIncome.Value)
        {
            errors["repPayeeMonthlyIncome"] =
                ["Monthly income cannot contain more than two decimal places."];
        }
        else if (monthlyIncome > MaxMonthlyIncome)
        {
            errors["repPayeeMonthlyIncome"] =
                ["Monthly income is too large to store."];
        }

        if (string.IsNullOrWhiteSpace(regularCheckRequestNeeds))
        {
            errors["repPayeeRegularCheckRequestNeeds"] =
                ["Describe the regular check-request needs, or enter “None” if there are no recurring requests."];
        }
        else if (regularCheckRequestNeeds.Trim().Length > MaxRegularCheckRequestNeedsLength)
        {
            errors["repPayeeRegularCheckRequestNeeds"] =
                [$"Regular check-request needs cannot exceed {MaxRegularCheckRequestNeedsLength:N0} characters."];
        }

        return errors;
    }
}
