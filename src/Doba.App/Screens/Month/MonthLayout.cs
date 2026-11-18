using Doba.Core.Projections;

namespace Doba.App.Screens.Month;

/// <summary>
/// Fixed geometry of the month grid (design-reference lines 456–501 and monthData): the numbers the view lays out with
/// (via <c>x:Static</c>) and the projection fits items with, kept in one place so the two can never disagree.
/// Paddings, radii, font sizes and colours are theme tokens (the "== Month ==" regions of Tokens.axaml).
/// </summary>
public static class MonthLayout
{
    /// <summary>Weekday header row ("MON … SUN").</summary>
    public const double HeaderHeight = 26;

    /// <summary>The week-number column ("W46") left of the grid, and the header spacer above it.</summary>
    public const double WeekLabelWidth = 40;

    /// <summary>Day-number grid row at the top of each week.</summary>
    public const double DayNumberHeight = 34;

    /// <summary>One multi-day lane grid row (at most two per week).</summary>
    public const double LaneHeight = 24;

    /// <summary>Multi-day bar, centred in its lane.</summary>
    public const double BarHeight = 20;

    /// <summary>Item chip and the gap between chips (the projection fits one item per <see cref="ItemPitch"/>).</summary>
    public const double ChipHeight = 22, ChipGap = 3;

    /// <summary>Vertical space one item takes in the list.</summary>
    public const double ItemPitch = ChipHeight + ChipGap;

    /// <summary>
    /// What the prototype's row formula loses against the real body: monthData computes
    /// <c>H = floor((832 − 26) / weeks)</c> with a hard-coded 832, while its month body (main padding 16 / 20) is 836 px
    /// at 1600 × 1000, so the reference rows are 134 px, not 135. The measured body minus this keeps that geometry.
    /// </summary>
    public const double PrototypeBodySlack = 4;

    /// <summary>Space the "+N more" line needs below the lanes: list padding-top 4 + one 22 px chip slot.</summary>
    public const double MinListRoom = 4 + ChipHeight;

    /// <summary>
    /// Metrics for a month body of <paramref name="bodyHeight"/> px as laid out (weekday header included); the
    /// projection divides <paramref name="bodyHeight"/> − <see cref="PrototypeBodySlack"/> into week rows.
    /// </summary>
    public static MonthMetrics Metrics(double bodyHeight) =>
        new(bodyHeight - PrototypeBodySlack, HeaderHeight, DayNumberHeight, LaneHeight, ItemPitch);
}
