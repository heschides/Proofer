using Sati.Contracts.V1;
using Sati.Models.Assessments;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Freezing a provider onto a document.
/// <para>
/// Everywhere else in Sati the practice and network are derived on every read, so correcting a
/// directory entry reaches every consumer. A document has to do the opposite: an assessment
/// approved in March must keep saying what it said in March, even after the clinician moves
/// practices. These tests pin that difference down, because getting it backwards in either
/// direction is silent.
/// </para>
/// </summary>
public sealed class ProviderSnapshotTests
{
    [Fact]
    public void ASnapshotCapturesTheProviderAndItsResolvedChain()
    {
        var snapshot = ProviderAffiliation.Snapshot(4, Directory());

        Assert.Equal(4, snapshot.ProviderId);
        Assert.Equal("Dr. Reed", snapshot.ProviderName);
        Assert.Equal("Coastal Women's Healthcare", snapshot.PracticeName);
        Assert.Equal("MaineHealth", snapshot.NetworkName);
        Assert.Equal("Dr. Reed — Coastal Women's Healthcare · MaineHealth", snapshot.Describe());
    }

    [Fact]
    public void ASnapshotDoesNotChangeWhenTheDirectoryDoes()
    {
        // The whole reason a document snapshots rather than derives.
        var before = ProviderAffiliation.Snapshot(4, Directory());

        var moved = Directory();
        moved.Add(new ProviderAffiliationNode(9, "InterMed", null, MedicalProviderKind.Network));
        moved[2] = moved[2] with { ParentProviderId = 9 };
        var after = ProviderAffiliation.Snapshot(4, moved);

        Assert.Equal("Coastal Women's Healthcare · MaineHealth",
            $"{before.PracticeName} · {before.NetworkName}");
        Assert.Equal("InterMed", after.NetworkName);
        // The already-taken snapshot is a value; nothing recomputed it.
        Assert.Equal("MaineHealth", before.NetworkName);
    }

    [Fact]
    public void AProviderThatStandsAloneDescribesAsJustItsName()
    {
        var snapshot = ProviderAffiliation.Snapshot(1, Directory());

        Assert.False(snapshot.HasAffiliation);
        Assert.Equal("MaineHealth", snapshot.Describe());
    }

    [Fact]
    public void AProviderWithOnlyANetworkOmitsTheMissingPractice()
    {
        // A hospitalist: network, no practice between. The dash-and-dot formatting must not
        // leave a dangling separator.
        var directory = Directory();
        directory.Add(new ProviderAffiliationNode(7, "Dr. Okafor", 1, MedicalProviderKind.Individual));

        var snapshot = ProviderAffiliation.Snapshot(7, directory);

        Assert.Equal(string.Empty, snapshot.PracticeName);
        Assert.Equal("Dr. Okafor — MaineHealth", snapshot.Describe());
    }

    [Fact]
    public void AnIdThatIsNotInTheDirectoryYieldsAnEmptySnapshotRatherThanAPartialOne()
    {
        // A document should not record an affiliation it could not actually resolve.
        var snapshot = ProviderAffiliation.Snapshot(999, Directory());

        Assert.Equal(0, snapshot.ProviderId);
        Assert.Equal(string.Empty, snapshot.ProviderName);
        Assert.Equal(string.Empty, snapshot.Describe());
    }

    [Fact]
    public void ZeroIsTreatedAsNoProviderRatherThanMatchingAnEmptyEntry()
    {
        Assert.Equal(0, ProviderAffiliation.Snapshot(0, Directory()).ProviderId);
    }

    [Fact]
    public void ANeedWrittenBeforeTheDirectoryStillDescribesItsBareName()
    {
        // Documents stored before 2026-08-28 have only the typed name. They must keep reading
        // correctly rather than gaining an empty affiliation clause.
        var need = new AssessmentNeed { ProviderNameSnapshot = "Dr. Reed" };

        Assert.Equal("Dr. Reed", need.DescribeProvider());
    }

    [Fact]
    public void ANeedCarriesTheFrozenChainOntoTheDocument()
    {
        var need = new AssessmentNeed
        {
            ProviderNameSnapshot = "Dr. Reed",
            ProviderPracticeSnapshot = "Coastal Women's Healthcare",
            ProviderNetworkSnapshot = "MaineHealth"
        };

        Assert.Equal("Dr. Reed — Coastal Women's Healthcare · MaineHealth", need.DescribeProvider());
    }

    [Fact]
    public void ANeedWithNoProviderDescribesAsNothing()
    {
        Assert.Equal(string.Empty, new AssessmentNeed().DescribeProvider());
    }

    private static List<ProviderAffiliationNode> Directory() =>
    [
        new(1, "MaineHealth", null, MedicalProviderKind.Network),
        new(3, "Coastal Women's Healthcare", 1, MedicalProviderKind.Practice),
        new(4, "Dr. Reed", 3, MedicalProviderKind.Individual)
    ];
}
