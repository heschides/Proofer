namespace Sati.Services;

/// <summary>
/// The presentation layouts available to the case-manager Overview. The names are
/// internal; users choose only the optional Easy Eyes enlargement.
/// </summary>
public enum OverviewLayoutTier
{
    NarrowStack,
    Compact,
    Balanced,
    Wide
}

public readonly record struct OverviewLayoutState(
    OverviewLayoutTier Tier,
    bool ShowsLowerSummaryBand,
    bool UsesShortNoteLayout,
    double EffectiveWidth,
    double EffectiveHeight);

/// <summary>
/// Converts the finite space allocated by WPF into a predictable Overview layout.
/// It has no monitor, view-model, or persistence dependencies, which keeps resize
/// events from becoming application behavior.
/// </summary>
public static class OverviewLayoutPolicy
{
    public const double CompactWidth = 1080;
    public const double BalancedWidth = 1440;
    public const double WideWidth = 2100;
    // The former Forms/Productivity tab set needed a tall 280-unit band. The
    // productivity-only summary fits comfortably once 700 effective units are
    // available, including on a 1080p display with Easy Eyes enabled.
    public const double SummaryBandHeight = 700;
    public const double TallHeight = 840;
    public const double ExpansionMargin = 48;

    public static OverviewLayoutState Evaluate(
        double effectiveWidth,
        double effectiveHeight,
        OverviewLayoutTier? previousTier = null)
    {
        if (!double.IsFinite(effectiveWidth) || effectiveWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(effectiveWidth));
        if (!double.IsFinite(effectiveHeight) || effectiveHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(effectiveHeight));

        var candidate = TierForWidth(effectiveWidth);
        if (previousTier is { } previous && candidate > previous)
        {
            var expansionThreshold = candidate switch
            {
                OverviewLayoutTier.Compact => CompactWidth + ExpansionMargin,
                OverviewLayoutTier.Balanced => BalancedWidth + ExpansionMargin,
                OverviewLayoutTier.Wide => WideWidth + ExpansionMargin,
                _ => 0
            };

            if (effectiveWidth < expansionThreshold)
                candidate = previous;
        }

        var tall = effectiveHeight >= TallHeight;
        return new OverviewLayoutState(
            candidate,
            ShowsLowerSummaryBand: effectiveHeight >= SummaryBandHeight &&
                                   candidate is not OverviewLayoutTier.NarrowStack,
            UsesShortNoteLayout: !tall,
            effectiveWidth,
            effectiveHeight);
    }

    private static OverviewLayoutTier TierForWidth(double width) => width switch
    {
        >= WideWidth => OverviewLayoutTier.Wide,
        >= BalancedWidth => OverviewLayoutTier.Balanced,
        >= CompactWidth => OverviewLayoutTier.Compact,
        _ => OverviewLayoutTier.NarrowStack
    };
}
