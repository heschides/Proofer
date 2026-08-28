using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The affiliation rule itself, exercised without a database. Both the transitional
/// desktop service and the API call these methods, so a disagreement between the two
/// would have to be a disagreement here first.
/// </summary>
public sealed class ProviderAffiliationTests
{
    // ── Tier rule ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MedicalProviderKind.Practice, true)]
    [InlineData(MedicalProviderKind.Network, true)]
    [InlineData(MedicalProviderKind.Individual, false)]
    public void AnIndividualBelongsToAPracticeOrANetworkButNeverToAnotherIndividual(
        MedicalProviderKind parentKind, bool allowed)
    {
        // Individual under Individual is a supervision relationship, not an affiliation.
        Assert.Equal(allowed, ProviderAffiliation.CanParent(MedicalProviderKind.Individual, parentKind));
    }

    [Theory]
    [InlineData(MedicalProviderKind.Network, true)]
    [InlineData(MedicalProviderKind.Practice, false)]
    [InlineData(MedicalProviderKind.Individual, false)]
    public void APracticeBelongsOnlyToANetwork(MedicalProviderKind parentKind, bool allowed)
    {
        Assert.Equal(allowed, ProviderAffiliation.CanParent(MedicalProviderKind.Practice, parentKind));
    }

    [Theory]
    [InlineData(MedicalProviderKind.Network, true)]
    [InlineData(MedicalProviderKind.Practice, false)]
    [InlineData(MedicalProviderKind.Individual, false)]
    public void ANetworkBelongsOnlyToAnotherNetwork(MedicalProviderKind parentKind, bool allowed)
    {
        // Network to Network is what carries MaineHealth owning Maine Medical Partners
        // owning practices without inventing a fourth tier name.
        Assert.Equal(allowed, ProviderAffiliation.CanParent(MedicalProviderKind.Network, parentKind));
    }

    [Fact]
    public void TheTierRefusalNamesBothTheProposedParentAndWhatWouldBeAllowed()
    {
        var directory = new[]
        {
            Node(1, "Dr. Reed", MedicalProviderKind.Individual),
            Node(2, "Coastal Women's Healthcare", MedicalProviderKind.Practice)
        };

        var reason = ProviderAffiliation.ValidateParent(
            2, MedicalProviderKind.Practice, 1, directory);

        Assert.NotNull(reason);
        Assert.Contains("A practice", reason);
        Assert.Contains("a network", reason);
        Assert.Contains("Dr. Reed", reason);
    }

    // ── Loops and self-reference ─────────────────────────────────────────────

    [Fact]
    public void AProviderCannotBeAffiliatedWithItself()
    {
        var directory = new[] { Node(7, "MaineHealth", MedicalProviderKind.Network) };

        var reason = ProviderAffiliation.ValidateParent(
            7, MedicalProviderKind.Network, 7, directory);

        Assert.Equal("A provider cannot be affiliated with itself.", reason);
    }

    [Fact]
    public void ADirectLoopIsRefused()
    {
        // Upper already belongs to Lower; making Lower belong to Upper closes the loop.
        var directory = new[]
        {
            Node(1, "Upper", MedicalProviderKind.Network, parent: 2),
            Node(2, "Lower", MedicalProviderKind.Network)
        };

        var reason = ProviderAffiliation.ValidateParent(
            2, MedicalProviderKind.Network, 1, directory);

        Assert.NotNull(reason);
        Assert.Contains("already sits beneath this entry", reason);
    }

    [Fact]
    public void AnIndirectLoopThroughAnIntermediateEntryIsRefused()
    {
        // N1 → N2 → N3. Pointing N3 at N1 would close a three-link loop, which a
        // self-comparison alone would not catch.
        var directory = new[]
        {
            Node(1, "N1", MedicalProviderKind.Network, parent: 2),
            Node(2, "N2", MedicalProviderKind.Network, parent: 3),
            Node(3, "N3", MedicalProviderKind.Network)
        };

        var reason = ProviderAffiliation.ValidateParent(
            3, MedicalProviderKind.Network, 1, directory);

        Assert.NotNull(reason);
        Assert.Contains("already sits beneath this entry", reason);
    }

    // ── Depth ────────────────────────────────────────────────────────────────

    [Fact]
    public void AChainAtTheDepthLimitStillAcceptsANewEntry()
    {
        var directory = NetworkChain(ProviderAffiliation.MaxDepth);

        var reason = ProviderAffiliation.ValidateParent(
            0, MedicalProviderKind.Individual, ProviderAffiliation.MaxDepth, directory);

        Assert.Null(reason);
    }

    [Fact]
    public void AChainBeyondTheDepthLimitIsRefused()
    {
        var directory = NetworkChain(ProviderAffiliation.MaxDepth + 1);

        var reason = ProviderAffiliation.ValidateParent(
            0, MedicalProviderKind.Individual, ProviderAffiliation.MaxDepth + 1, directory);

        Assert.NotNull(reason);
        Assert.Contains($"limited to {ProviderAffiliation.MaxDepth} levels", reason);
    }

    // ── Scope ────────────────────────────────────────────────────────────────

    [Fact]
    public void AParentThatIsNotInTheSuppliedDirectoryIsRefused()
    {
        // The caller scopes the directory to one agency, so an id belonging to another
        // tenant arrives here as an id that simply is not present.
        var directory = new[] { Node(1, "MaineHealth", MedicalProviderKind.Network) };

        var reason = ProviderAffiliation.ValidateParent(
            0, MedicalProviderKind.Practice, 999, directory);

        Assert.Equal("That parent organization is not in this agency's provider directory.", reason);
    }

    [Fact]
    public void AZeroParentIdIsRefusedAsMissingRatherThanMatchingAnEmptyEntry()
    {
        var directory = new[] { Node(1, "MaineHealth", MedicalProviderKind.Network) };

        var reason = ProviderAffiliation.ValidateParent(
            0, MedicalProviderKind.Practice, 0, directory);

        Assert.Equal("That parent organization is not in this agency's provider directory.", reason);
    }

    [Fact]
    public void ANonMedicalParentIsRefusedByName()
    {
        var directory = new[] { Node(1, "Spurwink", null) };

        var reason = ProviderAffiliation.ValidateParent(
            0, MedicalProviderKind.Individual, 1, directory);

        Assert.NotNull(reason);
        Assert.Contains("Spurwink", reason);
        Assert.Contains("not a medical provider", reason);
    }

    // ── Designation ──────────────────────────────────────────────────────────

    [Fact]
    public void AMedicalEntryMustCarryADesignation()
    {
        var reason = ProviderAffiliation.ValidateKind(isHealthcare: true, kind: null);

        Assert.NotNull(reason);
        Assert.Contains("individual, a practice, or a network", reason);
    }

    [Fact]
    public void ANonMedicalEntryMayNotCarryADesignation()
    {
        var reason = ProviderAffiliation.ValidateKind(
            isHealthcare: false, kind: MedicalProviderKind.Practice);

        Assert.NotNull(reason);
        Assert.Contains("Only medical providers", reason);
    }

    [Fact]
    public void ANonMedicalEntryMayNotCarryAParent()
    {
        // The column is general so waiver can use it later, but no tier rule exists to
        // check a waiver entry against yet, and an unvalidated hierarchy is worse than none.
        var directory = new[] { Node(1, "MaineHealth", MedicalProviderKind.Network) };

        var reason = ProviderAffiliation.ValidateParent(0, null, 1, directory);

        Assert.Equal("Only medical providers can be affiliated with a parent organization.", reason);
    }

    [Fact]
    public void NoParentIsAlwaysValid()
    {
        // Standing alone is a legitimate state — an independent dentist, a network at the
        // top — not missing data.
        Assert.Null(ProviderAffiliation.ValidateParent(
            0, MedicalProviderKind.Individual, null, Array.Empty<ProviderAffiliationNode>()));
    }

    // ── Resolution ───────────────────────────────────────────────────────────

    [Fact]
    public void TheChainResolvesAcrossFourLevels()
    {
        var directory = FourLevelDirectory();

        var ancestors = ProviderAffiliation.ResolveAncestors(4, directory);

        Assert.Equal(
            new[] { "Coastal Women's Healthcare", "Maine Medical Partners", "MaineHealth" },
            ancestors.Select(node => node.Name).ToArray());
    }

    [Fact]
    public void TheNearestAncestorOfEachKindIsFound()
    {
        var directory = FourLevelDirectory();

        var practice = ProviderAffiliation.NearestAncestorOfKind(4, MedicalProviderKind.Practice, directory);
        var network = ProviderAffiliation.NearestAncestorOfKind(4, MedicalProviderKind.Network, directory);

        // The nearest network is the group directly above the practice, not the top of
        // the tree — which is the answer a consumer profile wants.
        Assert.Equal("Coastal Women's Healthcare", practice?.Name);
        Assert.Equal("Maine Medical Partners", network?.Name);
    }

    [Fact]
    public void AnUnaffiliatedEntryResolvesToAnEmptyChain()
    {
        var directory = new[] { Node(1, "Independent Dentistry", MedicalProviderKind.Individual) };

        Assert.Empty(ProviderAffiliation.ResolveAncestors(1, directory));
        Assert.Equal(string.Empty, ProviderAffiliation.DescribeAffiliation(1, directory));
    }

    [Fact]
    public void TheAffiliationDescriptionReadsNearestFirst()
    {
        var directory = FourLevelDirectory();

        Assert.Equal(
            "Coastal Women's Healthcare · Maine Medical Partners · MaineHealth",
            ProviderAffiliation.DescribeAffiliation(4, directory));
    }

    [Fact]
    public void ResolvingTerminatesOnDataThatAlreadyContainsALoop()
    {
        // Validation refuses to create one, but a hand-edited row or a restored backup
        // could still hold a loop, and a reader must not spin on it.
        var directory = new[]
        {
            Node(1, "A", MedicalProviderKind.Network, parent: 2),
            Node(2, "B", MedicalProviderKind.Network, parent: 1)
        };

        var ancestors = ProviderAffiliation.ResolveAncestors(1, directory);

        Assert.Equal(new[] { "B" }, ancestors.Select(node => node.Name).ToArray());
    }

    [Fact]
    public void ResolvingIsBoundedByTheDepthLimitEvenOnALongerStoredChain()
    {
        var directory = NetworkChain(ProviderAffiliation.MaxDepth + 5);

        var ancestors = ProviderAffiliation.ResolveAncestors(
            ProviderAffiliation.MaxDepth + 5, directory);

        Assert.Equal(ProviderAffiliation.MaxDepth, ancestors.Count);
    }

    // ── Picker filter ────────────────────────────────────────────────────────

    [Fact]
    public void TheSelectableParentFilterAgreesWithValidation()
    {
        var directory = FourLevelDirectory();

        // An individual may sit under the practice or either network, but not under
        // another individual, and the picker must not offer what a save would refuse.
        Assert.True(ProviderAffiliation.IsSelectableParent(0, MedicalProviderKind.Individual, 3, directory));
        Assert.True(ProviderAffiliation.IsSelectableParent(0, MedicalProviderKind.Individual, 1, directory));
        Assert.False(ProviderAffiliation.IsSelectableParent(0, MedicalProviderKind.Practice, 3, directory));
        Assert.False(ProviderAffiliation.IsSelectableParent(4, MedicalProviderKind.Individual, 4, directory));
    }

    // ── Delete refusal ───────────────────────────────────────────────────────

    [Fact]
    public void TheDeleteRefusalReadsSingularForOneAffiliatedEntry()
    {
        var message = ProviderAffiliation.AffiliatedChildrenMessage("MaineHealth", ["Coastal Women's Healthcare"]);

        Assert.Contains("1 entry is affiliated", message);
        Assert.Contains("Coastal Women's Healthcare", message);
    }

    [Fact]
    public void TheDeleteRefusalSummarisesALongListRatherThanNamingEveryEntry()
    {
        var children = Enumerable.Range(1, 8).Select(index => $"Practice {index}").ToList();

        var message = ProviderAffiliation.AffiliatedChildrenMessage("MaineHealth", children);

        Assert.Contains("8 entries are affiliated", message);
        Assert.Contains("Practice 5", message);
        Assert.Contains("and 3 more", message);
        Assert.DoesNotContain("Practice 6", message);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ProviderAffiliationNode Node(
        int id, string name, MedicalProviderKind? kind, int? parent = null) =>
        new(id, name, parent, kind);

    /// <summary>MaineHealth → Maine Medical Partners → a practice → a clinician.</summary>
    private static ProviderAffiliationNode[] FourLevelDirectory() =>
    [
        Node(1, "MaineHealth", MedicalProviderKind.Network),
        Node(2, "Maine Medical Partners", MedicalProviderKind.Network, parent: 1),
        Node(3, "Coastal Women's Healthcare", MedicalProviderKind.Practice, parent: 2),
        Node(4, "Dr. Reed", MedicalProviderKind.Individual, parent: 3)
    ];

    /// <summary>
    /// Networks 1..count, each belonging to the one below it in number, so network
    /// <c>count</c> sits at the deep end of a chain exactly <c>count</c> links long.
    /// </summary>
    private static ProviderAffiliationNode[] NetworkChain(int count) =>
        Enumerable.Range(1, count)
            .Select(id => Node(id, $"N{id}", MedicalProviderKind.Network, id == 1 ? null : id - 1))
            .ToArray();
}
