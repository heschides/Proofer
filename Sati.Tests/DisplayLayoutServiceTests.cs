using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class DisplayLayoutServiceTests
{
    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void ADisplayLargerThan1080pKeepsTheStandardLayout(int width, int height)
    {
        var profile = DisplayLayoutService.FromPixelSize(width, height);

        Assert.False(profile.UsesCompactMode);
        Assert.False(profile.RequiresAdjustmentNotice);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1080)]
    [InlineData(1920, 1200)]
    public void A1080pBoundaryUsesCompactLayoutSilently(int width, int height)
    {
        var profile = DisplayLayoutService.FromPixelSize(width, height);

        Assert.True(profile.UsesCompactMode);
        Assert.False(profile.RequiresAdjustmentNotice);
    }

    [Theory]
    [InlineData(1919, 1080)]
    [InlineData(1920, 1079)]
    [InlineData(1280, 768)]
    [InlineData(2560, 720)]
    public void EitherDimensionBelow1080pUsesTheCompactLayout(int width, int height)
    {
        var profile = DisplayLayoutService.FromPixelSize(width, height);

        Assert.True(profile.UsesCompactMode);
        Assert.True(profile.RequiresAdjustmentNotice);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1, 1080)]
    [InlineData(1920, -1)]
    public void InvalidMonitorDimensionsAreRejected(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DisplayLayoutService.FromPixelSize(width, height));
    }
}
