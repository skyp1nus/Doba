using Doba.Core.Model;

namespace Doba.Core.Projections;

// ---------- Inbox ----------

/// <summary>Inbox: left = undated by priority + Done; right = "Scheduled" (Overdue, Today, Tomorrow, next 14 days, Later).</summary>
/// <param name="ScheduledCount">"57 open".</param>
public sealed record InboxModel(IReadOnlyList<TaskGroup> Left, IReadOnlyList<TaskGroup> Right, string ScheduledCount);

// ---------- Today ----------

/// <param name="Summary">"6 tasks · 4 done · 2 left · 3 overdue".</param>
/// <param name="Progress">0..1 share of today's single-day tasks that are done.</param>
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
public sealed record WeekMetrics(double RowHeight, Func<string, double> CardHeight, double CardGap = 6, double MoreReserve = 26, double HeaderHeight = 86, double LaneHeight = 36)
{
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
/// <param name="Progress">0..1 done share of single-day tasks.</param>
/// <param name="Tasks">Cards that fit; preview cards are rendered dimmer by the view.</param>
/// <param name="MoreCount">Tasks that did not fit ("+N more"); 0 = none.</param>
/// <param name="Empty">No single-day tasks ("Nothing here").</param>
public sealed record WeekDayCell(DateOnly Date, int Column, string Weekday, int DayNumber, string MonthShort, bool IsToday, bool IsPreview, double Progress, IReadOnlyList<TaskRow> Tasks, int MoreCount, bool Empty);

/// <param name="LaneCount">Number of multi-day lanes in this row.</param>
public sealed record WeekRow(int LaneCount, IReadOnlyList<WeekDayCell> Cells, IReadOnlyList<SpanBar> Spans);

/// <summary>Week view: row 1 = first four days of the week, row 2 = last three days + preview of next week's first day.</summary>
public sealed record WeekModel(DateOnly WeekStart, IReadOnlyList<WeekRow> Rows);

// ---------- Month ----------

/// <param name="AvailableHeight">Height of the month body including the 26 px weekday header (design 832).</param>
/// <param name="MinListRoom">Room a week row needs below its day number and lanes for a list line (list padding 4 +
/// one 22 px chip); with less, every task of a cell is counted in "+N more" in the day-number row.</param>
public sealed record MonthMetrics(double AvailableHeight = 832, double HeaderHeight = 26, double DayNumberHeight = 34, double LaneHeight = 24, double ItemHeight = 25, double MinListRoom = 26);

/// <param name="Items">Items that fit; when more exist the last slot is used by "+N more".</param>
/// <param name="MoreInHeader">The row has no room for a list line (<see cref="MonthMetrics.MinListRoom"/>): Items is
/// empty and "+<paramref name="MoreCount"/> more" (every task of the day) goes in the day-number row.</param>
public sealed record MonthDayCell(DateOnly Date, int DayNumber, bool InMonth, bool IsToday, IReadOnlyList<TaskRow> Items, int MoreCount, bool MoreInHeader = false);

/// <param name="WeekNumber">Shown as "W46".</param>
/// <param name="IsCurrent">Contains today (label in accent).</param>
/// <param name="ZoomTarget">Week view anchor when the week label is clicked (today if current week, else the week start).</param>
/// <param name="RowHeight">floor((AvailableHeight − header) / weekCount); 0 when that is negative or not a number.</param>
/// <param name="Spans">At most two lanes; further overlapping spans are not shown (prototype rule).</param>
public sealed record MonthWeek(DateOnly WeekStart, int WeekNumber, bool IsCurrent, DateOnly ZoomTarget, double RowHeight, int LaneCount, IReadOnlyList<MonthDayCell> Days, IReadOnlyList<SpanBar> Spans);

/// <param name="WeekdayLabels">"Mon".."Sun" in configured order.</param>
public sealed record MonthModel(int Year, int Month, IReadOnlyList<string> WeekdayLabels, IReadOnlyList<MonthWeek> Weeks);

// ---------- Year ----------

/// <param name="Stat">"31 tasks · 12 done", or "No tasks".</param>
/// <param name="Progress">0..1.</param>
/// <param name="Rows">Key items: multi-day spans (Meta "12–14" or "28 Oct – 2 Nov") then upcoming open tasks (Meta "16 Nov"), at most 4.</param>
/// <param name="MoreCount">"+N more" spans not listed; 0 = none.</param>
/// <param name="ZoomTarget">Month view anchor (today for the current month, else the 1st).</param>
public sealed record YearMonth(int Month, string Name, bool IsCurrent, bool IsPast, string Stat, double Progress, IReadOnlyList<TaskRow> Rows, int MoreCount, DateOnly ZoomTarget);

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
