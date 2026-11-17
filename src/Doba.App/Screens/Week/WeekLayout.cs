namespace Doba.App.Screens.Week;

/// <summary>
/// Fixed geometry of the week grid (design-reference lines 503–562 and weekData): the numbers the view lays out with
/// (via <c>x:Static</c>) and the projection fits cards with, kept in one place so the two can never disagree.
/// Font sizes, line heights and density paddings are not here: they are read from the theme resources.
/// </summary>
public static class WeekLayout
{
    /// <summary>Day header grid row (button: weekday label, day number, progress line).</summary>
    public const double HeaderHeight = 86;

    /// <summary>One multi-day lane grid row.</summary>
    public const double LaneHeight = 36;

    /// <summary>Card list padding: 10 px top, 8 px left/right.</summary>
    public const double ListPaddingTop = 10, ListPaddingX = 8;

    /// <summary>Gap between cards (and before "+N more").</summary>
    public const double CardGap = 6;

    /// <summary>Space reserved for the "+N more" link while more cards follow.</summary>
    public const double MoreReserve = 26;

    /// <summary>Gap between card meta items; the priority button is 18 px wide.</summary>
    public const double MetaGap = 8, PriorityButton = 18;

    /// <summary>Card checkbox (16 px).</summary>
    public const double CheckSize = 16;

    /// <summary>Multi-day bar height and its inset from an unclipped column edge.</summary>
    public const double BarHeight = 28, BarInset = 8;
}
