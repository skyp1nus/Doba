using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Screens.Day;
using Doba.App.Screens.Today;
using Doba.App.Tests.DesignSystem;
using Doba.App.Tests.Support;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using static Doba.App.Tests.Screens.Inbox.ListScreenKit;

namespace Doba.App.Tests.Controls;

/// <summary>
/// The schedule column (shared by Today and Day): overlap columns and "+N" at the prototype's positions (the numbers are
/// the golden <c>schedData</c> output, tests/Doba.Core.Tests/Golden/fixtures/schedule.json), click = open, the checkbox
/// toggles, the "+N" list (open, close, rows), the keyboard and the Tab order.
/// </summary>
public class ScheduleTimelineTests
{
    /// <summary>16 November: Deep work 09:00–12:00 and three tasks at 10:00–10:20 → two drawn, "+2".</summary>
    private static readonly DateOnly s_crowded = new(2026, 11, 16);

    /// <summary>A day without demo tasks.</summary>
    private static readonly DateOnly s_free = new(2026, 11, 20);

    private static (TodayViewModel Vm, TodayView View, Window Window) OpenToday(ScreenHarness harness)
    {
        var vm = new TodayViewModel(harness.Services);
        vm.Show(new NavState(AppView.Today, harness.App.DayClock.Today));
        var view = new TodayView { DataContext = vm };
        return (vm, view, Host(view));
    }

    private static (DayViewModel Vm, DayView View, Window Window) OpenDay(ScreenHarness harness, DateOnly date)
    {
        var vm = new DayViewModel(harness.Services);
        vm.Show(new NavState(AppView.Day, date));
        var view = new DayView { DataContext = vm };
        return (vm, view, Host(view));
    }

    /// <summary>Bounds of <paramref name="c"/> in the 770 px grid's coordinates.</summary>
    private static Rect InGrid(Control c, ScheduleTimeline t) => new(c.TranslatePoint(default, t.GridPanel)!.Value, c.Bounds.Size);

    private static Panel Block(ScheduleTimeline t, string range) =>
        t.Blocks().Single(b => b.DataContext is ScheduleBlockViewModel vm && vm.Range == range);

    private static ScheduleBlockViewModel Vm(Panel block) => (ScheduleBlockViewModel)block.DataContext!;

    private static Button More(ScheduleTimeline t) =>
        t.OverflowList.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_More");

    private static List<Button> ListButtons(ScheduleTimeline t, string name) =>
        [.. t.ListPopover.GetVisualDescendants().OfType<Button>().Where(b => b.Name == name)];

    private static List<ToggleButton> ListChecks(ScheduleTimeline t) =>
        [.. t.ListPopover.GetVisualDescendants().OfType<ToggleButton>().Where(b => b.Name == "PART_ListCheck")];

    private static TaskItem Timed(DateOnly date, string title, int hour, int minute, int duration, bool done = false) =>
        new(0, title, string.Empty, date, null, new TimeOnly(hour, minute), duration, Priority.None, null, done);

    private static IInputElement? Focused(Window window) => window.FocusManager?.GetFocusedElement();

    private static void Key(Window window, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPress(key, mods, PhysicalKey.None, null);
        window.KeyRelease(key, mods, PhysicalKey.None, null);
        Settle(window);
    }

    // ---- Overlap layout ---------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Today_puts_overlapping_blocks_side_by_side_where_the_prototype_does()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenToday(harness);
        var t = view.Schedule;

        // sched-today: (range, left, width, top, height). Two at once from 12:00, three from 14:00.
        (string Range, double X, double W, double Top, double H)[] golden =
        [
            ("08:00 – 08:45", 54, 302, 49, 33),
            ("09:30 – 10:30", 54, 302, 121, 45),
            ("12:00 – 13:00", 54, 149, 241, 45),
            ("12:15 – 12:45", 207, 149, 253, 24),
            ("14:00 – 15:30", 54, 98, 337, 69),
            ("14:30 – 15:15", 156, 98, 361, 33),
            ("15:00 – 16:00", 258, 98, 385, 45),
            ("21:00 – 21:30", 54, 302, 673, 24),
            ("22:00 – 22:45", 54, 302, 721, 33),
        ];
        Assert.Equal(golden.Length, t.Blocks().Count());
        var model = Projector.Today(harness.App.Store, new ProjectionContext(harness.App.DayClock.Today,
            harness.App.DayClock.Now, harness.App.Settings.Current.WeekStart, null)).Schedule;
        foreach (var (range, x, w, top, h) in golden)
        {
            var block = Block(t, range);
            Assert.Equal(new Rect(x, top, w, h), InGrid(block, t));
            var record = model.Blocks.Single(b => b.Range == range);
            Assert.Equal((record.X, record.Width, record.Top, record.Height), (Vm(block).X, Vm(block).Width, Vm(block).Top, Vm(block).Height));
        }

        // No two drawn blocks cover each other.
        var rects = t.Blocks().Select(b => InGrid(b, t)).ToList();
        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                var overlap = rects[i].Intersect(rects[j]);
                Assert.True(overlap.Width <= 0 || overlap.Height <= 0, $"{rects[i]} overlaps {rects[j]}");
            }
        }

        Assert.Empty(vm.Schedule.Overflows);
        Assert.Empty(t.OverflowList.GetVisualDescendants().OfType<Button>());
        window.Close();
    }

    [AvaloniaFact]
    public void Day_16_November_draws_two_blocks_and_folds_two_into_plus_2()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenDay(harness, s_crowded);
        var t = view.Schedule;

        // sched-day-2026-11-16: 09:00 and 10:00 in columns 0 and 1 of 3; 10:15 and 10:20 under "+2" in column 2.
        Assert.Equal(2, t.Blocks().Count());
        Assert.Equal(new Rect(54, 97, 98, 141), InGrid(Block(t, "09:00 – 12:00"), t));
        Assert.Equal(new Rect(156, 145, 98, 24), InGrid(Block(t, "10:00 – 10:30"), t));
        Assert.Equal("09:00", Part<TextBlock>(Block(t, "09:00 – 12:00"), "PART_Meta").Text); // tall, < 140: the start
        Assert.False(Part<TextBlock>(Block(t, "10:00 – 10:30"), "PART_Meta").IsEffectivelyVisible); // short, < 120: none

        var overflow = Assert.Single(vm.Schedule.Overflows);
        var more = More(t);
        Assert.Equal(new Rect(258, 157, 98, 33), InGrid(more, t));
        Assert.Equal("+2", ((TextBlock)more.Content!).Text);
        Assert.Equal("2 more tasks, 10:15 to 11:00", AutomationProperties.GetName(more));
        Assert.False(overflow.IsOpen);
        Assert.False(t.ListPopover.IsVisible);
        Assert.DoesNotContain("tall", more.Classes);
        Assert.Equal(Pixels.Resource("Brush.Schedule.More"), BackgroundOf(more));
        window.Close();

        static Avalonia.Media.Color BackgroundOf(Button b) =>
            ((Avalonia.Media.ISolidColorBrush)b.Background!).Color;
    }

    [AvaloniaFact]
    public void Rendered_blocks_land_on_the_prototype_pixels()
    {
        using var harness = new ScreenHarness();
        var (_, view, window) = OpenToday(harness);
        var t = view.Schedule;
        var path = Screenshots.Capture(window, "timeline-today");
        Assert.True(new FileInfo(path).Length > 1000);
        using var frame = window.CaptureRenderedFrame()!;
        var origin = t.GridPanel.TranslatePoint(default, window)!.Value;

        // The 1 px accent-25 % border of each open block sits on its column's left edge (54 / 207 / 156 / 258) and the
        // pixel just left of it is the grid background; the done block (14:30) has the Border.Default ring.
        (double X, double W, double Top, string Ring)[] edges =
        [
            (54, 149, 241, "open"), (207, 149, 253, "open"), (54, 98, 337, "open"), (156, 98, 361, "done"), (258, 98, 385, "open"),
        ];
        var background = Pixels.At(frame, (int)(origin.X + 52), (int)(origin.Y + 300));
        foreach (var (x, w, top, ring) in edges)
        {
            var px = (int)(origin.X + x);
            var py = (int)(origin.Y + top + 12);
            var edge = Pixels.At(frame, px, py);
            var outside = Pixels.At(frame, px - 1, py);
            Assert.True(Pixels.Near(outside, background, 6), $"x {x}: {outside} left of the block is not the background {background}");
            Assert.False(Pixels.Near(edge, background, 2), $"x {x}: no border at the block's left edge");
            // … and the right border is the block's last pixel column (width w), the background right after it.
            var right = Pixels.At(frame, px + (int)w - 1, py);
            var after = Pixels.At(frame, px + (int)w, py);
            Assert.True(Pixels.Near(right, edge, 3), $"x {x} + {w}: {right} is not the border colour {edge}");
            Assert.True(Pixels.Near(after, background, 6), $"x {x} + {w}: {after} right of the block is not the background");
            if (ring == "done")
            {
                Assert.True(Pixels.Near(edge, Pixels.Resource("Color.Border.Default"), 6), $"x {x}: {edge}");
            }
        }

        window.Close();
    }

    [AvaloniaFact]
    public void Layers_are_blocks_then_now_line_copy_chip_and_list_and_the_live_region_is_polite()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenToday(harness);
        var t = view.Schedule;
        Control Named(string name) => t.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name);

        Assert.Equal(0, t.BlockList.ZIndex);
        Assert.Equal(0, t.OverflowList.ZIndex);
        Assert.Equal(2, t.NowLine.ZIndex);
        Assert.Equal(3, Named("PART_Copy").ZIndex);
        Assert.Equal(4, Named("PART_Chip").ZIndex);
        Assert.Equal(5, t.ListPopover.ZIndex);
        // At rest the copy, the chip and the list are hidden; nothing drawn above the blocks takes the pointer.
        Assert.False(Named("PART_Copy").IsVisible);
        Assert.False(Named("PART_Chip").IsVisible);
        Assert.False(t.ListPopover.IsVisible);
        Assert.False(Named("PART_Copy").IsHitTestVisible);
        Assert.False(Named("PART_Chip").IsHitTestVisible);
        Assert.False(t.NowLine.IsHitTestVisible);

        // The live region announces each message, a repeated one too (cleared first, set on a posted job).
        Assert.Equal(Avalonia.Automation.AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(t.LiveRegion));
        Assert.True(t.LiveRegion.IsVisible);
        vm.Schedule.Announce("Moved Weekly review, 21:15 to 21:45");
        Assert.Equal(string.Empty, t.LiveRegion.Text);
        Settle(window);
        Assert.Equal("Moved Weekly review, 21:15 to 21:45", t.LiveRegion.Text);
        var changes = 0;
        t.LiveRegion.PropertyChanged += (_, e) => changes += e.Property == TextBlock.TextProperty ? 1 : 0;
        vm.Schedule.Announce("Moved Weekly review, 21:15 to 21:45");
        Settle(window);
        Assert.Equal(2, changes); // "" then the message again
        Assert.Equal("Moved Weekly review, 21:15 to 21:45", t.LiveRegion.Text);

        // The copy and the chip show what the drag stage gives them.
        var block = vm.Schedule.Blocks.Single(b => b.Range == "09:30 – 10:30");
        vm.Schedule.Copy.Show(block, 11 * 60 + 15, 60);
        vm.Schedule.Chip.Show(11 * 60 + 15);
        Settle(window);
        Assert.Equal(new Rect(54, 205, 302, 45), InGrid(Named("PART_Copy"), t));
        Assert.Equal("11:15 – 12:15 · 1h", vm.Schedule.Copy.Meta);
        Assert.Equal(new Point(0, 196), InGrid(Named("PART_Chip"), t).Position); // round(204) − 8
        Assert.Equal("11:15", vm.Schedule.Chip.Label);
        vm.Schedule.Copy.Hide();
        vm.Schedule.Chip.Hide();
        Settle(window);
        Assert.False(Named("PART_Copy").IsVisible);
        window.Close();
    }

    // ---- Click, checkbox, keyboard ----------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Clicking_a_block_opens_its_task_without_toggling_it()
    {
        using var harness = new ScreenHarness();
        var (_, view, window) = OpenToday(harness);
        var block = Block(view.Schedule, "09:30 – 10:30");
        var id = Vm(block).Id;

        Click(window, Part<Button>(block, "PART_Open"));

        Assert.Equal([id], harness.Commands.Edits);
        Assert.False(harness.App.Store.Get(id)!.Done);
        Assert.Equal(new TimeOnly(9, 30), harness.App.Store.Get(id)!.Time);
        Assert.Equal("Write the weekly update · 09:30–10:30", ToolTip.GetTip(Part<Button>(block, "PART_Open")));
        Assert.StartsWith("Write the weekly update, 09:30–10:30. Enter to edit", AutomationProperties.GetName(Part<Button>(block, "PART_Open")));
        window.Close();
    }

    [AvaloniaFact]
    public void The_checkbox_toggles_the_task_without_opening_it()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenToday(harness);
        var block = Block(view.Schedule, "22:00 – 22:45");
        var blockVm = Vm(block);
        var check = Part<ToggleButton>(block, "PART_Check");
        Assert.Equal("Mark done: Plan next week and set top 3 priorities", AutomationProperties.GetName(check));
        Assert.False(check.IsChecked);
        Assert.False(check.IsTabStop); // Space on the focused block does it from the keyboard

        Click(window, check);
        Assert.True(harness.App.Store.Get(blockVm.Id)!.Done);
        Assert.True(check.IsChecked); // aria-pressed
        Assert.Equal("Mark not done: Plan next week and set top 3 priorities", AutomationProperties.GetName(check));
        Assert.Empty(harness.Commands.Edits);
        Assert.Same(blockVm, vm.Schedule.Blocks.Single(b => b.Id == blockVm.Id));
        Assert.Same(block, view.Schedule.BlockOf(blockVm));

        Click(window, check);
        Assert.False(harness.App.Store.Get(blockVm.Id)!.Done);
        Assert.False(check.IsChecked);
        Assert.Empty(harness.Commands.Edits);
        window.Close();
    }

    [AvaloniaFact]
    public void Space_on_a_focused_block_marks_it_done_and_Enter_opens_it()
    {
        using var harness = new ScreenHarness();
        var (_, view, window) = OpenToday(harness);
        var block = Block(view.Schedule, "09:30 – 10:30");
        var id = Vm(block).Id;
        var open = Part<Button>(block, "PART_Open");
        Assert.True(open.Focus(NavigationMethod.Tab));
        Settle(window);

        PressSpace(window);
        Assert.True(harness.App.Store.Get(id)!.Done);
        Assert.Empty(harness.Commands.Edits); // the open button did not click on Space
        Assert.True(open.IsFocused);

        PressSpace(window);
        Assert.False(harness.App.Store.Get(id)!.Done);

        Key(window, Avalonia.Input.Key.Enter);
        Assert.Equal([id], harness.Commands.Edits);
        Assert.False(harness.App.Store.Get(id)!.Done);
        window.Close();
    }

    [AvaloniaFact]
    public void Tab_goes_through_the_blocks_by_time_whatever_their_order_in_the_collection()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenToday(harness);
        var t = view.Schedule;

        // Move the 22:00 block to 07:00: it keeps its place (and container) in the collection, but Tab now starts there.
        var plan = vm.Schedule.Blocks.Single(b => b.Range == "22:00 – 22:45");
        var index = vm.Schedule.Blocks.IndexOf(plan);
        var container = t.BlockOf(plan);
        var task = harness.App.Store.Get(plan.Id)!;
        harness.App.Store.Update(task with { Time = new TimeOnly(7, 0) });
        Settle(window);
        Assert.Equal(index, vm.Schedule.Blocks.IndexOf(plan));
        Assert.Same(container, t.BlockOf(plan));
        Assert.Equal("07:00 – 07:45", plan.Range);

        var expected = vm.Schedule.Blocks
            .OrderBy(b => b.StartMinutes).ThenByDescending(b => Math.Max(b.DurationMinutes, 30)).ThenBy(b => b.Id)
            .Select(b => b.Id).ToList();
        Assert.Equal(plan.Id, expected[0]);
        Assert.Equal(Enumerable.Range(0, expected.Count), expected.Select(id => vm.Schedule.Blocks.Single(b => b.Id == id).TabOrder));

        Assert.True(Part<Button>(t.BlockOf(plan)!, "PART_Open").Focus(NavigationMethod.Tab));
        Settle(window);
        var walk = new List<long> { plan.Id };
        for (var i = 1; i < expected.Count; i++)
        {
            Key(window, Avalonia.Input.Key.Tab);
            var focused = Assert.IsType<Button>(Focused(window));
            Assert.Equal("PART_Open", focused.Name); // the checkbox buttons are not Tab stops
            walk.Add(((ScheduleBlockViewModel)focused.DataContext!).Id);
        }

        Assert.Equal(expected, walk);

        // Past the last block Tab leaves the schedule's blocks (Local navigation: no wrap inside).
        Key(window, Avalonia.Input.Key.Tab);
        Assert.False(Focused(window) is Button { Name: "PART_Open" or "PART_Check" });
        window.Close();
    }

    [AvaloniaFact]
    public void Tab_reaches_the_plus_N_after_the_blocks_that_start_before_it()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenDay(harness, s_crowded);
        var t = view.Schedule;

        Assert.True(Part<Button>(Block(t, "09:00 – 12:00"), "PART_Open").Focus(NavigationMethod.Tab));
        Settle(window);
        Key(window, Avalonia.Input.Key.Tab);
        Assert.Same(Part<Button>(Block(t, "10:00 – 10:30"), "PART_Open"), Focused(window));
        Key(window, Avalonia.Input.Key.Tab);
        Assert.Same(More(t), Focused(window));
        Assert.Equal(2, vm.Schedule.Overflows[0].TabOrder);
        window.Close();
    }

    // ---- "+N" list ---------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Plus_N_opens_its_list_under_it_and_a_second_click_closes_it()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenDay(harness, s_crowded);
        var t = view.Schedule;
        var more = More(t);

        Click(window, more);
        var overflow = vm.Schedule.Overflows[0];
        Assert.True(overflow.IsOpen);
        Assert.Same(overflow, vm.Schedule.OpenOverflow);
        Assert.Contains("open", more.Classes);
        Assert.True(t.ListPopover.IsVisible);

        // sched-day-2026-11-16-open: left 124, top 157 + 33 + 6 = 196, 232 wide; head and rows by time.
        var list = InGrid(t.ListPopover, t);
        Assert.Equal(new Point(124, 196), list.Position);
        Assert.Equal(232, list.Width);
        Assert.Equal(6 + 4 + 15 + 6 + (2 * 32) + 6, list.Height); // padding 6, head 4 + 14.7 (→ 15) + 6, two 32 px rows, padding 6
        Assert.Equal("2 MORE · 10:15–11:00", t.ListPopover.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_ListHead").Text);
        Assert.Equal("2 more tasks", AutomationProperties.GetName(t.ListPopover));
        var titles = t.ListPopover.GetVisualDescendants().OfType<StrikeText>().Select(s => s.Text).ToList();
        Assert.Equal(["Quick call with design", "Code review — payments"], titles);
        Assert.Equal(["10:15–10:45", "10:20–11:00"], overflow.Rows.Select(r => r.Range));
        Assert.Equal(["Mark done: Quick call with design", "Mark done: Code review — payments"],
            ListChecks(t).Select(AutomationProperties.GetName));
        foreach (var row in ListButtons(t, "PART_ListOpen"))
        {
            Assert.Equal(32, row.GetVisualParent<Control>()!.Bounds.Height);
            Assert.Equal(28, row.Bounds.Height);
        }

        Click(window, more);
        Assert.False(overflow.IsOpen);
        Assert.Null(vm.Schedule.OpenOverflow);
        Assert.False(t.ListPopover.IsVisible);
        Assert.DoesNotContain("open", more.Classes);
        Assert.Empty(harness.Commands.Edits);
        window.Close();
    }

    [AvaloniaFact]
    public void Esc_and_a_press_elsewhere_in_the_schedule_close_the_list()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenDay(harness, s_crowded);
        var t = view.Schedule;

        Click(window, More(t));
        Assert.True(t.ListPopover.IsVisible);
        Key(window, Avalonia.Input.Key.Escape);
        Assert.Null(vm.Schedule.OpenOverflow);
        Assert.False(t.ListPopover.IsVisible);

        // A press on the list itself keeps it; one on the empty grid closes it.
        Click(window, More(t));
        Click(window, t.ListPopover.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_ListHead"));
        Assert.True(t.ListPopover.IsVisible);
        var empty = t.GridPanel.TranslatePoint(new Point(200, 600), window)!.Value;
        window.MouseDown(empty, MouseButton.Left);
        window.MouseUp(empty, MouseButton.Left);
        Settle(window);
        Assert.False(t.ListPopover.IsVisible);

        // A click on a block closes the list and opens the block's task.
        Click(window, More(t));
        var block = Block(t, "09:00 – 12:00");
        Click(window, Part<Button>(block, "PART_Open"));
        Assert.False(t.ListPopover.IsVisible);
        Assert.Equal([Vm(block).Id], harness.Commands.Edits);
        window.Close();
    }

    [AvaloniaFact]
    public void List_rows_open_their_task_closing_the_list_and_toggle_it_keeping_the_list()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenDay(harness, s_crowded);
        var t = view.Schedule;
        Click(window, More(t));
        var rows = vm.Schedule.OpenOverflow!.Rows.ToList();

        // The checkbox marks the task done; the list stays open with the same rows.
        Click(window, ListChecks(t).Last());
        Assert.True(harness.App.Store.Get(rows[1].Id)!.Done);
        Assert.True(t.ListPopover.IsVisible);
        Assert.Equal(rows, vm.Schedule.OpenOverflow!.Rows);
        Assert.True(ListChecks(t).Last().IsChecked);
        Assert.True(t.ListPopover.GetVisualDescendants().OfType<StrikeText>().Last().Struck);
        Assert.Empty(harness.Commands.Edits);

        // The title opens the task and closes the list.
        Click(window, ListButtons(t, "PART_ListOpen")[0]);
        Assert.Equal([rows[0].Id], harness.Commands.Edits);
        Assert.False(t.ListPopover.IsVisible);
        Assert.Null(vm.Schedule.OpenOverflow);
        window.Close();
    }

    [AvaloniaFact]
    public void A_list_opened_from_the_keyboard_focuses_its_first_row_and_Esc_returns_to_plus_N()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenDay(harness, s_crowded);
        var t = view.Schedule;
        var more = More(t);
        Assert.True(more.Focus(NavigationMethod.Tab));
        Settle(window);

        Key(window, Avalonia.Input.Key.Enter);
        Assert.NotNull(vm.Schedule.OpenOverflow);
        Assert.True(Pump.Until(() => Focused(window) is Button { Name: "PART_ListOpen" }, TimeSpan.FromSeconds(2)));
        Assert.Same(ListButtons(t, "PART_ListOpen")[0], Focused(window));

        // Tab stays in the list's rows (checkbox, title, next row).
        Key(window, Avalonia.Input.Key.Tab);
        Assert.True(Focused(window) is ToggleButton { Name: "PART_ListCheck" } next && t.ListPopover.IsVisualAncestorOf(next));

        Key(window, Avalonia.Input.Key.Escape);
        Assert.Null(vm.Schedule.OpenOverflow);
        Assert.Same(more, Focused(window));
        Assert.Empty(harness.Commands.Edits);
        window.Close();
    }

    [AvaloniaFact]
    public void A_late_list_opens_above_its_plus_N()
    {
        using var harness = new ScreenHarness();
        var store = harness.App.Store;
        // sched-late-overflow-opens-above: four at 22:00 → "+2" at 258 / 721 (24 px); the 112 px list does not fit
        // under it (721 + 24 + 6 + 112 > 770), so it opens above: top 721 − 112 − 6 = 603.
        store.Add(Timed(s_free, "Late one", 22, 0, 60));
        store.Add(Timed(s_free, "Late two", 22, 0, 60));
        store.Add(Timed(s_free, "Late three", 22, 0, 30));
        store.Add(Timed(s_free, "Late four", 22, 0, 30, done: true));
        var (vm, view, window) = OpenDay(harness, s_free);
        var t = view.Schedule;
        Assert.Equal(new Rect(258, 721, 98, 24), InGrid(More(t), t));

        Click(window, More(t));
        Assert.False(vm.Schedule.OpenOverflow!.PopoverBelow);
        Assert.Equal(new Point(124, 603), InGrid(t.ListPopover, t).Position);
        Assert.Equal(["Late three", "Late four"], vm.Schedule.OpenOverflow.Rows.Select(r => r.Row.Title));
        Assert.True(ListChecks(t).Last().IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void The_closed_list_never_takes_the_schedule_as_its_data_context()
    {
        // Its bindings are typed for ScheduleOverflowViewModel: inheriting the ScheduleViewModel would fail each of them.
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenDay(harness, s_crowded);
        var t = view.Schedule;
        Assert.Null(t.ListPopover.DataContext);

        Click(window, More(t));
        Assert.Same(vm.Schedule.OpenOverflow, t.ListPopover.DataContext);
        Key(window, Avalonia.Input.Key.Escape);
        Assert.Null(t.ListPopover.DataContext);

        // A timeline given another schedule keeps it closed and empty too.
        var other = new DayViewModel(harness.Services);
        other.Show(new NavState(AppView.Day, s_crowded));
        t.DataContext = other.Schedule;
        Settle(window);
        Assert.Null(t.ListPopover.DataContext);
        window.Close();
    }

    [AvaloniaFact]
    public void The_open_list_follows_its_plus_N_in_the_Tab_order_when_a_new_task_renumbers_it()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenDay(harness, s_crowded);
        var t = view.Schedule;
        var more = More(t);
        more.Focus(NavigationMethod.Tab);
        Key(window, Avalonia.Input.Key.Enter);
        var overflow = vm.Schedule.OpenOverflow!;
        Assert.Equal(2, overflow.TabOrder);
        Assert.Equal(2, KeyboardNavigation.GetTabIndex(t.ListPopover));

        // A new timed task at 08:00 the same day comes first: the "+N" is now the fourth stop; its list stays open.
        harness.App.Store.Add(Timed(s_crowded, "Early call", 8, 0, 30));
        Settle(window);
        Assert.Same(overflow, vm.Schedule.OpenOverflow);
        Assert.Equal(3, overflow.TabOrder);
        Assert.Equal(3, KeyboardNavigation.GetTabIndex(t.ListPopover));

        // Tab from the "+N" goes into its list, and Tab from the block before it reaches the "+N", not the list.
        more.Focus(NavigationMethod.Tab);
        Settle(window);
        Key(window, Avalonia.Input.Key.Tab);
        Assert.True(Focused(window) is Visual inList && t.ListPopover.IsVisualAncestorOf(inList));
        Assert.True(Part<Button>(Block(t, "10:00 – 10:30"), "PART_Open").Focus(NavigationMethod.Tab));
        Settle(window);
        Key(window, Avalonia.Input.Key.Tab);
        Assert.Same(more, Focused(window));
        window.Close();
    }

    [AvaloniaFact]
    public void Another_day_leaving_the_screen_or_a_change_of_its_tasks_closes_the_list()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenDay(harness, s_crowded);
        var t = view.Schedule;

        // Ticking a listed task keeps the list (same tasks); moving one out of the cluster closes it.
        Click(window, More(t));
        var rows = vm.Schedule.OpenOverflow!.Rows.Select(r => r.Id).ToList();
        harness.App.Store.ToggleDone(rows[0]);
        Settle(window);
        Assert.True(t.ListPopover.IsVisible);
        var moved = harness.App.Store.Get(rows[1])!;
        harness.App.Store.Update(moved with { Time = new TimeOnly(18, 0) });
        Settle(window);
        Assert.Null(vm.Schedule.OpenOverflow);
        Assert.False(t.ListPopover.IsVisible);

        // Put it back; open; show another day.
        harness.App.Store.Update(moved);
        Settle(window);
        Click(window, More(t));
        Assert.True(t.ListPopover.IsVisible);
        vm.Show(new NavState(AppView.Day, s_free));
        Settle(window);
        Assert.Null(vm.Schedule.OpenOverflow);
        Assert.False(t.ListPopover.IsVisible);

        // Back on the 16th, open, then the screen host hides the view.
        vm.Show(new NavState(AppView.Day, s_crowded));
        Settle(window);
        Click(window, More(t));
        Assert.True(t.ListPopover.IsVisible);
        view.IsVisible = false;
        Settle(window);
        Assert.Null(vm.Schedule.OpenOverflow);
        window.Close();
    }

    [AvaloniaFact]
    public void Today_after_midnight_shows_the_plus_2_of_the_16th_and_its_list()
    {
        using var harness = new ScreenHarness();
        var (vm, view, window) = OpenToday(harness);
        Clock(harness).Advance(TimeSpan.FromHours(2)); // 22:26 → 00:26 on the 16th
        harness.App.DayClock.Check();
        Settle(window);

        var t = view.Schedule;
        Assert.Equal(new Rect(258, 157, 98, 33), InGrid(More(t), t));
        Click(window, More(t));
        Assert.Equal(new Point(124, 196), InGrid(t.ListPopover, t).Position);
        var path = Screenshots.Capture(window, "timeline-today-midnight-list");
        Assert.True(new FileInfo(path).Length > 1000);
        Assert.Equal(DemoData.Today.AddDays(1), harness.App.DayClock.Today);
        window.Close();
    }

    [AvaloniaFact]
    public void Day_16_November_list_screenshot()
    {
        using var harness = new ScreenHarness();
        var (_, view, window) = OpenDay(harness, s_crowded);
        Click(window, More(view.Schedule));
        var path = Screenshots.Capture(window, "timeline-day-16-list");
        Assert.True(new FileInfo(path).Length > 1000);
        window.Close();
    }
}
