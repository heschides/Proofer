using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Matching the free-text provider fields that predate the directory. The rule that matters
/// most is what this deliberately will <em>not</em> do: no fuzzy matching, and no resolution of
/// an ambiguous name. A wrong provider on a consumer's medical record is worse than an unlinked
/// one, because unlinked is visibly unfinished and wrong looks finished.
/// </summary>
public sealed class LegacyProviderLinkingTests
{
    [Fact]
    public void AnExactNameMatchesRegardlessOfCasingOrSurroundingSpace()
    {
        var match = LegacyProviderLinking.Match("  dr. REED ", Directory());

        Assert.Equal(LegacyMatchOutcome.Matched, match.Outcome);
        Assert.True(match.CanLink);
        Assert.Equal(4, match.ProviderId);
        Assert.Equal("Dr. Reed", match.ProviderName);
    }

    [Theory]
    [InlineData("Dr. Reedy")]
    [InlineData("Reed")]
    [InlineData("Dr Reed")]
    [InlineData("Dr. Reed, MD")]
    public void ANearMissDoesNotMatch(string legacyName)
    {
        // Every one of these is the kind of value edit-distance or prefix matching would
        // happily attach to Dr. Reed. None of them is the same statement of fact.
        var match = LegacyProviderLinking.Match(legacyName, Directory());

        Assert.Equal(LegacyMatchOutcome.NoMatch, match.Outcome);
        Assert.False(match.CanLink);
    }

    [Fact]
    public void ADuplicatedNameIsReportedAsAmbiguousRatherThanResolved()
    {
        // Directory names are unique per agency only by identifier, not by name, so two
        // "Dr. Reed" rows are possible. Picking one would silently attach a consumer to
        // whichever happened to sort first.
        var directory = Directory().Append(
            new ProviderAffiliationNode(9, "Dr. Reed", null, MedicalProviderKind.Individual)).ToList();

        var match = LegacyProviderLinking.Match("Dr. Reed", directory);

        Assert.Equal(LegacyMatchOutcome.Ambiguous, match.Outcome);
        Assert.False(match.CanLink);
        Assert.Equal(2, match.CandidateCount);
        Assert.Equal(0, match.ProviderId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoTypedValueIsNotAGap(string? legacyName)
    {
        // Most consumers legitimately have nothing recorded. That must not read as unfinished
        // work, or every profile grows a prompt nobody can clear.
        var match = LegacyProviderLinking.Match(legacyName, Directory());

        Assert.Equal(LegacyMatchOutcome.NoLegacyValue, match.Outcome);
        Assert.Equal(string.Empty, LegacyProviderLinking.PrimaryCareGuidance(match));
    }

    [Fact]
    public void AHealthcareSystemNameOnlyMatchesANetwork()
    {
        // "MaineHealth" as a system name must not attach to a clinician or practice that
        // happens to share the name.
        var directory = Directory().Append(
            new ProviderAffiliationNode(9, "MaineHealth", null, MedicalProviderKind.Individual)).ToList();

        var restricted = LegacyProviderLinking.Match("MaineHealth", directory, MedicalProviderKind.Network);
        var unrestricted = LegacyProviderLinking.Match("MaineHealth", directory);

        Assert.Equal(LegacyMatchOutcome.Matched, restricted.Outcome);
        Assert.Equal(1, restricted.ProviderId);
        // Without the tier restriction the same name is genuinely ambiguous.
        Assert.Equal(LegacyMatchOutcome.Ambiguous, unrestricted.Outcome);
    }

    [Fact]
    public void EachOutcomeNamesTheNextAction()
    {
        var matched = LegacyProviderLinking.PrimaryCareGuidance(
            LegacyProviderLinking.Match("Dr. Reed", Directory()));
        var missing = LegacyProviderLinking.PrimaryCareGuidance(
            LegacyProviderLinking.Match("Dr. Nobody", Directory()));
        var ambiguous = LegacyProviderLinking.PrimaryCareGuidance(
            new LegacyProviderMatch(LegacyMatchOutcome.Ambiguous, 0, "Dr. Reed", 2));

        // "Not linked" on its own is a state, not a task.
        Assert.Contains("Link it", matched);
        Assert.Contains("Add them to the provider directory", missing);
        Assert.Contains("Merge the duplicates", ambiguous);
    }

    [Fact]
    public void AHealthcareSystemThatAgreesWithTheDerivedNetworkSaysNothing()
    {
        Assert.Equal(
            string.Empty,
            LegacyProviderLinking.HealthcareSystemGuidance("MaineHealth", "mainehealth"));
    }

    [Fact]
    public void AHealthcareSystemThatDisagreesIsSurfacedRatherThanSilentlyOverridden()
    {
        // One of the two is stale and only a person knows which.
        var guidance = LegacyProviderLinking.HealthcareSystemGuidance("MaineHealth", "InterMed");

        Assert.Contains("MaineHealth", guidance);
        Assert.Contains("InterMed", guidance);
        Assert.Contains("Check which is current", guidance);
    }

    [Fact]
    public void AHealthcareSystemWithNoLinkedProviderExplainsWhatWillHappen()
    {
        var guidance = LegacyProviderLinking.HealthcareSystemGuidance("MaineHealth", null);

        Assert.Contains("Once a provider is linked", guidance);
    }

    [Fact]
    public void NoTypedSystemNameSaysNothing()
    {
        Assert.Equal(string.Empty, LegacyProviderLinking.HealthcareSystemGuidance(null, "InterMed"));
        Assert.Equal(string.Empty, LegacyProviderLinking.HealthcareSystemGuidance("  ", "InterMed"));
    }

    private static List<ProviderAffiliationNode> Directory() =>
    [
        new(1, "MaineHealth", null, MedicalProviderKind.Network),
        new(3, "Coastal Women's Healthcare", 1, MedicalProviderKind.Practice),
        new(4, "Dr. Reed", 3, MedicalProviderKind.Individual)
    ];
}
