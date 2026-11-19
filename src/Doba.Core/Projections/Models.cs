using Doba.Core.Model;

namespace Doba.Core.Projections;

// ---------- Inbox ----------

/// <summary>Inbox: left = undated by priority + Done; right = "Scheduled" (Overdue, Today, Tomorrow, next 14 days, Later).</summary>
/// <param name="ScheduledCount">"57 open".</param>
public sealed record InboxModel(IReadOnlyList<TaskGroup> Left, IReadOnlyList<TaskGroup> Right, string ScheduledCount);

// ---------- Today ----------

/// <param name="Summary">"6 tasks · 4 done · 2 left · 3 overdue".</param>
/// <param name="Progress">0..1 share of today's single-day tasks that are done, in whole percents as the prototype's bar
/// (<c>Math.round(done / n · 100)</c>): 4 of 6 → 0.67.</param>
/// <param name="Groups">Overdue, In progress, Scheduled, Anytime (always present), Done.</param>
public sealed record TodayModel(string Summary, double Progress, IReadOnlyList<TaskGroup> Groups, ScheduleModel Schedule);

// ---------- Day ----------

/// <param name="Progress">"3 / 5 done", or empty when the day has no single-day tasks.</param>
/// <param name="Spans">Multi-day tasks covering the day; Meta = "Day 2 of 3".</param>
/// <param name="Items">Single-day tasks sorted timed-first by time, then by priority.</param>
/// <param name="Empty">No items and no spans ("Nothing here").</param>
public sealed record DayModel(DateOnly Date, string Progress, IReadOnlyList<TaskRow> Spans, IReadOnlyList<TaskRow> Items, bool Empty, ScheduleModel Schedule);

// ---------- Week ----------

/// <summary>Measured space for the week grid. The App measures; Core decides how many cards fit.</summary>
/// <param name="RowHeight">Height of one of the two week rows (design: 403 px at 1600×1000).</param>
/// <param name="CardHeight">Height of a task card for a given title (design: 36, or 54 when the title wraps to two lines).</param>
/// <param name="CardGap">Gap between cards (design 6).</param>
/// <param name="MoreReserve">Space kept for the "+N more" link when more cards follow (design 26).</param>
/// <param name="HeaderHeight">Day header row (design 86).</param>
/// <param name="LaneHeight">Multi-day lane (design 36).</param>
/// <param name="ListPaddingTop">Top padding of the card list below the lanes (design 10).</param>
public sealed record WeekMetrics(double RowHeight, Func<string, double> CardHeight, double CardGap = 6, double MoreReserve = 26, double HeaderHeight = 86, double LaneHeight = 36,
    double ListPaddingTop = 10)
{
    /// <summary>
    /// How many of <paramref name="lanes"/> multi-day lanes a row shows: all of them while they leave room under the
    /// lanes for the card list's top padding and a "+N more" line (always at the design size); in a short row only the
    /// lanes that leave that room, so no bar is cut by the row edge. An unmeasured row (NaN) shows every lane.
    /// </summary>
    public int VisibleLanes(int lanes)
    {
        if (double.IsNaN(RowHeight) || lanes == 0)
        {
            return lanes;
        }

        var room = RowHeight - HeaderHeight - ListPaddingTop - MoreReserve;
        var fit = room / LaneHeight;
        return fit >= lanes ? lanes : Math.Clamp((int)Math.Floor(fit), 0, lanes);
    }

    /// <summary>
    /// Height of a given task's card. A card's height depends on more than its title (tag, time and priority decide
    /// how much width the title gets), so when this is set it is used instead of <see cref="CardHeight"/>.
    /// </summary>
    public Func<TaskItem, double>? TaskCardHeight { get; init; }

    /// <summary>The height <see cref="Projector"/> fits <paramref name="task"/>'s card with.</summary>
    public double CardHeightOf(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return TaskCardHeight is { } perTask ? perTask(task) : CardHeight(task.Title);
    }

    /// <summary>The prototype's heuristic: titles longer than 26 characters wrap to two lines.</summary>
    public static WeekMetrics Design(double rowHeight = 403) => new(rowHeight, t => t.Length > 26 ? 54 : 36);
}

/// <summary>One day column in the Week view.</summary>
/// <param name="Column">0-based grid column (0..3).</param>
/// <param name="Weekday">"Mon".</param>
/// <param name="MonthShort">"Nov".</param>
/// <param name="IsPreview">The dimmed "Next week" preview cell (last cell of row 2).</param>
/// <param name="Progress">0..1 done share of single-day tasks, in whole percents (prototype <c>Math.round</c>).</param>
/// <param name="Tasks">Cards that fit; preview cards are rendered dimmer by the view.</param>
/// <param name="MoreCount">Tasks that did not fit, plus the multi-day bars covering this day in lanes the row cannot
/// show ("+N more"); 0 = none.</param>
/// <param name="Empty">Nothing to show for the day: no single-day tasks and no hidden bars ("Nothing here").</param>
public sealed record WeekDayCell(DateOnly Date, int Column, string Weekday, int DayNumber, string MonthShort, bool IsToday, bool IsPreview, double Progress, IReadOnlyList<TaskRow> Tasks, int MoreCount, bool Empty)
{
    /// <summary>"MON" (the prototype upper-cases the weekday with CSS).</summary>
    public string WeekdayLabel => Labels.Upper(Weekday);

    /// <summary>"9".</summary>
    public string DayLabel => Labels.Number(DayNumber);

    /// <summary>Day header button name: "Open Monday 9 November", or "Open next Monday 16 November" for the preview.</summary>
    public string OpenLabel => string.Concat(IsPreview ? "Open next " : "Open ", Labels.WeekdayLong(Date), " ", DayLabel, " ", Labels.MonthName(Date));

    /// <summary>"+2 more".</summary>
    public string MoreLabel => Labels.More(MoreCount);
}

/// <param name="LaneCount">Multi-day lanes shown in this row: all of the row's lanes, or fewer in a short row
/// (<see cref="WeekMetrics.VisibleLanes"/>).</param>
/// <param name="Spans">The bars in the shown lanes; a bar in a hidden lane is counted in "+N more" of each day it covers.</param>
public sealed record WeekRow(int LaneCount, IReadOnlyList<WeekDayCell> Cells, IReadOnlyList<SpanBar> Spans);

/// <summary>Week view: row 1 = first four days of the week, row 2 = last three days + preview of next week's first day.</summary>
public sealed record WeekModel(DateOnly WeekStart, IReadOnlyList<WeekRow> Rows);

// ---------- Month ----------

/// <param name="AvailableHeight">Height of the month body including the 26 px weekday header (design 832).</param>
/// <param name="MinListRoom">Room a week row needs below its day number and lanes for a list line (list padding 4 +
/// one 22 px chip); with less, every task of a cell is counted in "+N more" in the day-number row.</param>
public sealed record MonthMetrics(double AvailableHeight = 832, double HeaderHeight = 26, double DayNumberHeight = 34, double LaneHeight = 24, double ItemHeight = 25, double MinListRoom = 26);

/// <param name="Items">Items that fit; when more exist the last slot is used by "+N more".</param>
/// <param name="MoreCount">Items that did not fit, plus the multi-day spans covering this day that need a third lane
/// (not drawn in the Month view); 0 = none.</param>
/// <param name="MoreInHeader">The row has no room for a list line (<see cref="MonthMetrics.MinListRoom"/>): Items is
/// empty and "+<paramref name="MoreCount"/> more" (everything of the day) goes in the day-number row.</param>
public sealed record MonthDayCell(DateOnly Date, int DayNumber, bool InMonth, bool IsToday, IReadOnlyList<TaskRow> Items, int MoreCount, bool MoreInHeader = false)
{
    /// <summary>"15".</summary>
    public string DayLabel => Labels.Number(DayNumber);

    /// <summary>The cell button's name: "Open Sunday 15 November".</summary>
    public string OpenLabel => string.Concat("Open ", Labels.WeekdayLong(Date), " ", DayLabel, " ", Labels.MonthName(Date));

    /// <summary>"+3 more" (in the list, or in the day-number row when <see cref="MoreInHeader"/>).</summary>
    public string MoreLabel => Labels.More(MoreCount);
}

/// <param name="WeekNumber">Shown as "W46".</param>
/// <param name="IsCurrent">Contains today (label in accent).</param>
/// <param name="ZoomTarget">Week view anchor when the week label is clicked (today if current week, else the week start).</param>
/// <param name="RowHeight">floor((AvailableHeight − header) / weekCount); 0 when that is negative or not a number.</param>
/// <param name="Spans">At most two lanes; a span that would need a third lane is not drawn (prototype rule) and is
/// counted in "+N more" of each day it covers in the week instead.</param>
public sealed record MonthWeek(DateOnly WeekStart, int WeekNumber, bool IsCurrent, DateOnly ZoomTarget, double RowHeight, int LaneCount, IReadOnlyList<MonthDayCell> Days, IReadOnlyList<SpanBar> Spans)
{
    /// <summary>"W46".</summary>
    public string Label => "W" + Labels.Number(WeekNumber);

    /// <summary>"Open week 46".</summary>
    public string OpenLabel => "Open week " + Labels.Number(WeekNumber);
}

/// <param name="WeekdayLabels">"Mon".."Sun" in configured order.</param>
public sealed record MonthModel(int Year, int Month, IReadOnlyList<string> WeekdayLabels, IReadOnlyList<MonthWeek> Weeks)
{
    /// <summary>"MON".."SUN" in configured order (the prototype upper-cases the header with CSS).</summary>
    public IReadOnlyList<string> WeekdayHeaders { get; init; } = [];
}

// ---------- Year ----------

/// <param name="Stat">"31 tasks · 12 done", or "No tasks".</param>
/// <param name="Progress">0..1 done share of the month's single-day tasks, in whole percents (prototype <c>Math.round</c>).</param>
/// <param name="Rows">Key items: multi-day spans (Meta "12–14" or "28 Oct – 2 Nov") then upcoming open tasks (Meta "16 Nov"), at most 4
/// (fewer when <see cref="YearMetrics"/> says the cell has room for fewer).</param>
/// <param name="MoreCount">"+N more": spans not listed, plus key items the measured cell has no room for; 0 = none.</param>
/// <param name="ZoomTarget">Month view anchor (today for the current month, else the 1st).</param>
public sealed record YearMonth(int Month, string Name, bool IsCurrent, bool IsPast, string Stat, double Progress, IReadOnlyList<TaskRow> Rows, int MoreCount, DateOnly ZoomTarget)
{
    /// <summary>"31 TASKS · 12 DONE" (the prototype upper-cases the stat line with CSS).</summary>
    public string StatLabel => Labels.Upper(Stat);

    /// <summary>Header button name: "Open November".</summary>
    public string OpenLabel => "Open " + Name;

    /// <summary>"+3 more".</summary>
    public string MoreLabel => Labels.More(MoreCount);
}

/// <summary>
/// The measured room below a Year cell's header (KICKOFF §2: what fits depends on the measured size). The design's
/// 1600×1000 cells hold the prototype's four key items plus "+N more"; a smaller window holds fewer, and the key items
/// that do not fit are counted in "+N more" instead of being cut by the cell edge.
/// </summary>
/// <param name="Room">Height left for key items and "+N more" (cell content height − header − gap).</param>
/// <param name="ItemHeight">One key item (design 28).</param>
/// <param name="Gap">Gap between items and before "+N more" (design 6).</param>
/// <param name="MoreHeight">The "+N more" link's height.</param>
public sealed record YearMetrics(double Room, double ItemHeight, double Gap, double MoreHeight)
{
    /// <summary>
    /// How many of <paramref name="rows"/> key items to show and the "+N more" count: everything when it fits; otherwise
    /// the most items that fit together with the "+N more" line, which then also counts the hidden items.
    /// </summary>
    public (int Shown, int More) Fit(int rows, int more)
    {
        if (!(Need(rows, more > 0) > Room))
        {
            return (rows, more); // fits (or an unmeasured room: NaN shows everything, as without metrics)
        }

        var shown = rows - 1;
        while (shown > 0 && Need(shown, withMore: true) > Room)
        {
            shown--;
        }

        shown = Math.Max(0, shown);
        return (shown, more + rows - shown);
    }

    private double Need(int items, bool withMore)
    {
        var height = (items * ItemHeight) + (Math.Max(0, items - 1) * Gap);
        // The items list stays in the cell's stack even when empty, so the gap before "+N more" is always there.
        return withMore ? height + Gap + MoreHeight : height;
    }
}

public sealed record YearModel(int Year, IReadOnlyList<YearMonth> Months);

// ---------- Sidebar ----------

/// <param name="OpenCount">Open tasks with this tag.</param>
/// <param name="Active">This tag is the current filter.</param>
public sealed record TagEntry(string Name, int OpenCount, bool Active);

/// <param name="InboxCount">Open undated tasks.</param>
/// <param name="TodayCount">Open single-day tasks dated today.</param>
public sealed record SidebarModel(int InboxCount, int TodayCount, IReadOnlyList<TagEntry> Tags);

// ---------- Search ----------

/// <param name="Context">Where the task lives: "Inbox", "Mon 16 Nov", "20–22 Nov".</param>
public sealed record SearchHit(TaskRow Row, string Context);
