using Doba.Core.Editing;
using Doba.Core.Time;

namespace Doba.Core.Projections;

/// <summary>A timed task on the day timeline (07:00–23:00), placed by <see cref="ScheduleColumns"/>.</summary>
/// <param name="Row">The task (Meta empty).</param>
/// <param name="StartMinutes">Minutes since midnight.</param>
/// <param name="DurationMinutes">Effective duration (0 → 30).</param>
/// <param name="Range">"14:00 – 15:30".</param>
/// <param name="Placement">Column, span and column count of its overlap cluster; hidden under a "+N".</param>
/// <param name="X">Left edge in the grid (prototype <c>left</c>, to 0.5 px); 0 when hidden.</param>
/// <param name="Width">Width (to 0.5 px); 0 when hidden.</param>
/// <param name="Top">Top in the grid at the design's 48 px/hour.</param>
/// <param name="Height">Height at 48 px/hour (at least 24).</param>
/// <param name="Meta">The time line its width and height leave room for (see <see cref="ScheduleLayout.Meta"/>); empty for none.</param>
/// <param name="IsTall">At least 44 px: title and time stacked (column); otherwise one row.</param>
/// <param name="CheckTop">Top of the 24 px checkbox hit area inside the block.</param>
/// <param name="GripHeight">Height of the bottom resize zone (11 px; 8 below 32 px).</param>
/// <param name="AccessibleLabel">"Title, 09:30–10:30[, done]. Enter to edit, arrows to move, …".</param>
/// <param name="ToolTip">"Title · 09:30–10:30".</param>
public sealed record ScheduleBlock(
    TaskRow Row,
    int StartMinutes,
    int DurationMinutes,
    string Range,
    ColumnPlacement Placement,
    double X,
    double Width,
    double Top,
    double Height,
    string Meta,
    bool IsTall,
    double CheckTop,
    double GripHeight,
    string AccessibleLabel,
    string ToolTip)
{
    /// <summary>Folded into a "+N" overflow: not drawn and not draggable.</summary>
    public bool IsHidden => Placement.IsHidden;

    /// <summary>"Mark done: Title" / "Mark not done: Title".</summary>
    public string CheckLabel => Row.ToggleLabel;
}

/// <summary>One task in a "+N" list (the prototype's popover rows, sorted by byTime).</summary>
/// <param name="Range">"10:15–10:45".</param>
public sealed record ScheduleOverflowRow(TaskRow Row, int StartMinutes, int DurationMinutes, string Range)
{
    /// <summary>"Mark done: Title" / "Mark not done: Title".</summary>
    public string CheckLabel => Row.ToggleLabel;
}

/// <summary>A cluster's "+N" stand-in for the blocks it hides, with its list.</summary>
/// <param name="Start">Earliest start of the hidden blocks (unique per day: the overflow's key).</param>
/// <param name="End">Latest visual end of the hidden blocks.</param>
/// <param name="Column">The last column.</param>
/// <param name="Columns">Columns of its cluster.</param>
/// <param name="X">Left edge (to 0.5 px).</param>
/// <param name="Width">One column wide (to 0.5 px).</param>
/// <param name="Top">Top at 48 px/hour.</param>
/// <param name="Height">From <see cref="Start"/> to <see cref="End"/> (at least 24).</param>
/// <param name="IsTall">At least 44 px: the text sits at the top (6 px down) instead of centred.</param>
/// <param name="Text">"+2".</param>
/// <param name="AccessibleLabel">"2 more tasks, 10:15 to 11:00".</param>
/// <param name="PopoverHead">"2 more · 10:15–11:00" (to the latest end of the hidden tasks by their real duration).</param>
/// <param name="PopoverLabel">"2 more tasks".</param>
/// <param name="PopoverBelow">The list fits under the "+N" inside the grid; otherwise it opens above it.</param>
/// <param name="PopoverTop">Top of the list in the grid.</param>
/// <param name="Rows">The hidden tasks, by time.</param>
public sealed record ScheduleOverflow(
    int Start,
    int End,
    int Column,
    int Columns,
    double X,
    double Width,
    double Top,
    double Height,
    bool IsTall,
    string Text,
    string AccessibleLabel,
    string PopoverHead,
    string PopoverLabel,
    bool PopoverBelow,
    double PopoverTop,
    IReadOnlyList<ScheduleOverflowRow> Rows);

/// <summary>Timeline of one day. Blocks starting before <see cref="FirstHour"/> are omitted, as in the prototype.</summary>
/// <param name="Blocks">Every timed task of the day by time, hidden ones included (see <see cref="ScheduleBlock.IsHidden"/>).</param>
/// <param name="NowLabel">"22:26" when the day is today, else null (no now-line).</param>
/// <param name="Overflows">The "+N" stand-ins, in time order.</param>
public sealed record ScheduleModel(IReadOnlyList<ScheduleBlock> Blocks, int? NowMinutes, string? NowLabel, IReadOnlyList<ScheduleOverflow> Overflows)
{
    public const int FirstHour = 7;
    public const int LastHour = 23;
}

/// <summary>
/// Pixel geometry and text of the timeline (design: 48 px/hour, grid height 16·48+2, a 302 px column area at x 54 split
/// into up to three columns with 4 px gaps), mirroring the prototype's <c>schedData</c> and <c>sbVis</c>.
/// </summary>
public static class ScheduleLayout
{
    /// <summary>Design: 48 px per hour.</summary>
    public const double DesignHourHeight = 48;

    /// <summary>Left edge of the block area (hour labels 40 + gap 12 + 2).</summary>
    public const double ColumnsLeft = 54;

    /// <summary>Width of the block area.</summary>
    public const double ColumnsWidth = 302;

    /// <summary>Gap between columns.</summary>
    public const double ColumnGap = 4;

    /// <summary>Blocks at least this tall stack title and time.</summary>
    public const double TallHeight = 44;

    /// <summary>Width of the "+N" list, right-aligned with the block area.</summary>
    public const double PopoverWidth = 232;

    /// <summary>Left of the "+N" list: 54 + 302 − 232.</summary>
    public const double PopoverLeft = ColumnsLeft + ColumnsWidth - PopoverWidth;

    /// <summary>"+N" list: head 36 px.</summary>
    public const double PopoverHead = 36;

    /// <summary>"+N" list: 32 px per row.</summary>
    public const double PopoverRow = 32;

    /// <summary>"+N" list: 12 px under the rows.</summary>
    public const double PopoverFoot = 12;

    /// <summary>"+N" list: 6 px from the "+N".</summary>
    public const double PopoverGap = 6;

    /// <summary>top = round((start − 7h)/60 · hourHeight) + 1.</summary>
    public static double BlockTop(int startMinutes, double hourHeight) =>
        Math.Floor((startMinutes - ScheduleModel.FirstHour * 60) / 60.0 * hourHeight + 0.5) + 1;

    /// <summary>h = max(24, round(duration/60 · hourHeight) − 3).</summary>
    public static double BlockHeight(int durationMinutes, double hourHeight) =>
        Math.Max(24, Math.Floor(durationMinutes / 60.0 * hourHeight + 0.5) - 3);

    /// <summary>Blocks shorter than 44 px lay out title and range on one row.</summary>
    public static bool IsCompact(double blockHeight) => blockHeight < TallHeight;

    /// <summary>round((now − 7h)/60 · hourHeight).</summary>
    public static double NowTop(int nowMinutes, double hourHeight) =>
        Math.Floor((nowMinutes - ScheduleModel.FirstHour * 60) / 60.0 * hourHeight + 0.5);

    public static double GridHeight(double hourHeight) => 16 * hourHeight + 2;

    /// <summary>The prototype's <c>r1</c>: to the nearest half pixel (halves up).</summary>
    public static double Half(double v) => Math.Floor(v * 2 + 0.5) / 2;

    /// <summary>Column width for a cluster of <paramref name="columns"/>: 1 → 302, 2 → 149, 3 → 98.</summary>
    public static double ColumnWidth(int columns) => (ColumnsWidth - ColumnGap * (columns - 1)) / columns;

    /// <summary>x = 54 + col · (colW + 4), to 0.5 px; 0 when hidden.</summary>
    public static double BlockX(ColumnPlacement p) =>
        p.IsHidden ? 0 : Half(ColumnsLeft + p.Column * (ColumnWidth(p.Columns) + ColumnGap));

    /// <summary>w = span · colW + (span − 1) · 4, to 0.5 px; 0 when hidden.</summary>
    public static double BlockWidth(ColumnPlacement p) =>
        p.IsHidden ? 0 : Half(p.Span * ColumnWidth(p.Columns) + (p.Span - 1) * ColumnGap);

    /// <summary>
    /// The block's time line by its size (prototype <c>sbVis</c>): tall (≥ 44 px) → the range from 140 px wide, else
    /// the start; short → the range from 220 px, the start from 120 px, else nothing. <paramref name="active"/> (being
    /// resized, or the dragged copy) appends " · 1h 45m" to the range.
    /// </summary>
    public static string Meta(double width, double height, int startMinutes, int durationMinutes, bool active = false)
    {
        var start = DateMath.Hhmm(startMinutes);
        if (height >= TallHeight ? width < 140 : width < 220)
        {
            return height >= TallHeight || width >= 120 ? start : string.Empty;
        }

        var range = string.Concat(start, " – ", DateMath.Hhmm(startMinutes + durationMinutes));
        return active ? ScheduleEdit.ActiveRange(startMinutes, durationMinutes) : range;
    }

    /// <summary>Top of the 24 px checkbox hit area: 4 in a tall block, else centred (round((h − 2 − 24) / 2)).</summary>
    public static double CheckTop(double height) => height >= TallHeight ? 4 : DateMath.RoundHalfUp((height - 2 - 24) / 2);

    /// <summary>The bottom resize zone: 11 px, 8 in a block under 32 px (3 px of it below the edge).</summary>
    public static double GripHeight(double height) => height < 32 ? 8 : 11;

    /// <summary>"09:30–10:30" (no spaces: labels, tooltips and the "+N" list).</summary>
    public static string TightRange(int startMinutes, int durationMinutes) =>
        string.Concat(DateMath.Hhmm(startMinutes), "–", DateMath.Hhmm(startMinutes + durationMinutes));

    /// <summary>"Title, 09:30–10:30[, done]. Enter to edit, arrows to move, Shift and arrows to change length, Space to mark done".</summary>
    public static string BlockLabel(string title, int startMinutes, int durationMinutes, bool done) =>
        string.Concat(title, ", ", TightRange(startMinutes, durationMinutes), done ? ", done" : string.Empty,
            ". Enter to edit, arrows to move, Shift and arrows to change length, Space to mark done");

    /// <summary>"Title · 09:30–10:30".</summary>
    public static string BlockTip(string title, int startMinutes, int durationMinutes) =>
        string.Concat(title, " · ", TightRange(startMinutes, durationMinutes));

    /// <summary>A block of <paramref name="row"/> with everything the view draws.</summary>
    public static ScheduleBlock Block(TaskRow row, int startMinutes, int durationMinutes, ColumnPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(row);
        var w = BlockWidth(placement);
        var h = BlockHeight(durationMinutes, DesignHourHeight);
        return new ScheduleBlock(
            row,
            startMinutes,
            durationMinutes,
            string.Concat(DateMath.Hhmm(startMinutes), " – ", DateMath.Hhmm(startMinutes + durationMinutes)),
            placement,
            BlockX(placement),
            w,
            BlockTop(startMinutes, DesignHourHeight),
            h,
            Meta(w, h, startMinutes, durationMinutes),
            !IsCompact(h),
            CheckTop(h),
            GripHeight(h),
            BlockLabel(row.Title, startMinutes, durationMinutes, row.Done),
            BlockTip(row.Title, startMinutes, durationMinutes));
    }

    /// <summary>
    /// The "+N" of <paramref name="o"/>; <paramref name="rows"/> are its hidden tasks already sorted by time (the
    /// prototype's byTime, ties in layout order).
    /// </summary>
    public static ScheduleOverflow Overflow(ColumnOverflow o, IReadOnlyList<ScheduleOverflowRow> rows)
    {
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(rows);
        var cw = ColumnWidth(o.Columns);
        var top = BlockTop(o.Start, DesignHourHeight);
        var h = BlockHeight(o.End - o.Start, DesignHourHeight);
        var n = rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var realEnd = o.End;
        if (rows.Count > 0)
        {
            realEnd = int.MinValue;
            foreach (var r in rows)
            {
                realEnd = Math.Max(realEnd, r.StartMinutes + r.DurationMinutes);
            }
        }

        var popH = PopoverHead + rows.Count * PopoverRow + PopoverFoot;
        var below = top + h + PopoverGap + popH <= GridHeight(DesignHourHeight);
        return new ScheduleOverflow(
            o.Start,
            o.End,
            o.Column,
            o.Columns,
            Half(ColumnsLeft + o.Column * (cw + ColumnGap)),
            Half(cw),
            top,
            h,
            !IsCompact(h),
            "+" + n,
            string.Concat(n, " more tasks, ", DateMath.Hhmm(o.Start), " to ", DateMath.Hhmm(o.End)),
            string.Concat(n, " more · ", DateMath.Hhmm(o.Start), "–", DateMath.Hhmm(realEnd)),
            n + " more tasks",
            below,
            below ? top + h + PopoverGap : top - popH - PopoverGap,
            rows);
    }
}
