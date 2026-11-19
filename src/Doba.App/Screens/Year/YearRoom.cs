namespace Doba.App.Screens.Year;

/// <summary>
/// The measured room below a Year cell's header (KICKOFF §2: what fits depends on the measured size). The design's
/// 1600×1000 cells hold the prototype's four key items plus "+N more"; a smaller window holds fewer, and the key items
/// that do not fit are counted in "+N more" instead of being cut by the cell edge.
/// </summary>
/// <param name="Height">Height left for key items and "+N more" (cell content height − header − gap).</param>
/// <param name="ItemHeight">One key item (design 28).</param>
/// <param name="Gap">Flex gap between items and before "+N more" (design 6).</param>
/// <param name="MoreHeight">The "+N more" link's height.</param>
public sealed record YearRoom(double Height, double ItemHeight, double Gap, double MoreHeight)
{
    /// <summary>
    /// How many of <paramref name="rows"/> key items to show and the "+N more" count: everything when it fits; otherwise
    /// the most items that fit together with the "+N more" line, which then also counts the hidden items.
    /// </summary>
    public (int Shown, int More) Fit(int rows, int more)
    {
        if (Need(rows, more > 0) <= Height)
        {
            return (rows, more);
        }

        var shown = rows - 1;
        while (shown > 0 && Need(shown, withMore: true) > Height)
        {
            shown--;
        }

        return (Math.Max(0, shown), more + rows - Math.Max(0, shown));
    }

    private double Need(int items, bool withMore)
    {
        var height = (items * ItemHeight) + (Math.Max(0, items - 1) * Gap);
        // The items list stays in the cell's stack even when empty, so the gap before "+N more" is always there.
        return withMore ? height + Gap + MoreHeight : height;
    }
}
