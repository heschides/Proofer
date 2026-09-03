namespace Sati.Contracts.V1;

/// <summary>Whether one disclosure or repeated disclosures are authorized.</summary>
public enum AgencyReleaseScope
{
    OneTime,
    Multiple,
}

/// <summary>
/// Stable identifiers for the record categories offered by the agency release.
/// Display wording is centralized so the desktop, API validation, and PDF cannot
/// silently describe the same selection differently.
/// </summary>
public static class AgencyReleaseInformation
{
    public const string IntakeAssessment = "IntakeAssessment";
    public const string TreatmentPlan = "TreatmentPlan";
    public const string Evaluations = "Evaluations";
    public const string Observations = "Observations";
    public const string Diagnosis = "Diagnosis";
    public const string DischargeSummary = "DischargeSummary";
    public const string ProgressDailyNotes = "ProgressDailyNotes";
    public const string OngoingServiceProvision = "OngoingServiceProvision";
    public const string Other = "Other";

    public static IReadOnlyList<string> All { get; } =
    [
        IntakeAssessment,
        TreatmentPlan,
        Evaluations,
        Observations,
        Diagnosis,
        DischargeSummary,
        ProgressDailyNotes,
        OngoingServiceProvision,
        Other,
    ];

    public static string DisplayName(string value) => value switch
    {
        IntakeAssessment => "Intake assessment",
        TreatmentPlan => "Treatment plan",
        Evaluations => "Evaluations",
        Observations => "Observations",
        Diagnosis => "Diagnosis",
        DischargeSummary => "Discharge summary",
        ProgressDailyNotes => "Progress / daily notes",
        OngoingServiceProvision => "Ongoing service provision",
        Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown agency-release information category."),
    };
}

/// <summary>
/// Consumer-directed choices and recipient details for one agency release. The
/// consumer, case manager, and agency identities are derived from the authorized
/// route/session and never accepted from the caller.
/// </summary>
public sealed record AgencyReleaseRequest(
    bool? AuthorizationGranted,
    string? ContactType,
    string? ContactName,
    string? Relationship,
    string? ContactAddress,
    string? ContactCity,
    string? ContactState,
    string? ContactFax,
    string? ContactPhone,
    string? ContactEmail,
    IReadOnlyList<string>? InformationCategories,
    string? OtherInformation,
    DateOnly? StartDate,
    DateOnly? ExpirationDate,
    string? Scope,
    bool? IncludeDrugAlcohol,
    bool? IncludeMentalHealth,
    bool? IncludeHivAids,
    bool? ReleaseWithoutReview,
    bool IsRevocation = false,
    DateOnly? RevokedOn = null,
    bool ConfirmedObtainedRoi = false,
    bool IsDraft = false);

public sealed record AgencyReleaseResult(byte[] Pdf, string FileName);

/// <summary>
/// The single validation owner used before either local or API generation. This is
/// document validity, not mere UI enablement, so it lives in the shared contract.
/// </summary>
public static class AgencyReleaseRules
{
    public const int MaxTextLength = 300;

    public const string StaffAttestation =
        "I attest that I obtained the consumer's authorization represented by these selections and that I am preparing this release at the consumer's direction.";

    public const string AttestationScopeNotice =
        "This records the authenticated Sati user and generation time. It is not the consumer's electronic signature and does not replace any signature required by agency policy or law.";

    public static IReadOnlyDictionary<string, string[]> Validate(AgencyReleaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IsDraft)
            return ValidateDraft(request);
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var values))
                errors[field] = values = [];
            values.Add(message);
        }

        void Required(string field, string? value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                Add(field, $"{label} is required.");
            else if (value.Trim().Length > MaxTextLength)
                Add(field, $"{label} must be {MaxTextLength} characters or fewer.");
        }

        void OptionalLength(string field, string? value, string label)
        {
            if (value?.Trim().Length > MaxTextLength)
                Add(field, $"{label} must be {MaxTextLength} characters or fewer.");
        }

        if (request.AuthorizationGranted is null)
            Add(nameof(request.AuthorizationGranted), "Choose whether authorization was granted.");
        Required(nameof(request.ContactType), request.ContactType, "Contact type");
        Required(nameof(request.ContactName), request.ContactName, "Contact name");
        Required(nameof(request.ContactAddress), request.ContactAddress, "Contact address");
        Required(nameof(request.ContactPhone), request.ContactPhone, "Contact phone");
        OptionalLength(nameof(request.Relationship), request.Relationship, "Relationship");
        OptionalLength(nameof(request.ContactCity), request.ContactCity, "Contact city");
        OptionalLength(nameof(request.ContactState), request.ContactState, "Contact state");
        OptionalLength(nameof(request.ContactFax), request.ContactFax, "Contact fax");
        OptionalLength(nameof(request.ContactEmail), request.ContactEmail, "Contact email");
        OptionalLength(nameof(request.OtherInformation), request.OtherInformation, "Other information");

        var categories = request.InformationCategories?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList() ?? [];
        if (categories.Count == 0)
            Add(nameof(request.InformationCategories), "Choose at least one information category.");
        foreach (var category in categories)
        {
            if (!AgencyReleaseInformation.All.Contains(category, StringComparer.Ordinal))
                Add(nameof(request.InformationCategories), $"'{category}' is not a recognized information category.");
        }
        if (categories.Count != categories.Distinct(StringComparer.Ordinal).Count())
            Add(nameof(request.InformationCategories), "An information category cannot be selected more than once.");
        if (categories.Contains(AgencyReleaseInformation.Other, StringComparer.Ordinal) &&
            string.IsNullOrWhiteSpace(request.OtherInformation))
        {
            Add(nameof(request.OtherInformation), "Describe the other information to disclose or obtain.");
        }

        if (request.StartDate is null)
            Add(nameof(request.StartDate), "Start date is required.");
        if (request.ExpirationDate is null)
            Add(nameof(request.ExpirationDate), "Expiration date is required.");
        if (request.StartDate is DateOnly start && request.ExpirationDate is DateOnly expiration)
        {
            if (expiration < start)
                Add(nameof(request.ExpirationDate), "Expiration date cannot be before the start date.");

            if (Enum.TryParse<AgencyReleaseScope>(request.Scope, out var parsedScope))
            {
                var latest = parsedScope == AgencyReleaseScope.OneTime
                    ? start.AddDays(90)
                    : start.AddYears(1);
                if (expiration > latest)
                    Add(nameof(request.ExpirationDate), parsedScope == AgencyReleaseScope.OneTime
                        ? "A one-time disclosure cannot remain active longer than 90 days."
                        : "Multiple disclosures cannot remain active longer than one year.");
            }
        }

        if (!Enum.TryParse<AgencyReleaseScope>(request.Scope, out _))
            Add(nameof(request.Scope), "Choose one-time or multiple disclosures.");
        if (request.IncludeDrugAlcohol is null)
            Add(nameof(request.IncludeDrugAlcohol), "Choose whether drug/alcohol information is included.");
        if (request.IncludeMentalHealth is null)
            Add(nameof(request.IncludeMentalHealth), "Choose whether mental-health information is included.");
        if (request.IncludeHivAids is null)
            Add(nameof(request.IncludeHivAids), "Choose whether HIV/AIDS information is included.");
        if (request.ReleaseWithoutReview is null)
            Add(nameof(request.ReleaseWithoutReview), "Choose whether information may be released without prior review.");
        if (request.IsRevocation && request.RevokedOn is null)
            Add(nameof(request.RevokedOn), "A revocation date is required when revoking the authorization.");
        if (request.ConfirmedObtainedRoi && request.AuthorizationGranted != true)
            Add(nameof(request.ConfirmedObtainedRoi), "An authorization that was not granted cannot be recorded as obtained.");

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string[]> ValidateDraft(AgencyReleaseRequest request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var values))
                errors[field] = values = [];
            values.Add(message);
        }
        void CheckLength(string field, string? value, string label)
        {
            if (value?.Trim().Length > MaxTextLength)
                Add(field, $"{label} must be {MaxTextLength} characters or fewer.");
        }

        CheckLength(nameof(request.ContactType), request.ContactType, "Contact type");
        CheckLength(nameof(request.ContactName), request.ContactName, "Contact name");
        CheckLength(nameof(request.Relationship), request.Relationship, "Relationship");
        CheckLength(nameof(request.ContactAddress), request.ContactAddress, "Contact address");
        CheckLength(nameof(request.ContactCity), request.ContactCity, "Contact city");
        CheckLength(nameof(request.ContactState), request.ContactState, "Contact state");
        CheckLength(nameof(request.ContactFax), request.ContactFax, "Contact fax");
        CheckLength(nameof(request.ContactPhone), request.ContactPhone, "Contact phone");
        CheckLength(nameof(request.ContactEmail), request.ContactEmail, "Contact email");
        CheckLength(nameof(request.OtherInformation), request.OtherInformation, "Other information");

        var categories = request.InformationCategories?
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToList() ?? [];
        foreach (var category in categories.Where(category =>
                     !AgencyReleaseInformation.All.Contains(category, StringComparer.Ordinal)))
            Add(nameof(request.InformationCategories), $"'{category}' is not a recognized information category.");
        if (categories.Count != categories.Distinct(StringComparer.Ordinal).Count())
            Add(nameof(request.InformationCategories), "An information category cannot be selected more than once.");

        if (request.StartDate is DateOnly start && request.ExpirationDate is DateOnly expiration && expiration < start)
            Add(nameof(request.ExpirationDate), "Expiration date cannot be before the start date.");
        if (!string.IsNullOrWhiteSpace(request.Scope) &&
            !Enum.TryParse<AgencyReleaseScope>(request.Scope, out _))
            Add(nameof(request.Scope), "Choose one-time or multiple disclosures.");

        if (request.IncludeDrugAlcohol == true || request.IncludeMentalHealth == true || request.IncludeHivAids == true)
            Add("SensitiveConsent", "A draft cannot record sensitive-information consent.");
        if (request.ConfirmedObtainedRoi)
            Add(nameof(request.ConfirmedObtainedRoi), "A draft cannot include a staff attestation that authorization was obtained.");

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    public static void EnsureValid(AgencyReleaseRequest request)
    {
        var errors = Validate(request);
        if (errors.Count == 0)
            return;

        throw new InvalidOperationException(string.Join(" ", errors.Values.SelectMany(value => value)));
    }
}
