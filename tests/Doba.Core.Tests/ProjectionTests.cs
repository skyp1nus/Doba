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
        // 5 weeks in June 2026 (Monday start): H = floor(806/5) = 161, cap = floor((161 − 34 − 48 − 8)/25) = 2 → 1 item + "+8 more".
        var cell = week.Days.Single(x => x.Date == d);
        Assert.Single(cell.Items);
        Assert.Equal(8, cell.MoreCount);

        var tall = Projector.Month(store, Ctx(), d, new MonthMetrics(AvailableHeight: 1600));
        var tallCell = tall.Weeks.Single(w => w.WeekStart <= d && d <= w.WeekStart.AddDays(6)).Days.Single(x => x.Date == d);
        Assert.True(tallCell.Items.Count > cell.Items.Count);
        Assert.Equal(9, tallCell.Items.Count + tallCell.MoreCount);
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
