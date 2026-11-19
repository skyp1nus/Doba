using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Store;
using Doba.Core.Time;

namespace Doba.Core.Projections;

/// <summary>
/// Pure "what goes where" functions mirroring the prototype's inboxData / todayData / dayData / weekData /
/// monthData / yearData. Each must finish in under 5 ms against a 10,000-task store.
/// Sorting: single-day lists use the prototype's byTime (timed first, by time, then priority desc, then Id);
/// spans use byDate (Date, then Id).
/// </summary>
public static class Projector
{
    /// <summary>The prototype's byTime, made total with Id as the final key.</summary>
    internal static readonly Comparison<TaskItem> ByTime = (x, y) =>
    {
        var c = (x.Time is null ? 1 : 0) - (y.Time is null ? 1 : 0);
        if (c != 0)
        {
            return c;
        }

        if (x.Time is { } tx && y.Time is { } ty)
        {
            c = DateMath.Minutes(tx).CompareTo(DateMath.Minutes(ty));
            if (c != 0)
            {
                return c;
            }
        }

        c = ((int)y.Priority).CompareTo((int)x.Priority);
        return c != 0 ? c : x.Id.CompareTo(y.Id);
    };

    /// <summary>The prototype's byDate (stable), made total with Id: undated tasks sort last.</summary>
    internal static readonly Comparison<TaskItem> ByDate = (x, y) =>
    {
        if (x.Date != y.Date)
        {
            if (x.Date is null)
            {
                return 1;
            }

            if (y.Date is null)
            {
                return -1;
            }

            return x.Date.Value.CompareTo(y.Date.Value);
        }

        return x.Id.CompareTo(y.Id);
    };

    private static readonly Comparison<TaskItem> s_byId = (x, y) => x.Id.CompareTo(y.Id);

    private static readonly TaskRow[] s_noRows = [];

    // ---------- Inbox ----------

    public static InboxModel Inbox(ITaskReader store, ProjectionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ctx);
        var today = ctx.Today;

        // Left: undated, open by priority (ASAP first), then Done.
        var byPrio = new List<TaskRow>?[5];
        var doneRows = new List<TaskRow>();
        foreach (var t in store.Undated)
        {
            if (t.Done)
            {
                doneRows.Add(Row(t, ctx));
            }
            else
            {
                (byPrio[(int)t.Priority] ??= []).Add(Row(t, ctx));
            }
        }

        var left = new List<TaskGroup>(6);
        ReadOnlySpan<Priority> order = [Priority.Asap, Priority.High, Priority.Medium, Priority.Low, Priority.None];
        foreach (var p in order)
        {
            if (byPrio[(int)p] is { } rows)
            {
                left.Add(new TaskGroup(PriorityGroupKey(p), PriorityGroupLabel(p), Count(rows.Count), p == Priority.Asap ? GroupTone.Warning : GroupTone.Muted, rows));
            }
        }

        if (left.Count == 0)
        {
            left.Add(new TaskGroup("p-none", "No priority", "0", GroupTone.Muted, s_noRows));
        }

        if (doneRows.Count > 0)
        {
            left.Add(new TaskGroup("done", "Done", Count(doneRows.Count), GroupTone.Muted, doneRows));
        }

        // Right: open dated tasks — Overdue, each day of [today, today+13] that has tasks, then Later.
        const int horizonDays = 13;
        var horizon = today.AddDays(horizonDays);
        var openCount = 0;
        var overdue = new List<TaskItem>();
        var days = new List<TaskItem>?[horizonDays + 1];
        var laterCount = 0;
        TaskItem? firstLater = null;
        foreach (var t in store.All)
        {
            if (t.Date is not { } date || t.Done)
            {
                continue;
            }

            openCount++;
            var last = t.End ?? date;
            if (last < today)
            {
                overdue.Add(t);
            }
            else if (date > horizon)
            {
                laterCount++;
                if (firstLater is null || ByDate(t, firstLater) < 0)
                {
                    firstLater = t;
                }
            }
            else
            {
                // A span that started before today shows under Today; everything else under its start day.
                var slot = date < today ? 0 : DateMath.DaysBetween(today, date);
                (days[slot] ??= []).Add(t);
            }
        }

        var right = new List<TaskGroup>();
        if (overdue.Count > 0)
        {
            overdue.Sort(ByDate);
            right.Add(new TaskGroup("over", "Overdue", Count(overdue.Count), GroupTone.Warning, OverdueRows(overdue, ctx, false)));
        }

        for (var i = 0; i <= horizonDays; i++)
        {
            if (days[i] is not { } list)
            {
                continue;
            }

            list.Sort(ByTime);
            var day = today.AddDays(i);
            var rows = new TaskRow[list.Count];
            for (var k = 0; k < list.Count; k++)
            {
                var t = list[k];
                rows[k] = Row(t, ctx, t.End is { } end ? DaysText(t.Date!.Value, end) : TimeText(t));
            }

            var label = i == 0 ? "Today" : (i == 1 ? "Tomorrow" : DateMath.ShortWithWeekday(day));
            right.Add(new TaskGroup(DateMath.Iso(day), label, Count(rows.Length), i == 0 ? GroupTone.Accent : GroupTone.Muted, rows, new NavTarget(AppView.Day, day)));
        }

        if (firstLater is not null)
        {
            right.Add(new TaskGroup("later", "Later", Count(laterCount) + " tasks", GroupTone.Muted, s_noRows, new NavTarget(AppView.Month, firstLater.Date!.Value)));
        }

        return new InboxModel(left, right, Count(openCount) + " open");
    }

    // ---------- Today ----------

    public static TodayModel Today(ITaskReader store, ProjectionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ctx);
        var today = ctx.Today;
        var dayTasks = store.OnDate(today);

        var overdue = new List<TaskItem>();
        foreach (var t in store.All)
        {
            if (t.Date is not null && !t.Done && (t.End ?? t.Date.Value) < today)
            {
                overdue.Add(t);
            }
        }

        overdue.Sort(ByDate);
        var ongoing = store.SpansOverlapping(today, today).ToList();
        ongoing.Sort(s_byId);

        var sched = new List<TaskItem>();
        var anytime = new List<TaskRow>();
        var done = new List<TaskItem>();
        foreach (var t in dayTasks)
        {
            if (t.Done)
            {
                done.Add(t);
            }
            else if (t.Time is not null)
            {
                sched.Add(t);
            }
            else
            {
                anytime.Add(Row(t, ctx));
            }
        }

        sched.Sort(ByTime);
        done.Sort(ByTime);

        var groups = new List<TaskGroup>(5);
        if (overdue.Count > 0)
        {
            groups.Add(new TaskGroup("over", "Overdue", Count(overdue.Count), GroupTone.Warning, OverdueRows(overdue, ctx, true)));
        }

        if (ongoing.Count > 0)
        {
            var rows = ongoing.ConvertAll(t => Row(t, ctx, DayOfSpan(t, today)));
            groups.Add(new TaskGroup("ongoing", "In progress", Count(rows.Count), GroupTone.Muted, rows));
        }

        if (sched.Count > 0)
        {
            var rows = sched.ConvertAll(t => Row(t, ctx, TimeRange(t.Time!.Value, EffectiveDuration(t))));
            groups.Add(new TaskGroup("sched", "Scheduled", Count(rows.Count), GroupTone.Muted, rows));
        }

        groups.Add(new TaskGroup("any", "Anytime", Count(anytime.Count), GroupTone.Muted, anytime));

        if (done.Count > 0)
        {
            var rows = done.ConvertAll(t => Row(t, ctx, TimeText(t)));
            groups.Add(new TaskGroup("done", "Done", Count(rows.Count), GroupTone.Muted, rows));
        }

        var total = dayTasks.Count;
        var summary = string.Concat(Count(total), " tasks · ", Count(done.Count), " done · ", Count(total - done.Count), " left")
            + (overdue.Count > 0 ? " · " + Count(overdue.Count) + " overdue" : string.Empty);
        var progress = total == 0 ? 0 : (double)done.Count / total;

        var all = dayTasks.ToList();
        all.Sort(ByTime);
        return new TodayModel(summary, progress, groups, Schedule(all, ctx, today));
    }

    // ---------- Day ----------

    public static DayModel Day(ITaskReader store, ProjectionContext ctx, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ctx);

        // The prototype lists covering spans in array (Id) order, not by date.
        var spanTasks = store.SpansOverlapping(date, date).ToList();
        spanTasks.Sort(s_byId);
        var spans = spanTasks.ConvertAll(t => Row(t, ctx, DayOfSpan(t, date)));

        var list = store.OnDate(date).ToList();
        list.Sort(ByTime);
        var items = list.ConvertAll(t => Row(t, ctx));
        var doneN = 0;
        foreach (var t in list)
        {
            if (t.Done)
            {
                doneN++;
            }
        }

        var progress = list.Count == 0 ? string.Empty : string.Concat(Count(doneN), " / ", Count(list.Count), " done");
        return new DayModel(date, progress, spans, items, list.Count == 0 && spans.Count == 0, Schedule(list, ctx, date));
    }

    // ---------- Week ----------

    /// <param name="anchor">Any day in the week to show.</param>
    public static WeekModel Week(ITaskReader store, ProjectionContext ctx, DateOnly anchor, WeekMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(metrics);
        var ws = DateMath.WeekStart(anchor, ctx.WeekStart);
        return new WeekModel(ws, [WeekRowOf(store, ctx, ws, 0, metrics), WeekRowOf(store, ctx, ws, 1, metrics)]);
    }

    private static WeekRow WeekRowOf(ITaskReader store, ProjectionContext ctx, DateOnly ws, int r, WeekMetrics metrics)
    {
        var first = r == 0 ? 0 : 4;
        var dayCount = r == 0 ? 4 : 3;
        var rs = ws.AddDays(first);
        var re = ws.AddDays(first + dayCount - 1);

        var laneEnds = new List<DateOnly>(4);
        var spans = new List<SpanBar>();
        foreach (var t in store.SpansOverlapping(rs, re))
        {
            var start = t.Date!.Value;
            var end = t.End!.Value;
            var st = start < rs ? rs : start;
            var en = end > re ? re : end;
            var lane = AssignLane(laneEnds, st, en);
            var clipL = start < rs;
            var clipR = end > re;
            var meta = (clipL ? "← " : string.Empty) + DaysText(start, end) + (clipR ? " →" : string.Empty);
            spans.Add(new SpanBar(Row(t, ctx, meta), DateMath.DaysBetween(rs, st), DateMath.DaysBetween(st, en) + 1, lane, clipL, clipR));
        }

        var lanes = laneEnds.Count;
        var avail = metrics.RowHeight - metrics.HeaderHeight - lanes * metrics.LaneHeight - 10;
        if (double.IsNaN(avail))
        {
            avail = 0; // unmeasured or broken metrics: no card fits (every comparison with NaN would let all of them in)
        }

        var cells = new List<WeekDayCell>(4);
        for (var c = 0; c < dayCount; c++)
        {
            cells.Add(WeekCell(store, ctx, ws.AddDays(first + c), c, false, avail, metrics));
        }

        if (r == 1)
        {
            cells.Add(WeekCell(store, ctx, ws.AddDays(7), 3, true, avail, metrics));
        }

        return new WeekRow(lanes, cells, spans);
    }

    private static WeekDayCell WeekCell(ITaskReader store, ProjectionContext ctx, DateOnly d, int column, bool preview, double avail, WeekMetrics metrics)
    {
        var list = store.OnDate(d).ToList();
        list.Sort(ByTime);
        var doneN = 0;
        foreach (var t in list)
        {
            if (t.Done)
            {
                doneN++;
            }
        }

        var showN = Fit(list, avail, metrics);
        var rows = new TaskRow[showN];
        for (var k = 0; k < showN; k++)
        {
            rows[k] = Row(list[k], ctx);
        }

        // The preview's progress bar is always empty in the prototype.
        var progress = preview || list.Count == 0 ? 0 : (double)doneN / list.Count;
        return new WeekDayCell(d, column, DateMath.WeekdayShort(d.DayOfWeek), d.Day, DateMath.MonthAbbrev(d.Month),
            !preview && d == ctx.Today, preview, progress, rows, list.Count - showN, list.Count == 0);
    }

    /// <summary>The prototype's <c>fit</c>: how many cards fit, keeping room for "+N more" while more follow.</summary>
    private static int Fit(List<TaskItem> list, double avail, WeekMetrics m)
    {
        double used = 0;
        var n = 0;
        for (var k = 0; k < list.Count; k++)
        {
            var h = m.CardHeightOf(list[k]) + m.CardGap;
            var reserve = k < list.Count - 1 ? m.MoreReserve : 0;
            if (used + h + reserve > avail + m.CardGap)
            {
                break;
            }

            used += h;
            n++;
        }

        return n;
    }

    // ---------- Month ----------

    /// <param name="anchor">Any day in the month to show.</param>
    public static MonthModel Month(ITaskReader store, ProjectionContext ctx, DateOnly anchor, MonthMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(metrics);
        var today = ctx.Today;
        var first = DateMath.FirstOfMonth(anchor);
        var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);
        var gs = DateMath.WeekStart(first, ctx.WeekStart);
        var weekCount = (DateMath.DaysBetween(gs, first) + daysInMonth + 6) / 7;
        var rowHeight = Math.Floor((metrics.AvailableHeight - metrics.HeaderHeight) / weekCount);
        if (!(rowHeight > 0))
        {
            rowHeight = 0; // a body shorter than the header, or NaN: never a negative row
        }

        var weeks = new List<MonthWeek>(weekCount);
        for (var w = 0; w < weekCount; w++)
        {
            var ws = gs.AddDays(w * 7);
            var we = ws.AddDays(6);
            var laneEnds = new List<DateOnly>(2);
            var spans = new List<SpanBar>();
            foreach (var t in store.SpansOverlapping(ws, we))
            {
                var start = t.Date!.Value;
                var end = t.End!.Value;
                var st = start < ws ? ws : start;
                var en = end > we ? we : end;
                var lane = FreeLane(laneEnds, st);
                if (lane > 1)
                {
                    continue;
                }

                SetLane(laneEnds, lane, en);
                spans.Add(new SpanBar(Row(t, ctx), DateMath.DaysBetween(ws, st), DateMath.DaysBetween(st, en) + 1, lane, start < ws, end > we));
            }

            var lanes = laneEnds.Count;
            var room = rowHeight - metrics.DayNumberHeight - lanes * metrics.LaneHeight;

            // No room for even one list line below the lanes: every task goes to "+N more" in the day-number row.
            var headerOnly = !(room >= metrics.MinListRoom);
            var cap = Math.Max(1, (int)Math.Floor((room - 8) / metrics.ItemHeight));
            var cells = new List<MonthDayCell>(7);
            for (var i = 0; i < 7; i++)
            {
                var d = ws.AddDays(i);
                var list = store.OnDate(d).ToList();
                list.Sort(ByTime);
                var showN = headerOnly ? 0 : (list.Count > cap ? cap - 1 : list.Count);
                var items = new TaskRow[showN];
                for (var k = 0; k < showN; k++)
                {
                    items[k] = Row(list[k], ctx);
                }

                cells.Add(new MonthDayCell(d, d.Day, d.Year == first.Year && d.Month == first.Month, d == today, items, list.Count - showN,
                    headerOnly && list.Count > 0));
            }

            var current = ws <= today && today <= we;
            weeks.Add(new MonthWeek(ws, DateMath.WeekNumber(ws), current, current ? today : ws, rowHeight, lanes, cells, spans));
        }

        var labels = DateMath.WeekdayOrder(ctx.WeekStart).Select(DateMath.WeekdayShort).ToArray();
        return new MonthModel(first.Year, first.Month, labels, weeks);
    }

    // ---------- Year ----------

    public static YearModel Year(ITaskReader store, ProjectionContext ctx, int year)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ctx);
        var today = ctx.Today;
        var months = new List<YearMonth>(12);
        var upcoming = new List<TaskItem>();
        for (var m = 1; m <= 12; m++)
        {
            var first = new DateOnly(year, m, 1);
            var last = new DateOnly(year, m, DateTime.DaysInMonth(year, m));
            var isCur = today.Year == year && today.Month == m;
            var isPast = last < today;

            var ev = store.SpansOverlapping(first, last);
            var shownSpans = ev.Count > 4 ? 3 : ev.Count;
            var extra = ev.Count - shownSpans;

            var singleN = 0;
            var doneN = 0;
            upcoming.Clear();
            var wantUpcoming = !isPast && extra == 0;
            for (var d = first; d <= last; d = d.AddDays(1))
            {
                var list = store.OnDate(d);
                singleN += list.Count;
                foreach (var t in list)
                {
                    if (t.Done)
                    {
                        doneN++;
                    }
                    else if (wantUpcoming && d >= today && upcoming.Count < 4 - shownSpans)
                    {
                        upcoming.Add(t);
                    }
                }
            }

            var rows = new List<TaskRow>(4);
            for (var k = 0; k < shownSpans; k++)
            {
                var t = ev[k];
                rows.Add(Row(t, ctx, DateMath.SpanLabel(t.Date!.Value, t.End!.Value, monthInSameMonth: false)));
            }

            foreach (var t in upcoming)
            {
                rows.Add(Row(t, ctx, DateMath.DayMonth(t.Date!.Value)));
            }

            var stat = singleN > 0 ? string.Concat(Count(singleN), " tasks · ", Count(doneN), " done") : "No tasks";
            months.Add(new YearMonth(m, DateMath.MonthName(m), isCur, isPast, stat, singleN == 0 ? 0 : (double)doneN / singleN,
                rows, extra, isCur ? today : first));
        }

        return new YearModel(year, months);
    }

    // ---------- Sidebar ----------

    public static SidebarModel Sidebar(ITaskReader store, ProjectionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ctx);
        var inbox = 0;
        foreach (var t in store.Undated)
        {
            if (!t.Done)
            {
                inbox++;
            }
        }

        var todayN = 0;
        foreach (var t in store.OnDate(ctx.Today))
        {
            if (!t.Done)
            {
                todayN++;
            }
        }

        var tags = store.Tags;
        var counts = new int[tags.Count];
        if (tags.Count > 0)
        {
            var index = new Dictionary<string, int>(tags.Count, StringComparer.Ordinal);
            for (var i = 0; i < tags.Count; i++)
            {
                index.TryAdd(tags[i], i);
            }

            foreach (var t in store.All)
            {
                if (!t.Done && t.Tag is { } tag && index.TryGetValue(tag, out var i))
                {
                    counts[i]++;
                }
            }
        }

        var entries = new TagEntry[tags.Count];
        for (var i = 0; i < tags.Count; i++)
        {
            entries[i] = new TagEntry(tags[i], counts[i], string.Equals(ctx.FilterTag, tags[i], StringComparison.Ordinal));
        }

        return new SidebarModel(inbox, todayN, entries);
    }

    // ---------- Search ----------

    /// <summary>
    /// Case-insensitive substring match on titles across all tasks; open tasks first, then by date (undated last),
    /// then Id. At most <paramref name="limit"/> hits. Context: "Inbox", "Mon 16 Nov", or "20–22 Nov" / "28 Oct – 2 Nov".
    /// </summary>
    public static IReadOnlyList<SearchHit> Search(ITaskReader store, ProjectionContext ctx, string query, int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ctx);
        var q = query?.Trim() ?? string.Empty;
        if (q.Length == 0 || limit <= 0)
        {
            return [];
        }

        // Bounded selection: keep only the best `limit` matches, sorted, while scanning. A one-character query matches
        // nearly every task, so sorting all matches would cost O(n log n) and a large list per keystroke; here most
        // candidates are rejected by one compare with the last kept hit. The order is total (Id last), so the result
        // equals a full sort followed by Take(limit).
        var best = new TaskItem[Math.Min(limit, store.Count)];
        var n = 0;
        if (best.Length > 0)
        {
            foreach (var t in store.All)
            {
                if (!t.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (n == best.Length)
                {
                    if (SearchOrder(t, best[n - 1]) >= 0)
                    {
                        continue;
                    }

                    n--; // the last kept hit drops out
                }

                var k = n;
                while (k > 0 && SearchOrder(t, best[k - 1]) < 0)
                {
                    best[k] = best[k - 1];
                    k--;
                }

                best[k] = t;
                n++;
            }
        }

        var hits = new SearchHit[n];
        for (var i = 0; i < n; i++)
        {
            var t = best[i];
            var context = t.Date is not { } d ? "Inbox"
                : t.End is { } e ? DateMath.SpanLabel(d, e)
                : DateMath.ShortWithWeekday(d);
            hits[i] = new SearchHit(Row(t, ctx), context);
        }

        return hits;
    }

    /// <summary>Search order: open before done, then <see cref="ByDate"/> (undated last, then Id).</summary>
    private static int SearchOrder(TaskItem x, TaskItem y) =>
        x.Done != y.Done ? (x.Done ? 1 : 0) - (y.Done ? 1 : 0) : ByDate(x, y);

    // ---------- helpers ----------

    /// <summary>A task as a row: tag label "#work", time "HH:mm", dimmed when a different tag filter is active.</summary>
    internal static TaskRow Row(TaskItem t, ProjectionContext ctx, string meta = "", bool canMove = false) =>
        new(t.Id, t.Title, t.Done, t.Priority, TagLabel(t.Tag), TimeText(t), meta, t.IsMultiDay, IsDimmed(t, ctx), canMove);

    internal static bool IsDimmed(TaskItem t, ProjectionContext ctx) =>
        !string.IsNullOrEmpty(ctx.FilterTag) && !string.Equals(t.Tag, ctx.FilterTag, StringComparison.Ordinal);

    internal static string TagLabel(string? tag) => string.IsNullOrEmpty(tag) ? string.Empty : "#" + tag;

    private static string TimeText(TaskItem t) => t.Time is { } tm ? DateMath.Hhmm(tm) : string.Empty;

    private static string Count(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string DaysText(DateOnly start, DateOnly end) => Count(DateMath.DaysBetween(start, end) + 1) + " days";

    private static string DayOfSpan(TaskItem t, DateOnly d) =>
        string.Concat("Day ", Count(DateMath.DaysBetween(t.Date!.Value, d) + 1), " of ", Count(DateMath.DaysBetween(t.Date.Value, t.End!.Value) + 1));

    private static int EffectiveDuration(TaskItem t) => t.DurationMinutes > 0 ? t.DurationMinutes : 30;

    private static string TimeRange(TimeOnly start, int durationMinutes) =>
        string.Concat(DateMath.Hhmm(start), " – ", DateMath.Hhmm(DateMath.Minutes(start) + durationMinutes));

    private static List<TaskRow> OverdueRows(List<TaskItem> overdue, ProjectionContext ctx, bool canMove) =>
        overdue.ConvertAll(t => Row(t, ctx,
            t.End is { } end ? DateMath.SpanLabel(t.Date!.Value, end) : DateMath.ShortWithWeekday(t.Date!.Value),
            canMove && !t.IsMultiDay));

    private static string PriorityGroupKey(Priority p) => p switch
    {
        Priority.Asap => "p-asap",
        Priority.High => "p-high",
        Priority.Medium => "p-med",
        Priority.Low => "p-low",
        _ => "p-none",
    };

    private static string PriorityGroupLabel(Priority p) => p == Priority.None ? "No priority" : p.DisplayName();

    /// <summary>Greedy lane: the first lane whose last occupied day is before <paramref name="st"/>.</summary>
    private static int FreeLane(List<DateOnly> laneEnds, DateOnly st)
    {
        var lane = 0;
        while (lane < laneEnds.Count && laneEnds[lane] >= st)
        {
            lane++;
        }

        return lane;
    }

    private static void SetLane(List<DateOnly> laneEnds, int lane, DateOnly en)
    {
        if (lane == laneEnds.Count)
        {
            laneEnds.Add(en);
        }
        else
        {
            laneEnds[lane] = en;
        }
    }

    private static int AssignLane(List<DateOnly> laneEnds, DateOnly st, DateOnly en)
    {
        var lane = FreeLane(laneEnds, st);
        SetLane(laneEnds, lane, en);
        return lane;
    }

    /// <summary>Timeline blocks of one day's single-day tasks (already sorted by time); before 07:00 is omitted.</summary>
    private static ScheduleModel Schedule(List<TaskItem> sorted, ProjectionContext ctx, DateOnly date)
    {
        var blocks = new List<ScheduleBlock>();
        foreach (var t in sorted)
        {
            if (t.Time is not { } time)
            {
                continue;
            }

            var start = DateMath.Minutes(time);
            if (start < ScheduleModel.FirstHour * 60)
            {
                continue;
            }

            var dur = EffectiveDuration(t);
            blocks.Add(new ScheduleBlock(Row(t, ctx), start, dur, TimeRange(time, dur)));
        }

        var isToday = date == ctx.Today;
        return new ScheduleModel(blocks, isToday ? DateMath.Minutes(ctx.Now) : null, isToday ? DateMath.Hhmm(ctx.Now) : null);
    }
}
