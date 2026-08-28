using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// What a consumer's provider list will accept, without a database. Both the desktop
/// service and the API apply these, so the two cannot disagree about what "current" means
/// or which link wins the primary-care slot.
/// </summary>
public sealed class ConsumerProviderRulesTests
{
    [Fact]
    public void ALinkWithNoEndDateIsCurrent()
    {
        Assert.True(ConsumerProviderRules.IsCurrent(null));
        Assert.False(ConsumerProviderRules.IsCurrent(new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void AFutureEndDateStillEndsTheRelationship()
    {
        // A transfer recorded ahead of time is a real workflow. "Current" means no end date
        // at all, not an end date that has not yet arrived — anything else would need a
        // clock, and a rule that depends on when it is asked cannot be enforced twice
        // identically.
        Assert.False(ConsumerProviderRules.IsCurrent(new DateTime(2099, 1, 1)));
    }

    [Fact]
    public void AValidRequestPasses()
    {
        Assert.Empty(ConsumerProviderRules.Validate(Request()));
    }

    [Fact]
    public void AProviderMustBeChosen()
    {
        var errors = ConsumerProviderRules.Validate(Request() with { ProviderId = 0 });

        Assert.Contains("providerId", errors.Keys);
    }

    [Fact]
    public void AnOverlongRoleIsRejected()
    {
        var errors = ConsumerProviderRules.Validate(
            Request() with { Role = new string('x', ConsumerProviderRules.MaxRoleLength + 1) });

        Assert.Contains("role", errors.Keys);
    }

    [Fact]
    public void ARoleAtTheLimitIsAccepted()
    {
        var errors = ConsumerProviderRules.Validate(
            Request() with { Role = new string('x', ConsumerProviderRules.MaxRoleLength) });

        Assert.Empty(errors);
    }

    [Fact]
    public void AnEndDateBeforeTheStartDateIsRejected()
    {
        var errors = ConsumerProviderRules.Validate(Request() with
        {
            StartDate = new DateTime(2026, 5, 1),
            EndDate = new DateTime(2026, 4, 1)
        });

        Assert.Contains("endDate", errors.Keys);
    }

    [Fact]
    public void ARelationshipThatStartedAndEndedOnTheSameDayIsAccepted()
    {
        // One appointment and no return is a real record, not a typo.
        var errors = ConsumerProviderRules.Validate(Request() with
        {
            StartDate = new DateTime(2026, 5, 1),
            EndDate = new DateTime(2026, 5, 1)
        });

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ConsumerProviderRules.MaxSortOrder + 1)]
    public void AnOutOfRangeDisplayOrderIsRejected(int sortOrder)
    {
        var errors = ConsumerProviderRules.Validate(Request() with { SortOrder = sortOrder });

        Assert.Contains("sortOrder", errors.Keys);
    }

    [Fact]
    public void TheListReadsPrimaryCareFirstThenTheCaseManagersOrderThenName()
    {
        var rows = new[]
        {
            ("Zylstra", 0, false),
            ("Abbott", 2, false),
            ("Baker", 1, false),
            ("Primary", 9, true)
        };

        var ordered = ConsumerProviderRules.OrderForDisplay(
            rows,
            row => row.Item3,
            row => row.Item2,
            row => row.Item1).Select(row => row.Item1).ToArray();

        // Primary care jumps the queue despite the highest sort order; the rest follow the
        // case manager's ordering rather than the alphabet.
        Assert.Equal(new[] { "Primary", "Zylstra", "Baker", "Abbott" }, ordered);
    }

    [Fact]
    public void EqualOrderFallsBackToNameSoTheListDoesNotShuffleBetweenLoads()
    {
        var rows = new[] { ("Coastal", 0, false), ("Abbott", 0, false) };

        var ordered = ConsumerProviderRules.OrderForDisplay(
            rows, row => row.Item3, row => row.Item2, row => row.Item1)
            .Select(row => row.Item1).ToArray();

        Assert.Equal(new[] { "Abbott", "Coastal" }, ordered);
    }

    [Fact]
    public void TheRefusalMessagesNameWhatIsInTheWay()
    {
        // "There is already one" without saying which cannot be acted on.
        Assert.Contains("Dr. Reed", ConsumerProviderRules.PrimaryCareConflictMessage("Dr. Reed"));
        Assert.Contains("Dr. Reed", ConsumerProviderRules.DuplicateCurrentLinkMessage("Dr. Reed"));
        Assert.Contains(
            ConsumerProviderRules.MaxProvidersPerConsumer.ToString(),
            ConsumerProviderRules.TooManyProvidersMessage());
    }

    [Fact]
    public void TheLimitIsDescribedAsASafetyGuardRatherThanAClinicalRule()
    {
        // The number is deliberately not a product decision about how many providers a
        // person may have, and the message has to say so or it will be read as one.
        Assert.Contains("safety limit", ConsumerProviderRules.TooManyProvidersMessage());
    }

    private static SaveConsumerProviderRequest Request() =>
        new(ProviderId: 5, Role: "Neurologist", IsPrimaryCare: false,
            StartDate: new DateTime(2026, 1, 1), EndDate: null,
            HasActiveRelease: true, SortOrder: 0);
}
