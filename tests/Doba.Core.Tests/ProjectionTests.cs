using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using Doba.Core.Store;

namespace Doba.Core.Tests;

public class ProjectionTests
{
    private static readonly DateOnly Today = DemoData.Today;

    private static TaskStore DemoStore() => new(DemoData.Tasks(), DemoData.Tags, NullTaskPersistence.Instance);

    private static ProjectionContext Ctx(DateOnly? today = null, DayOfWeek weekStart = DayOfWeek.Monday, string? filter = null) =>
        new(today ?? Today, DemoData.Now, weekStart, filter);

    private static TaskItem T(long id, string title, DateOnly? date = null, DateOnly? end = null, TimeOnly? time = null, bool done = false, string? tag = null, Priority prio = Priority.None) =>
        new(id, title, string.Empty, date, end, time, 0, prio, tag, done);

    [Fact]
    public void Sunday_start_week_layout()
    {
        var w = Projector.Week(DemoStore(), Ctx(weekStart: DayOfWeek.Sunday), new DateOnly(2026, 11, 18), WeekMetrics.Design());
        Assert.Equal(new DateOnly(2026, 11, 15), w.WeekStart);
        Assert.Equal(["Sun", "Mon", "Tue", "Wed"], w.Rows[0].Cells.Select(c => c.Weekday));
        Assert.Equal([15, 16, 17, 18], w.Rows[0].Cells.Select(c => c.DayNumber));
        Assert.Equal(["Thu", "Fri", "Sat", "Sun"], w.Rows[1].Cells.Select(c => c.Weekday));
        Assert.Equal([19, 20, 21, 22], w.Rows[1].Cells.Select(c => c.DayNumber));
        Assert.Equal([0, 1, 2, 3], w.Rows[1].Cells.Select(c => c.Column));
        Assert.Equal([false, false, false, true], w.Rows[1].Cells.Select(c => c.IsPreview));
        Assert.True(w.Rows[0].Cells[0].IsToday);
        Assert.Equal(0, w.Rows[1].Cells[3].Progress);

        // "Write a blog post" 18–19 Nov crosses the row break; "Weekend trip" 20–22 is cut at Saturday 21.
        var blog = Assert.Single(w.Rows[0].Spans);
        Assert.Equal((3, 1, 0, false, true, "2 days →"), (blog.Column, blog.ColumnSpan, blog.Lane, blog.ClipStart, blog.ClipEnd, blog.Row.Meta));
        var row2 = w.Rows[1].Spans.ToDictionary(s => s.Row.Title);
        Assert.Equal((0, 1, true, false, "← 2 days"), (row2["Write a blog post about the migration"].Column, row2["Write a blog post about the migration"].ColumnSpan, row2["Write a blog post about the migration"].ClipStart, row2["Write a blog post about the migration"].ClipEnd, row2["Write a blog post about the migration"].Row.Meta));
        var trip = row2["Weekend trip to Kraków"];
        Assert.Equal((1, 2, 0, false, true, "3 days →"), (trip.Column, trip.ColumnSpan, trip.Lane, trip.ClipStart, trip.ClipEnd, trip.Row.Meta));
    }

    [Fact]
    public void Sunday_start_month_layout()
    {
        var m = Projector.Month(DemoStore(), Ctx(weekStart: DayOfWeek.Sunday), Today, new MonthMetrics());
        Assert.Equal(["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"], m.WeekdayLabels);
        Assert.Equal(5, m.Weeks.Count);                             // 1 Nov 2026 is a Sunday
        Assert.Equal(new DateOnly(2026, 11, 1), m.Weeks[0].WeekStart);
        Assert.Equal(Math.Floor(806.0 / 5), m.Weeks[0].RowHeight);
        Assert.Equal([45, 46, 47, 48, 49], m.Weeks.Select(w => w.WeekNumber));
        var current = Assert.Single(m.Weeks, w => w.IsCurrent);
        Assert.Equal(new DateOnly(2026, 11, 15), current.WeekStart);
        Assert.Equal(Today, current.ZoomTarget);
        Assert.Equal(new DateOnly(2026, 11, 22), m.Weeks[3].ZoomTarget);
        Assert.True(current.Days[0].IsToday);
        Assert.All(m.Weeks, w => Assert.Equal(7, w.Days.Count));
        Assert.True(m.Weeks[4].Days[1].InMonth);                    // Mon 30 Nov is in, Tue 1 Dec is not
        Assert.False(m.Weeks[4].Days[2].InMonth);
        Assert.Equal(new DateOnly(2026, 12, 1), m.Weeks[4].Days[2].Date);

        var monday = Projector.Month(DemoStore(), Ctx(), Today, new MonthMetrics());
        Assert.Equal(6, monday.Weeks.Count);
        Assert.Equal(new DateOnly(2026, 10, 26), monday.Weeks[0].WeekStart);
    }

    [Fact]
    public void Month_keeps_at_most_two_span_lanes_and_fits_items()
    {
        var d = new DateOnly(2026, 6, 10);
        var tasks = new List<TaskItem>
        {
            T(1, "s1", d, d.AddDays(2)),
            T(2, "s2", d, d.AddDays(2)),
            T(3, "s3", d, d.AddDays(2)),
        };
        for (var i = 0; i < 9; i++)
        {
            tasks.Add(T(10 + i, "t" + i, d));
        }

        var store = new TaskStore(tasks, [], NullTaskPersistence.Instance);
        var m = Projector.Month(store, Ctx(), d, new MonthMetrics());
        var week = m.Weeks.Single(w => w.WeekStart <= d && d <= w.WeekStart.AddDays(6));
        Assert.Equal(2, week.LaneCount);
        Assert.Equal([1L, 2], week.Spans.Select(s => s.Row.Id));
        // 5 weeks in June 2026 (Monday start): H = floor(806/5) = 161, cap = floor((161 − 34 − 48 − 8)/25) = 2 → 1 item +
        // "+9 more": the 8 items that do not fit and s3, which needs a third lane (DECISIONS "Month third lane").
        var cell = week.Days.Single(x => x.Date == d);
        Assert.Single(cell.Items);
        Assert.Equal(9, cell.MoreCount);

        var tall = Projector.Month(store, Ctx(), d, new MonthMetrics(AvailableHeight: 1600));
        var tallCell = tall.Weeks.Single(w => w.WeekStart <= d && d <= w.WeekStart.AddDays(6)).Days.Single(x => x.Date == d);
        Assert.True(tallCell.Items.Count > cell.Items.Count);
        Assert.Equal(9 + 1, tallCell.Items.Count + tallCell.MoreCount);
    }

    [Fact]
    public void Month_counts_a_span_that_needs_a_third_lane_in_more_of_each_day_it_covers()
    {
        // Wed 10 – Fri 12 June 2026: three overlapping spans; s3 gets no lane (the prototype drops it silently).
        var d = new DateOnly(2026, 6, 10);
        var store = new TaskStore(
        [
            T(1, "s1", d, d.AddDays(2)),
            T(2, "s2", d.AddDays(-1), d.AddDays(1)),
            T(3, "s3", d, d.AddDays(4)),        // Wed 10 – Sun 14: covers five days of the week
            T(4, "later", d.AddDays(3), d.AddDays(4)), // Sat 13 – Sun 14: lane 0 is free again there
            T(10, "item", d),
        ], [], NullTaskPersistence.Instance);
        var week = Projector.Month(store, Ctx(), d, new MonthMetrics()).Weeks.Single(w => w.WeekStart <= d && d <= w.WeekStart.AddDays(6));
        Assert.Equal(2, week.LaneCount);
        Assert.Equal([2L, 1, 4], week.Spans.Select(s => s.Row.Id)); // byDate: s2 (9th) first
        Assert.DoesNotContain(week.Spans, s => s.Row.Id == 3);

        // Mon 8 – Sun 14: s3 is counted on Wed–Sun only; Wed also has its item, which still fits (cap 2: 1 item + "+1 more").
        Assert.Equal([0, 0, 1, 1, 1, 1, 1], week.Days.Select(c => c.MoreCount));
        var wednesday = week.Days[2];
        Assert.Equal(["item"], wednesday.Items.Select(r => r.Title));
        Assert.Equal("+1 more", wednesday.MoreLabel);
        Assert.Empty(week.Days[3].Items);

        // A row with no list line counts it in the day-number row.
        var tiny = Projector.Month(store, Ctx(), d, new MonthMetrics(AvailableHeight: 26 + (5 * 90))).Weeks
            .Single(w => w.WeekStart <= d && d <= w.WeekStart.AddDays(6));
        Assert.Equal((true, 2, 0), (tiny.Days[2].MoreInHeader, tiny.Days[2].MoreCount, tiny.Days[2].Items.Count));
        Assert.Equal((true, 1), (tiny.Days[3].MoreInHeader, tiny.Days[3].MoreCount));
        Assert.False(tiny.Days[0].MoreInHeader);

        // With two lanes or fewer nothing changes (the golden month cases are the prototype's output).
        var two = new TaskStore([T(1, "s1", d, d.AddDays(2)), T(2, "s2", d, d.AddDays(1)), T(10, "item", d)], [], NullTaskPersistence.Instance);
        var plain = Projector.Month(two, Ctx(), d, new MonthMetrics()).Weeks.Single(w => w.WeekStart <= d && d <= w.WeekStart.AddDays(6));
        Assert.All(plain.Days, c => Assert.Equal(0, c.MoreCount));
    }

    [Fact]
    public void Week_row_shows_the_lanes_that_fit_and_counts_hidden_bars_in_more()
    {
        // Mon 8 – Tue 9 June 2026: five overlapping spans; one single-day task on Wednesday.
        var d = new DateOnly(2026, 6, 8);
        var tasks = Enumerable.Range(1, 5).Select(i => T(i, "span " + i, d, d.AddDays(1))).ToList();
        tasks.Add(T(20, "wednesday", d.AddDays(2)));
        var store = new TaskStore(tasks, [], NullTaskPersistence.Instance);

        // 264 px rows: 264 − 86 − 10 − 26 = 142 → 3 lanes of 36.
        var metrics = WeekMetrics.Design(264);
        Assert.Equal(3, metrics.VisibleLanes(5));
        var row = Projector.Week(store, Ctx(), d, metrics).Rows[0];
        Assert.Equal(3, row.LaneCount);
        Assert.Equal([1L, 2, 3], row.Spans.Select(s => s.Row.Id));
        Assert.All(row.Spans, s => Assert.InRange(s.Lane, 0, 2));

        // Two hidden bars on Mon–Tue: counted in "+N more"; those days have no task but are not "Nothing here".
        Assert.Equal([2, 2, 0, 0], row.Cells.Select(c => c.MoreCount));
        Assert.Equal([false, false, false, true], row.Cells.Select(c => c.Empty));
        Assert.Equal("+2 more", row.Cells[0].MoreLabel);

        // The card list gets the room of the visible lanes only: avail = 264 − 86 − 3 × 36 − 10 = 60, so Wednesday's
        // card fits (with all five lanes, 264 − 86 − 180 − 10 < 0, it would not).
        Assert.Equal(["wednesday"], row.Cells[2].Tasks.Select(t => t.Title));

        // At the design size every lane fits and nothing is counted.
        var design = Projector.Week(store, Ctx(), d, WeekMetrics.Design()).Rows[0];
        Assert.Equal(5, design.LaneCount);
        Assert.Equal(5, design.Spans.Count);
        Assert.Equal([0, 0, 0, 0], design.Cells.Select(c => c.MoreCount));
        Assert.Equal([true, true, false, true], design.Cells.Select(c => c.Empty));
    }

    [Theory]
    [InlineData(3, 403, 3)]   // the design's row: everything fits
    [InlineData(7, 403, 7)]   // 86 + 7 × 36 = 338 ≤ 403 − 10 − 26
    [InlineData(8, 403, 7)]
    [InlineData(5, 254, 3)]   // 1100 × 700: 254 − 86 − 10 − 26 = 132 → 3 lanes
    [InlineData(2, 100, 0)]   // not even one lane leaves room for "+N more"
    [InlineData(4, double.NaN, 4)] // unmeasured: every lane
    [InlineData(0, 100, 0)]
    public void Visible_lanes_leave_room_for_the_more_line(int lanes, double rowHeight, int visible) =>
        Assert.Equal(visible, WeekMetrics.Design(rowHeight).VisibleLanes(lanes));

    [Fact]
    public void A_hidden_bar_keeps_room_for_more_below_the_last_card()
    {
        // Three spans on Mon–Tue and one card on Monday, in a 205 px row: lanes floor((205 − 122) / 36) = 2 of 3, so one
        // bar is hidden; avail = 205 − 86 − 2 × 36 − 10 = 37. The card alone would fit (42 ≤ 37 + 6), but not with the
        // "+N more" line the hidden bar needs (42 + 26 > 43): it goes behind "+2 more" instead of pushing the link out.
        var d = new DateOnly(2026, 6, 8);
        var tasks = Enumerable.Range(1, 3).Select(i => T(i, "span " + i, d, d.AddDays(1))).ToList();
        tasks.Add(T(10, "card", d));
        var store = new TaskStore(tasks, [], NullTaskPersistence.Instance);

        var row = Projector.Week(store, Ctx(), d, WeekMetrics.Design(205)).Rows[0];
        Assert.Equal(2, row.LaneCount);
        var monday = row.Cells[0];
        Assert.Empty(monday.Tasks);
        Assert.Equal(2, monday.MoreCount);
        Assert.False(monday.Empty);

        // Without a hidden bar the same card fits (the prototype's rule: no reserve after the last card).
        var two = new TaskStore([.. tasks.Where(t => t.Id != 3)], [], NullTaskPersistence.Instance);
        var plain = Projector.Week(two, Ctx(), d, WeekMetrics.Design(205)).Rows[0].Cells[0];
        Assert.Equal(["card"], plain.Tasks.Select(t => t.Title));
        Assert.Equal(0, plain.MoreCount);
    }

    [Fact]
    public void Smaller_week_row_shows_fewer_cards()
    {
        var d = new DateOnly(2026, 6, 10);
        var store = new TaskStore(Enumerable.Range(1, 8).Select(i => T(i, "Task " + i, d)), [], NullTaskPersistence.Instance);
        WeekDayCell Cell(WeekMetrics m) => Projector.Week(store, Ctx(), d, m).Rows.SelectMany(r => r.Cells).Single(c => c.Date == d && !c.IsPreview);

        // 403: avail = 403 − 86 − 10 = 307; six 42 px cards + the 26 px "+N more" reserve fit.
        var design = Cell(WeekMetrics.Design());
        Assert.Equal(6, design.Tasks.Count);
        Assert.Equal(2, design.MoreCount);

        var small = Cell(WeekMetrics.Design(250));
        Assert.Equal(3, small.Tasks.Count);
        Assert.Equal(5, small.MoreCount);

        var tiny = Cell(WeekMetrics.Design(100));
        Assert.Empty(tiny.Tasks);
        Assert.Equal(8, tiny.MoreCount);

        // Custom card heights from the App's measurement are used.
        var tallCards = Cell(new WeekMetrics(403, _ => 80));
        Assert.Equal(3, tallCards.Tasks.Count);

        // The last card needs no reserve: seven cards fit exactly when there are seven.
        var seven = new TaskStore(Enumerable.Range(1, 7).Select(i => T(i, "Task " + i, d)), [], NullTaskPersistence.Instance);
        var c7 = Projector.Week(seven, Ctx(), d, WeekMetrics.Design()).Rows.SelectMany(r => r.Cells).Single(c => c.Date == d);
        Assert.Equal(7, c7.Tasks.Count);
        Assert.Equal(0, c7.MoreCount);
    }

    [Fact]
    public void Midnight_reprojects_with_the_new_today()
    {
        var store = DemoStore();
        var before = Projector.Today(store, Ctx());
        var after = Projector.Today(store, Ctx(new DateOnly(2026, 11, 16)));

        Assert.Equal("6 tasks · 4 done · 2 left · 3 overdue", before.Summary);
        Assert.Equal("2 tasks · 0 done · 2 left · 5 overdue", after.Summary);
        var overdue = after.Groups.Single(g => g.Key == "over");
        Assert.Contains(overdue.Items, r => r.Title == "Water the plants and repot the ficus" && r.CanMoveToToday && r.Meta == "Sun 15 Nov");
        Assert.Equal(["Team sync — sprint review"], after.Groups.Single(g => g.Key == "sched").Items.Select(r => r.Title));

        Assert.Equal(2, Projector.Sidebar(store, Ctx()).TodayCount);
        Assert.Equal(2, Projector.Sidebar(store, Ctx(new DateOnly(2026, 11, 16))).TodayCount);
        Assert.Equal(0, Projector.Sidebar(store, Ctx(new DateOnly(2026, 11, 13))).TodayCount);
        Assert.Equal(1, Projector.Sidebar(store, Ctx(new DateOnly(2026, 11, 14))).TodayCount);

        // The now-line follows "today".
        Assert.NotNull(Projector.Day(store, Ctx(), Today).Schedule.NowLabel);
        Assert.Null(Projector.Day(store, Ctx(new DateOnly(2026, 11, 16)), Today).Schedule.NowLabel);
        Assert.Equal(22 * 60 + 26, Projector.Day(store, Ctx(new DateOnly(2026, 11, 16)), new DateOnly(2026, 11, 16)).Schedule.NowMinutes);

        var week = Projector.Week(store, Ctx(new DateOnly(2026, 11, 16)), Today, WeekMetrics.Design());
        Assert.DoesNotContain(week.Rows.SelectMany(r => r.Cells), c => c.IsToday);
        var next = Projector.Week(store, Ctx(new DateOnly(2026, 11, 16)), new DateOnly(2026, 11, 16), WeekMetrics.Design());
        Assert.True(next.Rows[0].Cells[0].IsToday);
    }

    [Fact]
    public void Overdue_span_across_months_uses_both_months()
    {
        var store = new TaskStore([T(1, "Trip", new DateOnly(2026, 10, 28), new DateOnly(2026, 11, 2)), T(2, "Old", new DateOnly(2026, 11, 3))], [], NullTaskPersistence.Instance);
        var today = Projector.Today(store, Ctx());
        var over = today.Groups.Single(g => g.Key == "over");
        Assert.Equal(["28 Oct – 2 Nov", "Tue 3 Nov"], over.Items.Select(r => r.Meta));
        Assert.Equal([false, true], over.Items.Select(r => r.CanMoveToToday));
        var inbox = Projector.Inbox(store, Ctx());
        Assert.Equal(["28 Oct – 2 Nov", "Tue 3 Nov"], inbox.Right.Single(g => g.Key == "over").Items.Select(r => r.Meta));
        Assert.All(inbox.Right.Single(g => g.Key == "over").Items, r => Assert.False(r.CanMoveToToday));
    }

    [Fact]
    public void Inbox_scheduled_column()
    {
        var store = new TaskStore(
        [
            T(1, "ongoing", Today.AddDays(-2), Today.AddDays(1)),
            T(2, "today timed", Today, time: new TimeOnly(9, 0)),
            T(3, "tomorrow", Today.AddDays(1)),
            T(4, "day 13", Today.AddDays(13)),
            T(5, "day 14", Today.AddDays(14)),
            T(6, "later span", Today.AddDays(20), Today.AddDays(22)),
            T(7, "done", Today, done: true),
            T(8, "inbox", prio: Priority.High),
        ], [], NullTaskPersistence.Instance);
        var m = Projector.Inbox(store, Ctx());
        Assert.Equal("6 open", m.ScheduledCount);
        Assert.Equal(["Today", "Tomorrow", "Sat 28 Nov", "Later"], m.Right.Select(g => g.Label));
        Assert.Equal([GroupTone.Accent, GroupTone.Muted, GroupTone.Muted, GroupTone.Muted], m.Right.Select(g => g.Tone));
        Assert.Equal(["09:00", "4 days"], m.Right[0].Items.Select(r => r.Meta));
        Assert.Equal(new NavTarget(AppView.Day, Today.AddDays(1)), m.Right[1].Link);
        Assert.Equal("2026-11-16", m.Right[1].Key);
        var later = m.Right[3];
        Assert.Equal(("2 tasks", 0), (later.Count, later.Items.Count));
        Assert.Equal(new NavTarget(AppView.Month, Today.AddDays(14)), later.Link);
        Assert.Equal(["High"], m.Left.Select(g => g.Label));

        var empty = Projector.Inbox(new TaskStore([], [], NullTaskPersistence.Instance), Ctx());
        var only = Assert.Single(empty.Left);
        Assert.Equal(("p-none", "No priority", "0"), (only.Key, only.Label, only.Count));
        Assert.Empty(empty.Right);
        Assert.Equal("0 open", empty.ScheduledCount);
    }

    [Fact]
    public void Filter_dims_other_tags()
    {
        var rows = Projector.Day(DemoStore(), Ctx(filter: "work"), Today).Items;
        Assert.All(rows, r => Assert.Equal(r.TagLabel != "#work", r.Dimmed));
        Assert.Contains(rows, r => r.Dimmed);
        Assert.DoesNotContain(Projector.Day(DemoStore(), Ctx(), Today).Items, r => r.Dimmed);
        var sidebar = Projector.Sidebar(DemoStore(), Ctx(filter: "work"));
        Assert.Equal([true, false, false], sidebar.Tags.Select(t => t.Active));
    }

    [Fact]
    public void Rows_are_value_equal_for_diffing()
    {
        var store = DemoStore();
        var a = Projector.Week(store, Ctx(), Today, WeekMetrics.Design());
        var b = Projector.Week(store, Ctx(), Today, WeekMetrics.Design());
        Assert.Equal(a.Rows[0].Cells[0].Tasks, b.Rows[0].Cells[0].Tasks);
        var id = a.Rows[0].Cells[0].Tasks[0].Id;
        store.ToggleDone(id);
        var c = Projector.Week(store, Ctx(), Today, WeekMetrics.Design());
        var changed = c.Rows[0].Cells[0].Tasks.Single(r => r.Id == id);
        Assert.NotEqual(a.Rows[0].Cells[0].Tasks.Single(r => r.Id == id), changed);
        Assert.Equal(a.Rows[0].Cells[0].Tasks.Where(r => r.Id != id), c.Rows[0].Cells[0].Tasks.Where(r => r.Id != id));
    }

    [Fact]
    public void Search_orders_open_first_then_by_date_and_labels_context()
    {
        var store = new TaskStore(
        [
            T(1, "Pay BILLS", Today.AddDays(3)),
            T(2, "pay bills", Today.AddDays(-30), done: true),
            T(3, "Bills archive"),
            T(4, "Bills trip", new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 22)),
            T(5, "Bills crossing", new DateOnly(2026, 10, 28), new DateOnly(2026, 11, 2)),
            T(6, "Bills too", Today.AddDays(3)),
            T(7, "Nothing"),
        ], [], NullTaskPersistence.Instance);
        var hits = Projector.Search(store, Ctx(), "  bills ");
        Assert.Equal([5L, 1, 6, 4, 3, 2], hits.Select(h => h.Row.Id));
        Assert.Equal(["28 Oct – 2 Nov", "Wed 18 Nov", "Wed 18 Nov", "20–22 Nov", "Inbox", "Fri 16 Oct"], hits.Select(h => h.Context));
        Assert.Equal(2, Projector.Search(store, Ctx(), "bills", limit: 2).Count);
        Assert.Empty(Projector.Search(store, Ctx(), "   "));
        Assert.Empty(Projector.Search(store, Ctx(), "zzz"));
    }

    [Fact]
    public void Search_with_more_hits_than_the_limit_keeps_the_best_in_order()
    {
        var rnd = new Random(11);
        var tasks = new List<TaskItem>();
        for (var i = 1; i <= 400; i++)
        {
            var roll = rnd.Next(10);
            DateOnly? date = roll < 2 ? null : Today.AddDays(rnd.Next(-60, 60));
            DateOnly? end = date is { } d && roll == 9 ? d.AddDays(1 + rnd.Next(4)) : null;
            tasks.Add(T(i, (i % 7 == 0 ? "Other " : "Match ") + i, date, end, done: rnd.Next(3) == 0));
        }

        var store = new TaskStore(tasks, [], NullTaskPersistence.Instance);
        var expected = tasks
            .Where(t => t.Title.Contains("match", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Done)
            .ThenBy(t => t.Date is null)
            .ThenBy(t => t.Date)
            .ThenBy(t => t.Id)
            .Select(t => t.Id)
            .ToList();
        Assert.True(expected.Count > 300);

        var hits = Projector.Search(store, Ctx(), "MATCH");
        Assert.Equal(50, hits.Count); // the default limit
        Assert.Equal(expected.Take(50), hits.Select(h => h.Row.Id));
        Assert.All(hits, h => Assert.False(h.Row.Done)); // enough open hits: no done one makes the cut

        Assert.Equal(expected.Take(7), Projector.Search(store, Ctx(), "match", limit: 7).Select(h => h.Row.Id));
        Assert.Equal(expected, Projector.Search(store, Ctx(), "match", limit: 1000).Select(h => h.Row.Id));
        Assert.Equal(expected.Take(1), Projector.Search(store, Ctx(), "match", limit: 1).Select(h => h.Row.Id));
        Assert.Empty(Projector.Search(store, Ctx(), "match", limit: 0));
        Assert.Empty(Projector.Search(new TaskStore([], [], NullTaskPersistence.Instance), Ctx(), "m"));
    }

    [Fact]
    public void Search_truncation_keeps_open_before_done_and_dated_before_undated()
    {
        var store = new TaskStore(
        [
            T(1, "x done dated", Today, done: true),
            T(2, "x open undated"),
            T(3, "x open late", Today.AddDays(30)),
            T(4, "x done undated", done: true),
            T(5, "x open early", Today.AddDays(-3)),
            T(6, "x open undated 2"),
        ], [], NullTaskPersistence.Instance);
        Assert.Equal([5L, 3, 2], Projector.Search(store, Ctx(), "x", limit: 3).Select(h => h.Row.Id));
        Assert.Equal([5L, 3, 2, 6, 1], Projector.Search(store, Ctx(), "x", limit: 5).Select(h => h.Row.Id));
    }

    [Fact]
    public void Inbox_later_link_goes_to_the_earliest_later_date()
    {
        var store = new TaskStore(
        [
            T(9, "later b", Today.AddDays(20)),
            T(3, "later far", Today.AddDays(40)),
            T(5, "later a", Today.AddDays(20)),
            T(7, "later span", Today.AddDays(21), Today.AddDays(23)),
            T(2, "later done", Today.AddDays(15), done: true),
        ], [], NullTaskPersistence.Instance);
        var later = Projector.Inbox(store, Ctx()).Right.Single(g => g.Key == "later");
        Assert.Equal("4 tasks", later.Count);
        Assert.Equal(new NavTarget(AppView.Month, Today.AddDays(20)), later.Link);
    }

    [Fact]
    public void Month_row_with_room_for_one_line_shows_one_item_or_only_more()
    {
        var d = new DateOnly(2026, 6, 10); // June 2026, Monday start: 5 weeks
        TaskStore Store(int n) => new(Enumerable.Range(1, n).Select(i => T(i, "t" + i, d)), [], NullTaskPersistence.Instance);
        MonthDayCell Cell(TaskStore s, MonthMetrics m) =>
            Projector.Month(s, Ctx(), d, m).Weeks.Single(w => w.WeekStart <= d && d <= w.WeekStart.AddDays(6)).Days.Single(c => c.Date == d);

        // Row 64 px: room below the day number 30 ≥ 26 → cap = max(1, floor((30 − 8) / 25)) = 1.
        var oneLine = new MonthMetrics(AvailableHeight: 26 + (5 * 64));
        var single = Cell(Store(1), oneLine);
        Assert.Equal((1, 0, false), (single.Items.Count, single.MoreCount, single.MoreInHeader));
        var three = Cell(Store(3), oneLine);
        Assert.Equal((0, 3, false), (three.Items.Count, three.MoreCount, three.MoreInHeader));
    }

    [Fact]
    public void Month_row_without_room_for_a_list_line_counts_every_task_in_the_header()
    {
        var d = new DateOnly(2026, 6, 10);
        TaskStore Store(int n) => new(Enumerable.Range(1, n).Select(i => T(i, "t" + i, d)), [], NullTaskPersistence.Instance);
        MonthDayCell Cell(TaskStore s, MonthMetrics m) =>
            Projector.Month(s, Ctx(), d, m).Weeks.Single(w => w.WeekStart <= d && d <= w.WeekStart.AddDays(6)).Days.Single(c => c.Date == d);

        // Row 55 px: room 21 < 26.
        var tiny = new MonthMetrics(AvailableHeight: 26 + (5 * 55));
        var one = Cell(Store(1), tiny);
        Assert.Equal((0, 1, true), (one.Items.Count, one.MoreCount, one.MoreInHeader));
        var three = Cell(Store(3), tiny);
        Assert.Equal((0, 3, true), (three.Items.Count, three.MoreCount, three.MoreInHeader));
        var other = Projector.Month(Store(1), Ctx(), d, tiny).Weeks.SelectMany(w => w.Days).Where(c => c.Date != d);
        Assert.All(other, c => Assert.Equal((0, 0, false), (c.Items.Count, c.MoreCount, c.MoreInHeader))); // empty days: nothing to show

        // Two span lanes take the room of a taller row: 134 − 34 − 48 = 52 ≥ 26 → a list; 100 − 34 − 48 = 18 → header.
        var spans = new TaskStore([T(1, "s1", d, d.AddDays(1)), T(2, "s2", d, d.AddDays(1)), T(3, "t", d)], [], NullTaskPersistence.Instance);
        Assert.False(Cell(spans, new MonthMetrics(AvailableHeight: 26 + (5 * 134))).MoreInHeader);
        var shortRow = Cell(spans, new MonthMetrics(AvailableHeight: 26 + (5 * 100)));
        Assert.Equal((0, 1, true), (shortRow.Items.Count, shortRow.MoreCount, shortRow.MoreInHeader));

        // The design height never moves anything to the header.
        Assert.All(Projector.Month(DemoStore(), Ctx(), Today, new MonthMetrics()).Weeks.SelectMany(w => w.Days), c => Assert.False(c.MoreInHeader));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(-50)]
    [InlineData(10)]
    public void Month_with_degenerate_height_has_no_negative_rows_and_lists_nothing(double height)
    {
        var d = new DateOnly(2026, 6, 10);
        var store = new TaskStore([T(1, "a", d), T(2, "b", d)], [], NullTaskPersistence.Instance);
        var m = Projector.Month(store, Ctx(), d, new MonthMetrics(AvailableHeight: height));
        Assert.All(m.Weeks, w => Assert.Equal(0, w.RowHeight));
        var cell = m.Weeks.SelectMany(w => w.Days).Single(c => c.Date == d);
        Assert.Equal((0, 2, true), (cell.Items.Count, cell.MoreCount, cell.MoreInHeader));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(-100)]
    public void Week_with_degenerate_row_height_shows_no_cards(double rowHeight)
    {
        var d = new DateOnly(2026, 6, 10);
        var store = new TaskStore(Enumerable.Range(1, 4).Select(i => T(i, "Task " + i, d)), [], NullTaskPersistence.Instance);
        var cell = Projector.Week(store, Ctx(), d, WeekMetrics.Design(rowHeight)).Rows.SelectMany(r => r.Cells).Single(c => c.Date == d && !c.IsPreview);
        Assert.Empty(cell.Tasks);
        Assert.Equal(4, cell.MoreCount);
    }

    [Fact]
    public void Week_fit_uses_the_per_task_card_height_when_given()
    {
        var d = new DateOnly(2026, 6, 10);

        // Same title twice: one card is tall (its tag and time squeeze the title onto two lines), one is short.
        var store = new TaskStore(
        [
            T(1, "Standup", d, time: new TimeOnly(9, 0), tag: "work"),
            T(2, "Standup", d),
            T(3, "Standup", d),
            T(4, "Standup", d),
            T(5, "Standup", d),
            T(6, "Standup", d),
            T(7, "Standup", d),
        ], [], NullTaskPersistence.Instance);
        WeekDayCell Cell(WeekMetrics m) => Projector.Week(store, Ctx(), d, m).Rows.SelectMany(r => r.Cells).Single(c => c.Date == d && !c.IsPreview);

        // By title only, every "Standup" has to be fitted with the tall card: avail 307 → 4 × 60 + 26 fit, the 5th not.
        var byTitle = Cell(new WeekMetrics(403, _ => 54));
        Assert.Equal(4, byTitle.Tasks.Count);

        // Per task: one 54 px card and six 36 px ones (60 + 6 × 42 = 312 ≤ 307 + 6) all fit.
        var perTask = Cell(new WeekMetrics(403, _ => 54) { TaskCardHeight = t => t.Tag is null ? 36 : 54 });
        Assert.Equal(7, perTask.Tasks.Count);
        Assert.Equal(0, perTask.MoreCount);
    }

    [Theory]
    [InlineData(4, 0, 1000, 4, 0)]          // room to spare: all four
    [InlineData(3, 2, 200, 3, 2)]           // three items + "+2 more": 3×28 + 2×6 + 6 + 20 = 122 fits
    [InlineData(4, 0, 125, 3, 1)]           // four items, no more: 4×28 + 3×6 = 130 > 125 …
    [InlineData(4, 0, 130, 4, 0)]           // … exactly 130 fits
    [InlineData(4, 0, 129, 3, 1)]           // 129: three items (96) + 6 + 20 = 122, the fourth counted in "+1 more"
    [InlineData(3, 2, 87, 1, 4)]            // two items (62) + 6 + 20 = 88 > 87 → one item …
    [InlineData(3, 2, 88, 2, 3)]            // … 88 fits two; the third span joins "+N more"
    [InlineData(2, 0, 40, 0, 2)]            // not even one item with the link: only "+2 more"
    [InlineData(4, 1, double.NaN, 4, 1)]    // unmeasured: as without metrics
    public void Year_metrics_show_the_items_that_fit_and_count_the_rest(int rows, int more, double room, int shown, int counted)
    {
        var metrics = new YearMetrics(room, ItemHeight: 28, Gap: 6, MoreHeight: 20);
        Assert.Equal((shown, counted), metrics.Fit(rows, more));
    }

    [Fact]
    public void Year_projection_fits_key_items_to_the_measured_room()
    {
        var store = DemoStore();
        var plain = Projector.Year(store, Ctx(), 2026);

        // Without metrics, or with room to spare, the prototype's rows (the golden cases check these).
        var roomy = Projector.Year(store, Ctx(), 2026, _ => new YearMetrics(1000, 28, 6, 20));
        Assert.Equal(plain.Months.Select(m => (m.Rows.Count, m.MoreCount)), roomy.Months.Select(m => (m.Rows.Count, m.MoreCount)));
        Assert.Equal(plain.Months[11].Rows, roomy.Months[11].Rows);

        // December lists four key items ("21–27", "1 Dec", "3 Dec", "5 Dec"); room for two plus "+N more" (88 px) shows the
        // first two and counts the other two. Months without metrics are untouched.
        var fitted = Projector.Year(store, Ctx(), 2026, m => m == 12 ? new YearMetrics(88, 28, 6, 20) : null);
        var dec = fitted.Months[11];
        Assert.Equal(plain.Months[11].Rows.Take(2), dec.Rows);
        Assert.Equal(2, dec.MoreCount);
        Assert.Equal("+2 more", dec.MoreLabel);
        Assert.Equal(plain.Months[10].Rows, fitted.Months[10].Rows);
        Assert.Equal(plain.Months[10].MoreCount, fitted.Months[10].MoreCount);
    }

    [Fact]
    public void Progress_is_a_whole_percent_like_the_prototypes_bars()
    {
        var store = DemoStore();

        // Today: 4 of 6 done → Math.round(66.67) = 67 % (the bar's width), not 0.6667.
        Assert.Equal(0.67, Projector.Today(store, Ctx()).Progress);

        // Week cells and Year months: whole percents too (prototype weekData / yearData `pct`).
        var d = new DateOnly(2026, 6, 10);
        var thirds = new TaskStore([T(1, "a", d, done: true), T(2, "b", d), T(3, "c", d)], [], NullTaskPersistence.Instance);
        var cell = Projector.Week(thirds, Ctx(), d, WeekMetrics.Design()).Rows.SelectMany(r => r.Cells).Single(c => c.Date == d && !c.IsPreview);
        Assert.Equal(0.33, cell.Progress);
        Assert.Equal(0.33, Projector.Year(thirds, Ctx(), 2026).Months[5].Progress);
        var twoThirds = new TaskStore([T(1, "a", d, done: true), T(2, "b", d, done: true), T(3, "c", d)], [], NullTaskPersistence.Instance);
        Assert.Equal(0.67, Projector.Year(twoThirds, Ctx(), 2026).Months[5].Progress);
        Assert.Equal(0.5, Projector.WholePercent(1, 2));
        Assert.Equal(0.01, Projector.WholePercent(1, 200)); // 0.5 % rounds up, as Math.round does
        Assert.Equal(0, Projector.WholePercent(0, 0));
    }

    [Fact]
    public void Records_carry_the_display_labels()
    {
        var store = DemoStore();
        var week = Projector.Week(store, Ctx(), Today, WeekMetrics.Design());
        var monday = week.Rows[0].Cells[0];
        Assert.Equal(("MON", "9", "Open Monday 9 November"), (monday.WeekdayLabel, monday.DayLabel, monday.OpenLabel));
        var preview = week.Rows[1].Cells[3];
        Assert.Equal("Open next Monday 16 November", preview.OpenLabel);
        Assert.Equal("+2 more", week.Rows[1].Cells[2].MoreLabel);

        var month = Projector.Month(store, Ctx(), Today, new MonthMetrics());
        Assert.Equal(["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"], month.WeekdayHeaders);
        Assert.Equal(["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"], Projector.Month(store, Ctx(weekStart: DayOfWeek.Sunday), Today, new MonthMetrics()).WeekdayLabels);
        var w46 = month.Weeks[2];
        Assert.Equal(("W46", "Open week 46"), (w46.Label, w46.OpenLabel));
        var sunday = w46.Days[6];
        Assert.Equal(("15", "Open Sunday 15 November"), (sunday.DayLabel, sunday.OpenLabel));
        Assert.Equal("+" + sunday.MoreCount + " more", sunday.MoreLabel);

        var november = Projector.Year(store, Ctx(), 2026).Months[10];
        Assert.Equal(("Open November", "+3 more"), (november.OpenLabel, november.MoreLabel));
        Assert.Equal(november.Stat.ToUpperInvariant(), november.StatLabel);
        Assert.Equal("NO TASKS", Projector.Year(store, Ctx(), 2030).Months[0].StatLabel);

        // Row buttons (prototype rowItem / vis).
        var row = new TaskRow(1, "Call mom", false, Priority.High, "", "", "", false, false);
        Assert.Equal("Mark done: Call mom", row.ToggleLabel);
        Assert.Equal("Mark not done: Call mom", (row with { Done = true }).ToggleLabel);
        Assert.Equal("Priority: High — change for Call mom", row.PriorityLabel);
        Assert.Equal("Priority: none — change for Call mom", (row with { Priority = Priority.None }).PriorityLabel);
        Assert.Equal(("High priority", "No priority"), (row.PriorityTitle, (row with { Priority = Priority.None }).PriorityTitle));
        Assert.Equal("Move to today: Call mom", row.MoveLabel);
    }

    [Fact]
    public void Year_lists_spans_then_upcoming_tasks()
    {
        var y = Projector.Year(DemoStore(), Ctx(), 2026);
        var dec = y.Months[11];
        Assert.Equal(["21–27", "1 Dec", "3 Dec", "5 Dec"], dec.Rows.Select(r => r.Meta));
        Assert.Equal(new DateOnly(2026, 12, 1), dec.ZoomTarget);
        Assert.Equal(Today, y.Months[10].ZoomTarget);
        Assert.True(y.Months[0].IsPast);
        Assert.Equal(3, y.Months[10].MoreCount);
        Assert.Equal("No tasks", Projector.Year(DemoStore(), Ctx(), 2030).Months[0].Stat);
    }
}
