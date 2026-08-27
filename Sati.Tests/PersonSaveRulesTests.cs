using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class PersonSaveRulesTests
{
    private static readonly DateTime Today = new(2026, 8, 27);

    [Fact]
    public void ValidClientWithoutEffectiveDatePassesSharedValidation()
    {
        var errors = PersonSaveRules.Validate(ValidRequest(), Today, requireNewForms: false);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmailIsOptionalWhenNoAddressWasProvided(string? email)
    {
        var errors = PersonSaveRules.Validate(
            ValidRequest() with { Email = email },
            Today,
            requireNewForms: false);

        Assert.DoesNotContain("email", errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("first-empty", "firstName")]
    [InlineData("first-long", "firstName")]
    [InlineData("last-empty", "lastName")]
    [InlineData("last-long", "lastName")]
    [InlineData("birth-old", "birthDate")]
    [InlineData("birth-future", "birthDate")]
    [InlineData("bio-empty", "bio")]
    [InlineData("bio-long", "bio")]
    [InlineData("gender", "gender")]
    [InlineData("waiver", "waiver")]
    [InlineData("day-low", "dayProgramCount")]
    [InlineData("day-high", "dayProgramCount")]
    [InlineData("guardian", "guardianName")]
    [InlineData("phone", "phoneNumber")]
    [InlineData("email-long", "email")]
    [InlineData("email-format", "email")]
    [InlineData("address", "address")]
    [InlineData("billing-street", "billingStreet")]
    [InlineData("billing-city", "billingCity")]
    [InlineData("billing-state", "billingState")]
    [InlineData("billing-zip", "billingZip")]
    [InlineData("pcp", "primaryCareProvider")]
    [InlineData("health-system", "healthcareSystemName")]
    public void InvalidClientFieldIsIdentifiedPrecisely(string scenario, string expectedKey)
    {
        var request = scenario switch
        {
            "first-empty" => ValidRequest() with { FirstName = " " },
            "first-long" => ValidRequest() with { FirstName = new string('a', 51) },
            "last-empty" => ValidRequest() with { LastName = " " },
            "last-long" => ValidRequest() with { LastName = new string('a', 51) },
            "birth-old" => ValidRequest() with { BirthDate = new DateTime(1899, 12, 31) },
            "birth-future" => ValidRequest() with { BirthDate = Today.AddDays(1) },
            "bio-empty" => ValidRequest() with { Bio = " " },
            "bio-long" => ValidRequest() with { Bio = new string('a', PersonSaveRules.BioMaxLength + 1) },
            "gender" => ValidRequest() with { Gender = "Unrecognized" },
            "waiver" => ValidRequest() with { Waiver = "Unrecognized" },
            "day-low" => ValidRequest() with { DayProgramCount = 0 },
            "day-high" => ValidRequest() with { DayProgramCount = 101 },
            "guardian" => ValidRequest() with { GuardianName = new string('a', 101) },
            "phone" => ValidRequest() with { PhoneNumber = new string('1', 21) },
            "email-long" => ValidRequest() with { Email = $"a@{new string('x', 253)}" },
            "email-format" => ValidRequest() with { Email = "not-an-email" },
            "address" => ValidRequest() with { Address = new string('a', 251) },
            "billing-street" => ValidRequest() with { BillingStreet = new string('a', 56) },
            "billing-city" => ValidRequest() with { BillingCity = new string('a', 31) },
            "billing-state" => ValidRequest() with { BillingState = "MEE" },
            "billing-zip" => ValidRequest() with { BillingZip = new string('1', 16) },
            "pcp" => ValidRequest() with { PrimaryCareProvider = new string('a', 101) },
            "health-system" => ValidRequest() with { HealthcareSystemName = new string('a', 101) },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var errors = PersonSaveRules.Validate(request, Today, requireNewForms: false);

        Assert.Contains(expectedKey, errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepresentativePayeeRequirementsAreIncludedInPersonValidation()
    {
        var errors = PersonSaveRules.Validate(
            ValidRequest() with
            {
                CaseManagerIsRepPayee = true,
                RepPayeeMonthlyIncome = null,
                RepPayeeRegularCheckRequestNeeds = " "
            },
            Today,
            requireNewForms: false);

        Assert.Contains("repPayeeMonthlyIncome", errors.Keys);
        Assert.Contains("repPayeeRegularCheckRequestNeeds", errors.Keys);
    }

    [Fact]
    public void CompleteUniqueInitialFormSetPasses()
    {
        var request = ValidRequest() with
        {
            EffectiveDate = Today.AddMonths(-2),
            Waiver = "Section21",
            Forms = InitialForms()
        };

        var errors = PersonSaveRules.Validate(request, Today, requireNewForms: true);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("checked-without-date")]
    [InlineData("unchecked-with-date")]
    [InlineData("future-completion")]
    [InlineData("future-opened")]
    public void InvalidInitialFormGraphIsRejected(string scenario)
    {
        var forms = InitialForms().ToList();
        forms = scenario switch
        {
            "missing" => forms.Skip(1).ToList(),
            "duplicate" => forms.Select((form, index) => index == 1
                ? form with { Type = PersonSaveRules.FormTypes[0] }
                : form).ToList(),
            "unknown" => forms.Select((form, index) => index == 0
                ? form with { Type = "UnknownForm" }
                : form).ToList(),
            "checked-without-date" => forms.Select((form, index) => index == 0
                ? form with { IsCompliant = true, CompletedDate = null }
                : form).ToList(),
            "unchecked-with-date" => forms.Select((form, index) => index == 0
                ? form with { IsCompliant = false, CompletedDate = Today }
                : form).ToList(),
            "future-completion" => forms.Select((form, index) => index == 0
                ? form with { IsCompliant = true, CompletedDate = Today.AddDays(1) }
                : form).ToList(),
            "future-opened" => forms.Select((form, index) => index == 0
                ? form with { OpenedDate = Today.AddDays(1) }
                : form).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var request = ValidRequest() with
        {
            EffectiveDate = Today.AddMonths(-2),
            Waiver = "Section21",
            Forms = forms
        };

        var errors = PersonSaveRules.Validate(request, Today, requireNewForms: true);

        Assert.Contains("forms", errors.Keys);
    }

    [Fact]
    public void FormsWithoutEffectiveDateAreRejectedInsteadOfSilentlyDiscarded()
    {
        var errors = PersonSaveRules.Validate(
            ValidRequest() with { Forms = InitialForms() },
            Today,
            requireNewForms: false);

        Assert.Contains("forms", errors.Keys);
    }

    private static IReadOnlyList<SavePersonFormRequest> InitialForms() =>
        PersonSaveRules.FormTypes
            .Select(type => new SavePersonFormRequest(0, type, false, null, null))
            .ToList();

    internal static SavePersonRequest ValidRequest() => new(
        "Jamie",
        "River",
        new DateTime(1990, 4, 3),
        "Unknown",
        null,
        "A valid client record.",
        "None",
        null,
        null,
        null,
        null,
        false,
        false,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        false,
        false,
        false,
        false,
        false,
        false,
        1,
        false,
        false,
        false,
        [],
        0,
        true,
        false,
        null,
        null,
        false,
        false,
        "jamie@example.test");
}
