namespace Sati.Services;

/// <summary>
/// The presentation layouts available to the case-manager Overview. The names are
/// internal; users choose only Easy Eyes and, when useful, Focus note.
/// </summary>
public enum OverviewLayoutTier
{
    CompactOnePane,
    CompactTwoPane,
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
    public const double TwoPaneWidth = 1080;
    public const double BalancedWidth = 1440;
    public const double WideWidth = 2100;
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
                OverviewLayoutTier.CompactTwoPane => TwoPaneWidth + ExpansionMargin,
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
            ShowsLowerSummaryBand: tall && candidate is OverviewLayoutTier.Balanced or OverviewLayoutTier.Wide,
            UsesShortNoteLayout: !tall,
            effectiveWidth,
            effectiveHeight);
    }

    private static OverviewLayoutTier TierForWidth(double width) => width switch
    {
        >= WideWidth => OverviewLayoutTier.Wide,
        >= BalancedWidth => OverviewLayoutTier.Balanced,
        >= TwoPaneWidth => OverviewLayoutTier.CompactTwoPane,
        _ => OverviewLayoutTier.CompactOnePane
    };
}
