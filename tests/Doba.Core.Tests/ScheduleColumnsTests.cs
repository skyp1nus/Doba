using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Projections;
using Doba.Core.Store;

namespace Doba.Core.Tests;

/// <summary>
/// The schedule's overlap layout (<see cref="ScheduleColumns"/>), its geometry and texts (<see cref="ScheduleLayout"/>)
/// and the Today / Day schedule models. The golden tests compare the same functions with the prototype's JavaScript;
/// these spell the rules out case by case.
/// </summary>
public class ScheduleColumnsTests
{
    private const int Ten = 10 * 60;

    private static ScheduleSpan S(long id, int start, int dur) => new(id, start, dur);

    private static ColumnLayout Layout(params ScheduleSpan[] items) => ScheduleColumns.Layout(items);

    private static ColumnPlacement P(int col, int span, int cols) => new(col, span, cols);

    private static ScheduleSpan[] AtOnce(int n, int start = Ten, int dur = 60) =>
        [.. Enumerable.Range(1, n).Select(i => S(i, start, dur))];

    // ---------- layout ----------

    [Fact]
    public void Empty_day_has_no_placements()
    {
        var l = Layout();
        Assert.Empty(l.Placements);
        Assert.Empty(l.Overflows);
    }

    [Fact]
    public void A_lone_block_takes_the_full_width()
    {
        var l = Layout(S(1, 570, 60));
        Assert.Equal(P(0, 1, 1), l.Placements[1]);
        Assert.Empty(l.Overflows);
    }

    [Fact]
    public void Two_at_once_split_into_two_columns()
    {
        var l = Layout(AtOnce(2));
        Assert.Equal(P(0, 1, 2), l.Placements[1]);
        Assert.Equal(P(1, 1, 2), l.Placements[2]);
    }

    [Fact]
    public void Three_at_once_split_into_three_columns()
    {
        var l = Layout(AtOnce(3));
        Assert.Equal([P(0, 1, 3), P(1, 1, 3), P(2, 1, 3)], [l.Placements[1], l.Placements[2], l.Placements[3]]);
        Assert.Empty(l.Overflows);
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(6, 4)]
    [InlineData(9, 7)]
    public void From_four_at_once_the_third_column_onwards_folds_into_one_overflow(int n, int hidden)
    {
        var l = Layout(AtOnce(n));

        Assert.Equal(P(0, 1, 3), l.Placements[1]);
        Assert.Equal(P(1, 1, 3), l.Placements[2]);
        Assert.Equal(hidden, l.Placements.Values.Count(p => p.IsHidden));
        var o = Assert.Single(l.Overflows);
        Assert.Equal((2, 3, Ten, Ten + 60), (o.Column, o.Columns, o.Start, o.End));
        Assert.Equal(Enumerable.Range(3, hidden).Select(i => (long)i), o.Ids);
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 2, 0)]
    [InlineData(3, 3, 0)]
    [InlineData(4, 2, 2)]
    [InlineData(8, 2, 6)]
    public void Visible_and_hidden_counts(int n, int visible, int hidden)
    {
        var l = Layout(AtOnce(n));
        Assert.Equal(visible, l.Placements.Values.Count(p => !p.IsHidden));
        Assert.Equal(hidden, l.Placements.Values.Count(p => p.IsHidden));
        Assert.Equal(hidden > 0 ? 1 : 0, l.Overflows.Count);
        Assert.Equal(hidden, l.Overflows.Sum(o => o.Ids.Count));
    }

    [Fact]
    public void Partial_overlap_needs_two_columns()
    {
        var l = Layout(S(1, Ten, 60), S(2, Ten + 30, 60));
        Assert.Equal(P(0, 1, 2), l.Placements[1]);
        Assert.Equal(P(1, 1, 2), l.Placements[2]);
    }

    [Fact]
    public void A_chain_is_one_cluster_and_reuses_a_freed_column()
    {
        // A ∩ B, B ∩ C, A ∦ C: C goes back to A's column, the cluster keeps two columns.
        var l = Layout(S(1, Ten, 60), S(2, Ten + 45, 75), S(3, Ten + 100, 60));
        Assert.Equal(P(0, 1, 2), l.Placements[1]);
        Assert.Equal(P(1, 1, 2), l.Placements[2]);
        Assert.Equal(P(0, 1, 2), l.Placements[3]);
    }

    [Fact]
    public void A_short_block_inside_a_long_one_goes_beside_it()
    {
        var l = Layout(S(1, 9 * 60, 180), S(2, Ten, 15));
        Assert.Equal(P(0, 1, 2), l.Placements[1]);
        Assert.Equal(P(1, 1, 2), l.Placements[2]);
    }

    [Fact]
    public void Touching_edges_do_not_overlap()
    {
        var l = Layout(S(1, Ten, 60), S(2, Ten + 60, 60));
        Assert.Equal(P(0, 1, 1), l.Placements[1]);
        Assert.Equal(P(0, 1, 1), l.Placements[2]);
    }

    [Fact]
    public void A_15_minute_block_occupies_30_minutes()
    {
        // 10:00 for 15 min is drawn 24 px tall (30 min), so a block at 10:15 overlaps it…
        var l = Layout(S(1, Ten, 15), S(2, Ten + 15, 30));
        Assert.Equal(P(0, 1, 2), l.Placements[1]);
        Assert.Equal(P(1, 1, 2), l.Placements[2]);

        // …and one at 10:30 only touches it.
        l = Layout(S(1, Ten, 15), S(2, Ten + 30, 30));
        Assert.Equal(P(0, 1, 1), l.Placements[2]);
        Assert.Equal(Ten + 30, S(1, Ten, 15).End);
        Assert.Equal(Ten + 30, S(1, Ten, 0).End); // unspecified (0) also occupies 30
        Assert.Equal(Ten + 45, S(1, Ten, 45).End);
    }

    [Fact]
    public void A_block_widens_over_free_columns_to_its_right()
    {
        // 10:00–12:00 | 10:00–10:30 | 10:00–10:30, then 10:30–11:00 lands in column 1 and column 2 is free by then.
        var l = Layout(S(1, Ten, 120), S(2, Ten, 30), S(3, Ten, 30), S(4, Ten + 30, 30));
        Assert.Equal(P(0, 1, 3), l.Placements[1]);
        Assert.Equal(P(1, 1, 3), l.Placements[2]);
        Assert.Equal(P(2, 1, 3), l.Placements[3]);
        Assert.Equal(P(1, 2, 3), l.Placements[4]);
    }

    [Fact]
    public void The_overflow_counts_as_occupied_when_widening()
    {
        // Four at 10:00 → 3 and 4 fold into "+2" over 10:00–10:30 in column 2. Block 2 (10:00–10:30, column 1) may not
        // widen over it; block 5 (10:30–11:30, column 1) starts when it ends and widens to the edge.
        var l = Layout(S(1, Ten, 120), S(2, Ten, 30), S(3, Ten, 30), S(4, Ten, 30), S(5, Ten + 30, 60));
        Assert.Equal(P(1, 1, 3), l.Placements[2]);
        Assert.True(l.Placements[3].IsHidden);
        Assert.True(l.Placements[4].IsHidden);
        Assert.Equal(P(1, 2, 3), l.Placements[5]);
        var o = Assert.Single(l.Overflows);
        Assert.Equal((Ten, Ten + 30), (o.Start, o.End));
    }

    [Fact]
    public void Equal_starts_put_the_longer_first_then_the_lower_id()
    {
        var l = Layout(S(3, Ten, 60), S(1, Ten, 60), S(2, Ten, 30));
        Assert.Equal(0, l.Placements[1].Column);
        Assert.Equal(1, l.Placements[3].Column);
        Assert.Equal(2, l.Placements[2].Column);
    }

    [Fact]
    public void The_input_order_does_not_matter()
    {
        ScheduleSpan[] items = [S(1, Ten, 120), S(2, Ten, 30), S(3, Ten + 15, 30), S(4, Ten + 20, 40), S(5, 13 * 60, 30), S(6, 13 * 60, 60)];
        var a = Layout(items);
        var b = Layout([.. items.Reverse()]);
        Assert.Equal(a.Placements.OrderBy(p => p.Key), b.Placements.OrderBy(p => p.Key));
        Assert.Equal(a.Overflows.Single().Ids, b.Overflows.Single().Ids);
    }

    [Fact]
    public void Each_crowded_cluster_gets_its_own_overflow_in_time_order()
    {
        var l = Layout([.. AtOnce(4), S(5, 13 * 60, 30), S(6, 13 * 60, 30), S(7, 13 * 60 + 10, 30), S(8, 13 * 60 + 15, 45), S(9, 13 * 60 + 20, 20)]);
        Assert.Equal(2, l.Overflows.Count);
        Assert.Equal(Ten, l.Overflows[0].Start);
        Assert.Equal([3L, 4], l.Overflows[0].Ids);
        Assert.Equal((13 * 60 + 10, 14 * 60), (l.Overflows[1].Start, l.Overflows[1].End));
        Assert.Equal([7L, 8, 9], l.Overflows[1].Ids);
    }

    [Fact]
    public void A_sixteen_hour_task_is_one_column_of_the_whole_day()
    {
        var l = Layout(S(1, 7 * 60, 960), S(2, 22 * 60, 60));
        Assert.Equal(P(0, 1, 2), l.Placements[1]);
        Assert.Equal(P(1, 1, 2), l.Placements[2]);
    }

    [Fact]
    public void Other_maximums()
    {
        var one = ScheduleColumns.Layout(AtOnce(3), maxColumns: 1);
        Assert.All(one.Placements.Values, p => Assert.True(p.IsHidden)); // one column: everything is the overflow
        Assert.Equal([1L, 2, 3], one.Overflows.Single().Ids);

        var four = ScheduleColumns.Layout(AtOnce(4), maxColumns: 4);
        Assert.Equal(P(3, 1, 4), four.Placements[4]);
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleColumns.Layout(AtOnce(2), maxColumns: 0));
        Assert.Throws<ArgumentNullException>(() => ScheduleColumns.Layout(null!));
    }

    // ---------- geometry ----------

    [Fact]
    public void Column_geometry_matches_the_design()
    {
        Assert.Equal([302.0, 149, 98], [ScheduleLayout.ColumnWidth(1), ScheduleLayout.ColumnWidth(2), ScheduleLayout.ColumnWidth(3)]);
        Assert.Equal((54, 302), (ScheduleLayout.BlockX(P(0, 1, 1)), ScheduleLayout.BlockWidth(P(0, 1, 1))));
        Assert.Equal((207, 149), (ScheduleLayout.BlockX(P(1, 1, 2)), ScheduleLayout.BlockWidth(P(1, 1, 2))));
        Assert.Equal((156, 98), (ScheduleLayout.BlockX(P(1, 1, 3)), ScheduleLayout.BlockWidth(P(1, 1, 3))));
        Assert.Equal((258, 98), (ScheduleLayout.BlockX(P(2, 1, 3)), ScheduleLayout.BlockWidth(P(2, 1, 3))));
        Assert.Equal((156, 200), (ScheduleLayout.BlockX(P(1, 2, 3)), ScheduleLayout.BlockWidth(P(1, 2, 3))));
        Assert.Equal((54, 302), (ScheduleLayout.BlockX(P(0, 3, 3)), ScheduleLayout.BlockWidth(P(0, 3, 3))));
        Assert.Equal((0, 0), (ScheduleLayout.BlockX(ColumnPlacement.Hidden), ScheduleLayout.BlockWidth(ColumnPlacement.Hidden)));

        // Four columns (not the design's maximum) land on half pixels: (302 − 12) / 4 = 72.5.
        Assert.Equal((130.5, 72.5), (ScheduleLayout.BlockX(P(1, 1, 4)), ScheduleLayout.BlockWidth(P(1, 1, 4))));
        Assert.Equal(124, ScheduleLayout.PopoverLeft);
    }

    [Fact]
    public void Half_rounds_to_the_nearest_half_pixel_halves_up()
    {
        Assert.Equal(207.5, ScheduleLayout.Half(207.25));
        Assert.Equal(207, ScheduleLayout.Half(207.24));
        Assert.Equal(98.5, ScheduleLayout.Half(98.6));
        Assert.Equal(99, ScheduleLayout.Half(98.75));
    }

    [Theory]
    [InlineData(302, 45, false, "09:30 – 10:30")] // tall: range from 140 px
    [InlineData(140, 45, false, "09:30 – 10:30")]
    [InlineData(139, 45, false, "09:30")]       // tall and narrower: the start
    [InlineData(98, 45, false, "09:30")]
    [InlineData(302, 33, false, "09:30 – 10:30")] // short: range from 220 px
    [InlineData(220, 24, false, "09:30 – 10:30")]
    [InlineData(219, 24, false, "09:30")]       // start from 120 px
    [InlineData(120, 33, false, "09:30")]
    [InlineData(119.5, 33, false, "")]          // under 120: title only
    [InlineData(98, 24, false, "")]
    [InlineData(302, 24, true, "09:30 – 10:30 · 1h")] // active (resizing / the dragged copy) adds the length
    [InlineData(149, 45, true, "09:30 – 10:30 · 1h")]
    [InlineData(139, 45, true, "09:30")]
    public void Content_tier_by_width_and_height(double w, double h, bool active, string meta)
    {
        Assert.Equal(meta, ScheduleLayout.Meta(w, h, 570, 60, active));
    }

    [Theory]
    [InlineData(24, -1, 8)]
    [InlineData(25, 0, 8)]  // round(−0.5) = 0, as Math.round
    [InlineData(31, 3, 8)]  // round(2.5) = 3
    [InlineData(32, 3, 11)]
    [InlineData(33, 4, 11)]
    [InlineData(43, 9, 11)]
    [InlineData(44, 4, 11)] // tall: the checkbox sits at the top
    [InlineData(141, 4, 11)]
    public void Checkbox_top_and_grip_height_by_block_height(double h, double checkTop, double grip)
    {
        Assert.Equal(checkTop, ScheduleLayout.CheckTop(h));
        Assert.Equal(grip, ScheduleLayout.GripHeight(h));
    }

    [Fact]
    public void Block_labels()
    {
        Assert.Equal("Write the weekly update, 09:30–10:30. Enter to edit, arrows to move, Shift and arrows to change length, Space to mark done",
            ScheduleLayout.BlockLabel("Write the weekly update", 570, 60, done: false));
        Assert.Equal("Call with the client, 14:30–15:15, done. Enter to edit, arrows to move, Shift and arrows to change length, Space to mark done",
            ScheduleLayout.BlockLabel("Call with the client", 870, 45, done: true));
        Assert.Equal("Write the weekly update · 09:30–10:30", ScheduleLayout.BlockTip("Write the weekly update", 570, 60));
        Assert.Equal("22:30–00:15", ScheduleLayout.TightRange(22 * 60 + 30, 105)); // past midnight wraps like hhmm
    }

    [Fact]
    public void A_block_carries_its_geometry_and_texts()
    {
        var row = new TaskRow(7, "Pair session — auth refactor", false, Priority.None, "#work", "15:00", "", false, false);
        var b = ScheduleLayout.Block(row, 15 * 60, 60, P(2, 1, 3));
        Assert.Equal((258, 98, 385, 45), (b.X, b.Width, b.Top, b.Height));
        Assert.Equal(("15:00", true, 4, 11), (b.Meta, b.IsTall, b.CheckTop, b.GripHeight));
        Assert.Equal("15:00 – 16:00", b.Range);
        Assert.Equal("Pair session — auth refactor · 15:00–16:00", b.ToolTip);
        Assert.Equal("Mark done: Pair session — auth refactor", b.CheckLabel);
        Assert.False(b.IsHidden);
        Assert.True(ScheduleLayout.Block(row, 15 * 60, 60, ColumnPlacement.Hidden).IsHidden);
    }

    // ---------- the Today / Day models ----------

    private static TaskStore DemoStore() => new(DemoData.Tasks(), DemoData.Tags, NullTaskPersistence.Instance);

    private static ProjectionContext Ctx(DateOnly? today = null, string? filter = null) =>
        new(today ?? DemoData.Today, DemoData.Now, DayOfWeek.Monday, filter);

    private static TaskItem Timed(long id, string title, DateOnly date, int start, int dur, bool done = false, Priority prio = Priority.None) =>
        new(id, title, string.Empty, date, null, new TimeOnly(start / 60, start % 60), dur, prio, null, done);

    [Fact]
    public void Today_15_November_has_overlaps_of_two_and_three()
    {
        var s = Projector.Today(DemoStore(), Ctx()).Schedule;
        ScheduleBlock B(string title) => s.Blocks.Single(b => b.Row.Title.StartsWith(title, StringComparison.Ordinal));

        Assert.Empty(s.Overflows);
        Assert.DoesNotContain(s.Blocks, b => b.IsHidden);
        Assert.Equal((54, 302), (B("Write the weekly update").X, B("Write the weekly update").Width));
        Assert.Equal((P(0, 1, 2), P(1, 1, 2)), (B("Meal prep").Placement, B("Grocery delivery").Placement));
        Assert.Equal((207, 149, "12:15"), (B("Grocery delivery").X, B("Grocery delivery").Width, B("Grocery delivery").Meta));
        Assert.Equal([P(0, 1, 3), P(1, 1, 3), P(2, 1, 3)],
            [B("Coffee and reading").Placement, B("Call with the client").Placement, B("Pair session").Placement]);
        Assert.Equal("14:00 – 15:30", B("Coffee and reading").Range);
        Assert.Equal("14:00", B("Coffee and reading").Meta); // 98 px wide, tall: the start only
    }

    [Fact]
    public void Day_16_November_folds_two_blocks_into_plus_two()
    {
        var s = Projector.Day(DemoStore(), Ctx(), new DateOnly(2026, 11, 16)).Schedule;

        Assert.Equal(["Quick call with design", "Code review — payments"], s.Blocks.Where(b => b.IsHidden).Select(b => b.Row.Title));
        var o = Assert.Single(s.Overflows);
        Assert.Equal((258, 98, 157, 33, false), (o.X, o.Width, o.Top, o.Height, o.IsTall));
        Assert.Equal("+2", o.Text);
        Assert.Equal("2 more tasks, 10:15 to 11:00", o.AccessibleLabel);
        Assert.Equal("2 more · 10:15–11:00", o.PopoverHead);
        Assert.Equal("2 more tasks", o.PopoverLabel);
        Assert.True(o.PopoverBelow);
        Assert.Equal(157 + 33 + 6, o.PopoverTop);
        Assert.Equal(["10:15–10:45", "10:20–11:00"], o.Rows.Select(r => r.Range));
        Assert.Equal("Mark done: Quick call with design", o.Rows[0].CheckLabel);
    }

    [Fact]
    public void Marking_a_task_done_moves_no_block()
    {
        var store = DemoStore();
        var before = Projector.Today(store, Ctx()).Schedule;
        var grocery = store.All.Single(t => t.Title == "Grocery delivery window");
        store.Update(grocery with { Done = true });
        var after = Projector.Today(store, Ctx()).Schedule;

        Assert.Equal(before.Blocks.Select(b => (b.Row.Id, b.Placement, b.X, b.Width)), after.Blocks.Select(b => (b.Row.Id, b.Placement, b.X, b.Width)));
        Assert.EndsWith(", done. Enter to edit, arrows to move, Shift and arrows to change length, Space to mark done",
            after.Blocks.Single(b => b.Row.Id == grocery.Id).AccessibleLabel);
    }

    [Fact]
    public void A_late_plus_N_opens_its_list_above()
    {
        var d = new DateOnly(2026, 6, 10);
        var store = new TaskStore([.. Enumerable.Range(1, 4).Select(i => Timed(i, "Late " + i, d, 22 * 60, i <= 2 ? 60 : 30))], [], NullTaskPersistence.Instance);
        var o = Projector.Day(store, Ctx(), d).Schedule.Overflows.Single();
        Assert.Equal((721, 24), (o.Top, o.Height));
        Assert.False(o.PopoverBelow); // 721 + 24 + 6 + (36 + 2·32 + 12) > 770
        Assert.Equal(721 - 112 - 6, o.PopoverTop);
    }

    [Fact]
    public void The_plus_N_list_is_by_time_then_priority_then_layout_order()
    {
        var d = new DateOnly(2026, 6, 10);
        var store = new TaskStore(
        [
            Timed(1, "Alpha", d, Ten, 60), Timed(2, "Bravo", d, Ten, 60), Timed(3, "Charlie", d, Ten, 30, prio: Priority.High),
            Timed(4, "Delta", d, Ten, 45, done: true), Timed(5, "Echo", d, Ten, 30, prio: Priority.Asap), Timed(6, "Foxtrot", d, Ten, 15),
            Timed(7, "Golf", d, Ten + 20, 60),
        ], [], NullTaskPersistence.Instance);
        var o = Projector.Day(store, Ctx(), d).Schedule.Overflows.Single();

        // Hidden in layout order: Delta (45), then the 30-minute ones by id (Charlie, Echo, Foxtrot drawn as 30), then Golf.
        Assert.Equal(["Echo", "Charlie", "Delta", "Foxtrot", "Golf"], o.Rows.Select(r => r.Row.Title));
        Assert.Equal("5 more · 10:00–11:20", o.PopoverHead); // Golf ends 11:20 by its real length
        Assert.Equal("5 more tasks, 10:00 to 11:20", o.AccessibleLabel);
        Assert.Equal("Mark not done: Delta", o.Rows[2].CheckLabel);
        Assert.Equal("10:00–10:15", o.Rows[3].Range); // Foxtrot's real 15 minutes
    }

    [Fact]
    public void Blocks_before_seven_are_left_out_and_untimed_tasks_are_not_blocks()
    {
        var d = new DateOnly(2026, 6, 10);
        var store = new TaskStore(
        [
            Timed(1, "Early", d, 6 * 60 + 30, 60),
            Timed(2, "Seven", d, 7 * 60, 0),
            new TaskItem(3, "Untimed", string.Empty, d, null, null, 0, Priority.None, null, false),
        ], [], NullTaskPersistence.Instance);
        var s = Projector.Day(store, Ctx(), d).Schedule;
        var b = Assert.Single(s.Blocks);
        Assert.Equal(("Seven", 30, "07:00 – 07:30"), (b.Row.Title, b.DurationMinutes, b.Range));
        Assert.Equal((P(0, 1, 1), 1.0, 24.0, "07:00 – 07:30", false), (b.Placement, b.Top, b.Height, b.Meta, b.IsTall));
    }
}
