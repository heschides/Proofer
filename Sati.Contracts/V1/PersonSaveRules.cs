using System.ComponentModel.DataAnnotations;

namespace Sati.Contracts.V1;

/// <summary>
/// The single validation owner for Person creation and editing. Both persistence
/// paths call this before writing so Local and Demo cannot accept different client
/// records merely because they use different transports.
/// </summary>
public static class PersonSaveRules
{
    public const int FirstNameMaxLength = 50;
    public const int LastNameMaxLength = 50;
    public const int BioMaxLength = 1_000_000;
    public const int GuardianNameMaxLength = 100;
    public const int PhoneMaxLength = 20;
    public const int EmailMaxLength = 254;
    public const int AddressMaxLength = 250;
    public const int BillingStreetMaxLength = 55;
    public const int BillingCityMaxLength = 30;
    public const int BillingStateMaxLength = 2;
    public const int BillingZipMaxLength = 15;
    public const int PrimaryCareProviderMaxLength = 100;
    public const int HealthcareSystemMaxLength = 100;
    public const int VrStaffNameMaxLength = 150;

    // Credible ids are short numeric strings; 32 is generous. Bounded rather than unlimited
    // so a dedupe index on (AgencyId, CredibleClientId) stays a one-step change.
    public const int CredibleClientIdMaxLength = 32;

    public static IReadOnlyList<string> FormTypes { get; } =
    [
        "Q1R",
        "Q2R",
        "Q3R",
        "Q4R",
        "PCP",
        "ComprehensiveAssessment",
        "Reclassification",
        "SafetyPlan",
        "PrivacyPractices",
        "Release_Agency",
        "Release_DHHS",
        "Release_Medical"
    ];

    private static readonly HashSet<string> ValidGenders =
        new(["Unknown", "Male", "Female", "NonBinary"], StringComparer.Ordinal);
    private static readonly HashSet<string> ValidWaivers =
        new(["None", "Section21", "Section29"], StringComparer.Ordinal);
    private static readonly HashSet<string> ValidFormTypes =
        new(FormTypes, StringComparer.Ordinal);

    public static Dictionary<string, string[]> Validate(
        SavePersonRequest request,
        DateTime today,
        bool requireNewForms)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        RequiredLength(errors, "firstName", "First name", request.FirstName, FirstNameMaxLength);
        RequiredLength(errors, "lastName", "Last name", request.LastName, LastNameMaxLength);

        if (request.BirthDate.Date < new DateTime(1900, 1, 1) ||
            request.BirthDate.Date > today.Date)
        {
            errors["birthDate"] = ["Birth date must be between January 1, 1900 and today."];
        }

        RequiredLength(errors, "bio", "Biography", request.Bio, BioMaxLength);
        if (!ValidGenders.Contains(request.Gender))
            errors["gender"] = ["Choose a recognized gender and pronoun option."];
        if (!ValidWaivers.Contains(request.Waiver))
            errors["waiver"] = ["Choose a recognized waiver option."];
        if (request.DayProgramCount is < 1 or > 100)
            errors["dayProgramCount"] = ["Day program count must be between 1 and 100."];

        OptionalLength(errors, "credibleClientId", "Credible client ID", request.CredibleClientId,
            CredibleClientIdMaxLength);
        OptionalLength(errors, "guardianName", "Guardian name", request.GuardianName, GuardianNameMaxLength);
        OptionalLength(errors, "phoneNumber", "Phone number", request.PhoneNumber, PhoneMaxLength);
        OptionalLength(errors, "email", "Email", request.Email, EmailMaxLength);
        if (!string.IsNullOrWhiteSpace(request.Email) &&
            !new EmailAddressAttribute().IsValid(request.Email))
        {
            errors["email"] = ["Enter a valid email address."];
        }

        OptionalLength(errors, "address", "Address", request.Address, AddressMaxLength);
        OptionalLength(errors, "billingStreet", "Billing street", request.BillingStreet, BillingStreetMaxLength);
        OptionalLength(errors, "billingCity", "Billing city", request.BillingCity, BillingCityMaxLength);
        OptionalLength(errors, "billingState", "Billing state", request.BillingState, BillingStateMaxLength);
        OptionalLength(errors, "billingZip", "Billing ZIP", request.BillingZip, BillingZipMaxLength);
        OptionalLength(
            errors,
            "primaryCareProvider",
            "Primary care provider",
            request.PrimaryCareProvider,
            PrimaryCareProviderMaxLength);
        OptionalLength(
            errors,
            "healthcareSystemName",
            "Healthcare system",
            request.HealthcareSystemName,
            HealthcareSystemMaxLength);
        OptionalLength(errors, "vrCounselorName", "VR counselor", request.VrCounselorName,
            VrStaffNameMaxLength);
        OptionalLength(errors, "vrAssistantName", "VR assistant", request.VrAssistantName,
            VrStaffNameMaxLength);

        foreach (var error in RepresentativePayeeRules.Validate(
                     request.CaseManagerIsRepPayee,
                     request.RepPayeeMonthlyIncome,
                     request.RepPayeeRegularCheckRequestNeeds))
        {
            errors[error.Key] = error.Value;
        }

        ValidateInitialForms(errors, request, today.Date, requireNewForms);
        return errors;
    }

    public static string Describe(IReadOnlyDictionary<string, string[]> errors) =>
        string.Join(" ", errors.Values.SelectMany(messages => messages).Distinct(StringComparer.Ordinal));

    private static void ValidateInitialForms(
        Dictionary<string, string[]> errors,
        SavePersonRequest request,
        DateTime today,
        bool requireNewForms)
    {
        var newForms = request.Forms.Where(form => form.Id == 0).ToList();
        if (!request.EffectiveDate.HasValue && newForms.Count > 0)
        {
            errors["forms"] = ["Compliance forms cannot be created without an effective date."];
            return;
        }

        if (!requireNewForms)
            return;

        if (newForms.Count != FormTypes.Count ||
            newForms.Select(form => form.Type).Distinct(StringComparer.Ordinal).Count() != FormTypes.Count ||
            newForms.Any(form => !ValidFormTypes.Contains(form.Type)))
        {
            errors["forms"] = ["A complete, non-duplicated initial form set is required."];
            return;
        }

        if (newForms.Any(form => form.IsCompliant != form.CompletedDate.HasValue))
        {
            errors["forms"] =
                ["Each completed form needs a completion date, and each incomplete form must leave that date blank."];
            return;
        }

        if (newForms.Any(form => form.CompletedDate?.Date > today))
        {
            errors["forms"] = ["A form completion date cannot be in the future."];
            return;
        }

        if (newForms.Any(form => form.OpenedDate?.Date > today))
            errors["forms"] = ["A form opened date cannot be in the future."];
    }

    private static void RequiredLength(
        Dictionary<string, string[]> errors,
        string key,
        string label,
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors[key] = [$"{label} is required."];
        else if (value.Trim().Length > maxLength)
            errors[key] = [$"{label} must not exceed {maxLength:N0} characters."];
    }

    private static void OptionalLength(
        Dictionary<string, string[]> errors,
        string key,
        string label,
        string? value,
        int maxLength)
    {
        if (value?.Trim().Length > maxLength)
            errors[key] = [$"{label} must not exceed {maxLength:N0} characters."];
    }
}

/// <summary>
/// Shared defaults and validation limits for the agency-configurable label used
/// for the staff member assisting the Vocational Rehabilitation counselor.
/// </summary>
public static class VocationalRehabilitationProfile
{
    public const string DefaultAssistantTitle = "VSA";
    public const int AssistantTitleMaxLength = 100;

    public static string NormalizeAssistantTitle(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DefaultAssistantTitle : value.Trim();
}
