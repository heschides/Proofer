using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class OverviewLayoutPolicyTests
{
    [Theory]
    [InlineData(1079, OverviewLayoutTier.CompactOnePane)]
    [InlineData(1080, OverviewLayoutTier.CompactTwoPane)]
    [InlineData(1439, OverviewLayoutTier.CompactTwoPane)]
    [InlineData(1440, OverviewLayoutTier.Balanced)]
    [InlineData(2099, OverviewLayoutTier.Balanced)]
    [InlineData(2100, OverviewLayoutTier.Wide)]
    public void WidthSelectsExpectedTier(double width, OverviewLayoutTier expected)
    {
        Assert.Equal(expected, OverviewLayoutPolicy.Evaluate(width, 900).Tier);
    }

    [Theory]
    [InlineData(1127, OverviewLayoutTier.CompactOnePane, OverviewLayoutTier.CompactOnePane)]
    [InlineData(1128, OverviewLayoutTier.CompactOnePane, OverviewLayoutTier.CompactTwoPane)]
    [InlineData(1487, OverviewLayoutTier.CompactTwoPane, OverviewLayoutTier.CompactTwoPane)]
    [InlineData(1488, OverviewLayoutTier.CompactTwoPane, OverviewLayoutTier.Balanced)]
    [InlineData(2147, OverviewLayoutTier.Balanced, OverviewLayoutTier.Balanced)]
    [InlineData(2148, OverviewLayoutTier.Balanced, OverviewLayoutTier.Wide)]
    public void GrowingRequiresExpansionMargin(
        double width,
        OverviewLayoutTier previous,
        OverviewLayoutTier expected)
    {
        Assert.Equal(expected, OverviewLayoutPolicy.Evaluate(width, 900, previous).Tier);
    }

    [Theory]
    [InlineData(839, false, true)]
    [InlineData(840, true, false)]
    public void HeightControlsSummaryBandAndShortNoteLayout(
        double height,
        bool showsBand,
        bool usesShortNoteLayout)
    {
        var state = OverviewLayoutPolicy.Evaluate(1600, height);

        Assert.Equal(showsBand, state.ShowsLowerSummaryBand);
        Assert.Equal(usesShortNoteLayout, state.UsesShortNoteLayout);
    }

    [Theory]
    [InlineData(0, 900)]
    [InlineData(-1, 900)]
    [InlineData(double.NaN, 900)]
    [InlineData(1200, 0)]
    [InlineData(1200, double.PositiveInfinity)]
    public void InvalidGeometryIsRejected(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverviewLayoutPolicy.Evaluate(width, height));
    }
}
