using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class OverviewLayoutPolicyTests
{
    [Theory]
    [InlineData(1079, OverviewLayoutTier.NarrowStack)]
    [InlineData(1080, OverviewLayoutTier.Compact)]
    [InlineData(1439, OverviewLayoutTier.Compact)]
    [InlineData(1440, OverviewLayoutTier.Balanced)]
    [InlineData(2099, OverviewLayoutTier.Balanced)]
    [InlineData(2100, OverviewLayoutTier.Wide)]
    public void WidthSelectsExpectedTier(double width, OverviewLayoutTier expected)
    {
        Assert.Equal(expected, OverviewLayoutPolicy.Evaluate(width, 900).Tier);
    }

    [Theory]
    [InlineData(1127, OverviewLayoutTier.NarrowStack, OverviewLayoutTier.NarrowStack)]
    [InlineData(1128, OverviewLayoutTier.NarrowStack, OverviewLayoutTier.Compact)]
    [InlineData(1487, OverviewLayoutTier.Compact, OverviewLayoutTier.Compact)]
    [InlineData(1488, OverviewLayoutTier.Compact, OverviewLayoutTier.Balanced)]
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
    [InlineData(699, false, true)]
    [InlineData(700, true, true)]
    [InlineData(839, true, true)]
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
