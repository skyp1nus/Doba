using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Screens.Day;
using Doba.App.Screens.Today;
using Doba.App.Tests.DesignSystem;
using Doba.App.Tests.Support;
using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using Doba.Core.Store;
using static Doba.App.Tests.Screens.Inbox.ListScreenKit;
using AppMotion = Doba.App.Motion.Motion;
using MotionTimes = Doba.App.Motion.MotionTimes;

namespace Doba.App.Tests.Controls;

/// <summary>
/// The schedule's keyboard on a focused block (motion spec «Клавіатура», the prototype's <c>sbKey</c>), with real headless
/// key events on the window: ↑ / ↓ (Alt, Shift, Alt+Shift) from aligned and unaligned times, the 07:00 / 23:00 edges, one
/// store write per step (auto-repeat too), the block's view-model, container and focus kept (or the focus on the "+N"
/// that folds it), the time chip and its 900 ms hold, the live region, Enter, Space, Tab, and ← / → in Day.
/// </summary>
public class ScheduleKeyboardTests
{
    /// <summary>A day without demo tasks.</summary>
    private static readonly DateOnly s_free = new(2026, 11, 20);

    private static readonly TimeSpan s_hold = TimeSpan.FromMilliseconds(AppMotion.ChipHold);
    private static readonly TimeSpan s_tick = TimeSpan.FromMilliseconds(1);

    /// <summary>A Day screen on <see cref="s_free"/> holding <paramref name="tasks"/>, with a manual chip clock and a
    /// recorder of the store's changes.</summary>
    private sealed class Scene : IDisposable
    {
        public Scene(params TaskItem[] tasks)
            : this(s_free, ScreenHarness.ContentHeight, tasks)
        {
        }

        public Scene(DateOnly date, double height, params TaskItem[] tasks)
        {
            Harness = new ScreenHarness();
            Ids = [.. tasks.Select(t => Harness.App.Store.Add(t).Id)];
            Vm = new DayViewModel(Harness.Services);
            Vm.Show(new NavState(AppView.Day, date));
            Vm.Schedule.HoldClock = Clock;
            View = new DayView { DataContext = Vm };
            Window = ScreenHarness.Host(View, ScreenHarness.ContentWidth, height);
            Settle(Window);
            Harness.App.Store.Changed += (_, e) => Changes.Add(e);
            Window.AddHandler(InputElement.KeyDownEvent, (_, e) => Handled.Add((e.Key, e.Handled)), RoutingStrategies.Bubble, handledEventsToo: true);
        }

        public ScreenHarness Harness { get; }

        public ManualClock Clock { get; } = new();

        public long[] Ids { get; }

        public DayViewModel Vm { get; }

        public DayView View { get; }

        public Window Window { get; }

        public ScheduleTimeline Timeline => View.Schedule;

        public List<TaskStoreChangedEventArgs> Changes { get; } = [];

        /// <summary>Every key-down that reached the window, and whether something handled it on the way.</summary>
        public List<(Key Key, bool Handled)> Handled { get; } = [];

        public TaskItem Task(long id) => Harness.App.Store.Get(id)!;

        public ScheduleBlockViewModel BlockVm(long id) => Vm.Schedule.Blocks.Single(b => b.Id == id);

        public Button Open(long id) => Part<Button>(Timeline.BlockOf(BlockVm(id))!, "PART_Open");

        public IInputElement? Focused => Window.FocusManager?.GetFocusedElement();

        /// <summary>Focuses the block of <paramref name="id"/> from the keyboard.</summary>
        public Button Focus(long id)
        {
            var open = Open(id);
            Assert.True(open.Focus(NavigationMethod.Tab));
            Settle(Window);
            Assert.Same(open, Focused);
            return open;
        }

        public void Key(Key key, RawInputModifiers mods = RawInputModifiers.None)
        {
            Window.KeyPress(key, mods, PhysicalKey.None, null);
            Window.KeyRelease(key, mods, PhysicalKey.None, null);
            Settle(Window);
        }

        public void Dispose()
        {
            Window.Close();
            Harness.Dispose();
        }
    }

    private static TaskItem Timed(string title, int hour, int minute, int duration, DateOnly? date = null) =>
        new(0, title, string.Empty, date ?? s_free, null, new TimeOnly(hour, minute), duration, Priority.None, null, false);

    private static int Minutes(TaskItem t) => (t.Time!.Value.Hour * 60) + t.Time.Value.Minute;

    /// <summary>The one change since <paramref name="before"/> is an in-place update of <paramref name="id"/>.</summary>
    private static void AssertOneUpdate(Scene s, int before, long id)
    {
        Assert.Equal(before + 1, s.Changes.Count);
        var change = s.Changes[^1];
        Assert.Equal(TaskChangeKind.Updated, change.Kind);
        Assert.Equal(id, change.New!.Id);
    }

    // ---- ↑ / ↓ -------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Up_and_Down_move_by_15_minutes_one_write_each_keeping_view_model_container_and_focus()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var vm = s.BlockVm(id);
        var container = s.Timeline.BlockOf(vm);
        var open = s.Focus(id);

        s.Key(Key.Down);
        AssertOneUpdate(s, 0, id);
        Assert.Equal(new TimeOnly(9, 45), s.Task(id).Time);
        Assert.Equal(60, s.Task(id).DurationMinutes);
        Assert.Same(vm, s.BlockVm(id));
        Assert.Same(container, s.Timeline.BlockOf(vm));
        Assert.Same(open, s.Focused);
        Assert.Equal("09:45 – 10:45", vm.Range);
        Assert.Equal(ScheduleLayout.BlockTop(9 * 60 + 45, ScheduleLayout.DesignHourHeight), vm.Top);
        Assert.Equal(vm.Top, container!.Margin.Top);

        s.Key(Key.Up);
        s.Key(Key.Up);
        Assert.Equal(3, s.Changes.Count);
        Assert.Equal(new TimeOnly(9, 15), s.Task(id).Time);
        Assert.Equal(60, s.Task(id).DurationMinutes);
        Assert.Same(vm, s.BlockVm(id));
        Assert.Same(container, s.Timeline.BlockOf(vm));
        Assert.Same(open, s.Focused);
        Assert.True(open.IsKeyboardFocusWithin);
        Assert.Empty(s.Harness.Commands.Edits);
        Assert.All(s.Handled.Where(h => h.Key is Key.Up or Key.Down), h => Assert.True(h.Handled));
    }

    [AvaloniaFact]
    public void From_an_unaligned_time_the_arrows_go_to_the_next_grid_line_and_Alt_to_the_next_5_minutes()
    {
        using var s = new Scene(Timed("Unaligned", 9, 37, 60));
        var id = s.Ids[0];
        var original = s.Task(id);
        s.Focus(id);

        s.Key(Key.Down);
        Assert.Equal(new TimeOnly(9, 45), s.Task(id).Time);
        s.Harness.App.Store.Update(original);
        s.Key(Key.Up);
        Assert.Equal(new TimeOnly(9, 30), s.Task(id).Time);
        s.Harness.App.Store.Update(original);
        s.Key(Key.Down, RawInputModifiers.Alt);
        Assert.Equal(new TimeOnly(9, 40), s.Task(id).Time);
        s.Harness.App.Store.Update(original);
        s.Key(Key.Up, RawInputModifiers.Alt);
        Assert.Equal(new TimeOnly(9, 35), s.Task(id).Time);
        s.Key(Key.Up, RawInputModifiers.Alt);
        Assert.Equal(new TimeOnly(9, 30), s.Task(id).Time);
        Assert.All(s.Changes.Where(c => c.Kind == TaskChangeKind.Updated), c => Assert.Equal(60, c.New!.DurationMinutes));
    }

    [AvaloniaFact]
    public void Shift_moves_the_end_Alt_Shift_by_5_minutes_and_the_length_stays_at_least_15()
    {
        using var s = new Scene(Timed("Unaligned", 9, 37, 60));
        var id = s.Ids[0];
        var original = s.Task(id);
        var open = s.Focus(id);

        s.Key(Key.Down, RawInputModifiers.Shift);
        AssertOneUpdate(s, 0, id);
        Assert.Equal(new TimeOnly(9, 37), s.Task(id).Time);
        Assert.Equal(68, s.Task(id).DurationMinutes); // end 10:37 → 10:45
        s.Harness.App.Store.Update(original);
        s.Key(Key.Up, RawInputModifiers.Shift);
        Assert.Equal(53, s.Task(id).DurationMinutes); // end 10:37 → 10:30
        s.Harness.App.Store.Update(original);
        s.Key(Key.Down, RawInputModifiers.Shift | RawInputModifiers.Alt);
        Assert.Equal(63, s.Task(id).DurationMinutes); // end 10:37 → 10:40
        s.Harness.App.Store.Update(original);
        s.Key(Key.Up, RawInputModifiers.Shift | RawInputModifiers.Alt);
        Assert.Equal(58, s.Task(id).DurationMinutes); // end 10:37 → 10:35
        Assert.Equal(new TimeOnly(9, 37), s.Task(id).Time);
        Assert.Same(open, s.Focused);

        // Down to the 15-minute minimum, then Shift+↑ does nothing (handled, no write).
        s.Harness.App.Store.Update(original with { DurationMinutes = 20 });
        s.Key(Key.Up, RawInputModifiers.Shift);
        Assert.Equal(15, s.Task(id).DurationMinutes); // end 09:57 → 09:45 (8 min), at least 15
        var writes = s.Changes.Count;
        s.Key(Key.Up, RawInputModifiers.Shift);
        Assert.Equal(writes, s.Changes.Count);
        Assert.Equal(15, s.Task(id).DurationMinutes);
        Assert.True(s.Handled[^1].Handled);
        Assert.Equal("09:37 – 09:52", s.BlockVm(id).Range);
    }

    [AvaloniaFact]
    public void An_unspecified_duration_stays_unspecified_on_a_move_and_Shift_writes_the_real_length()
    {
        using var s = new Scene(Timed("No length", 11, 0, 0));
        var id = s.Ids[0];
        s.Focus(id);

        s.Key(Key.Down);
        Assert.Equal(new TimeOnly(11, 15), s.Task(id).Time);
        Assert.Equal(0, s.Task(id).DurationMinutes);
        Assert.Equal("11:15 – 11:45", s.BlockVm(id).Range);

        s.Key(Key.Down, RawInputModifiers.Shift);
        Assert.Equal(new TimeOnly(11, 15), s.Task(id).Time);
        Assert.Equal(45, s.Task(id).DurationMinutes); // the drawn 30 min end 11:45 → 12:00
        Assert.Equal(2, s.Changes.Count);
    }

    [AvaloniaFact]
    public void Held_arrows_write_once_per_auto_repeat()
    {
        using var s = new Scene(Timed("Held", 10, 0, 30));
        var id = s.Ids[0];
        var open = s.Focus(id);

        for (var i = 0; i < 3; i++)
        {
            s.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); // auto-repeat: no release
            Settle(s.Window);
            AssertOneUpdate(s, i, id);
        }

        s.Window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Settle(s.Window);
        Assert.Equal(3, s.Changes.Count);
        Assert.Equal(new TimeOnly(10, 45), s.Task(id).Time);
        Assert.Same(open, s.Focused);
    }

    [AvaloniaFact]
    public void Reschedule_is_one_in_place_update_and_nothing_for_the_same_slot_an_untimed_or_a_missing_task()
    {
        using var s = new Scene(Timed("No length", 11, 0, 0));
        var id = s.Ids[0];
        var actions = s.Harness.Actions;
        var untimed = s.Harness.App.Store.Add(Timed("Untimed", 9, 0, 30) with { Time = null }).Id;
        s.Changes.Clear();

        Assert.False(actions.Reschedule(id, ScheduleDragMode.Move, new(11 * 60, 30))); // where it is (0 draws as 30)
        Assert.False(actions.Reschedule(untimed, ScheduleDragMode.Move, new(10 * 60, 30)));
        Assert.False(actions.Reschedule(-1, ScheduleDragMode.Move, new(10 * 60, 30)));
        Assert.Empty(s.Changes);

        Assert.True(actions.Reschedule(id, ScheduleDragMode.Move, new(10 * 60, 30)));
        AssertOneUpdate(s, 0, id);
        Assert.Equal(s.Task(id), s.Changes[^1].New);
        Assert.Equal(new TimeOnly(10, 0), s.Task(id).Time);
        Assert.Equal(0, s.Task(id).DurationMinutes);

        Assert.True(actions.Reschedule(id, ScheduleDragMode.Resize, new(10 * 60, 45)));
        AssertOneUpdate(s, 1, id);
        Assert.Equal(45, s.Task(id).DurationMinutes);
        Assert.Equal("10:00 – 10:45", s.BlockVm(id).Range);
    }

    // ---- Edges -------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void At_07_00_and_23_00_the_arrows_are_handled_but_write_nothing_and_do_not_scroll()
    {
        // A low window: the grid scrolls.
        using var s = new Scene(s_free, 400, Timed("First", 7, 0, 60), Timed("Last", 22, 30, 30), Timed("Middle", 15, 0, 60));
        var (first, last) = (s.Ids[0], s.Ids[1]);
        var scroller = s.Timeline.Scroller;
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height);

        s.Focus(first);
        var offset = scroller.Offset;
        s.Key(Key.Up);
        s.Key(Key.Up, RawInputModifiers.Alt);
        Assert.Empty(s.Changes);
        Assert.Equal(offset, scroller.Offset);
        Assert.Equal(new TimeOnly(7, 0), s.Task(first).Time);
        Assert.Same(s.Open(first), s.Focused);
        Assert.False(s.Vm.Schedule.Chip.IsVisible); // nothing changed: no chip, no message
        Assert.Equal(string.Empty, s.Timeline.LiveRegion.Text ?? string.Empty);

        scroller.Offset = new Vector(0, scroller.Extent.Height - scroller.Viewport.Height);
        s.Focus(last);
        offset = scroller.Offset;
        s.Key(Key.Down);
        s.Key(Key.Down, RawInputModifiers.Shift); // the end is 23:00 already
        s.Key(Key.Down, RawInputModifiers.Shift | RawInputModifiers.Alt);
        Assert.Empty(s.Changes);
        Assert.Equal(offset, scroller.Offset);
        Assert.Equal(new TimeOnly(22, 30), s.Task(last).Time);
        Assert.Equal(30, s.Task(last).DurationMinutes);
        Assert.Same(s.Open(last), s.Focused);

        var arrows = s.Handled.Where(h => h.Key is Key.Up or Key.Down).ToList();
        Assert.Equal(5, arrows.Count);
        Assert.All(arrows, h => Assert.True(h.Handled));
    }

    [AvaloniaFact]
    public void A_move_stops_at_the_edges_without_overshooting()
    {
        using var s = new Scene(Timed("Early", 7, 10, 30), Timed("Late", 22, 20, 30));
        var (early, late) = (s.Ids[0], s.Ids[1]);

        s.Focus(early);
        s.Key(Key.Up);
        Assert.Equal(new TimeOnly(7, 0), s.Task(early).Time);
        s.Key(Key.Up);
        Assert.Equal(new TimeOnly(7, 0), s.Task(early).Time);

        s.Focus(late);
        s.Key(Key.Down);
        Assert.Equal(new TimeOnly(22, 30), s.Task(late).Time); // 22:20 → 22:30: the end is 23:00
        s.Key(Key.Down);
        Assert.Equal(new TimeOnly(22, 30), s.Task(late).Time);
        Assert.Equal(2, s.Changes.Count);
    }

    // ---- Focus -------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_block_stepped_under_a_plus_N_hands_the_focus_to_that_plus_N()
    {
        // Three at 10:00 fill the three columns; 11:00 touches them. ↑ puts it into the cluster: 4 at once → "+2".
        using var s = new Scene(Timed("A", 10, 0, 60), Timed("B", 10, 0, 60), Timed("C", 10, 0, 60), Timed("D", 11, 0, 30));
        var d = s.Ids[3];
        Assert.Empty(s.Vm.Schedule.Overflows);
        s.Focus(d);

        s.Key(Key.Up);
        AssertOneUpdate(s, 0, d);
        Assert.Equal(new TimeOnly(10, 45), s.Task(d).Time);
        Assert.DoesNotContain(s.Vm.Schedule.Blocks, b => b.Id == d);
        var overflow = Assert.Single(s.Vm.Schedule.Overflows);
        Assert.Contains(overflow.Rows, r => r.Id == d);
        Assert.Equal("+2", overflow.Text);
        var more = s.Timeline.MoreOf(overflow);
        Assert.NotNull(more);
        Assert.Same(more, s.Focused);
        Assert.True(more.IsFocused);
        Assert.Contains(":focus-visible", more.Classes); // still keyboard focus
        Assert.Equal("10:45", s.Vm.Schedule.Chip.Label);
        Assert.Equal("Moved D, 10:45 to 11:15", s.Timeline.LiveRegion.Text);

        // ↓ on the "+N" is not a block step.
        s.Key(Key.Down);
        Assert.Single(s.Changes);
    }

    // ---- Chip and live region ------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_chip_shows_the_new_start_for_900_ms_and_a_new_step_restarts_the_hold()
    {
        using var s = new Scene(Timed("Chip", 9, 30, 60));
        var id = s.Ids[0];
        var chip = s.Vm.Schedule.Chip;
        var part = s.Timeline.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_Chip");
        Assert.False(part.IsVisible);
        s.Focus(id);

        s.Key(Key.Down);
        Assert.True(chip.IsVisible);
        Assert.True(part.IsVisible);
        Assert.Equal("09:45", chip.Label);
        Assert.Equal(ScheduleLayout.NowTop((9 * 60) + 45, ScheduleLayout.DesignHourHeight) - 8, chip.Top);
        Assert.Equal(chip.Top, part.Margin.Top);

        s.Clock.Advance(s_hold - s_tick);
        Assert.True(chip.IsVisible);
        s.Clock.Advance(s_tick);
        Assert.False(chip.IsVisible);
        Settle(s.Window);
        Assert.False(part.IsVisible);

        s.Key(Key.Down);
        s.Clock.Advance(TimeSpan.FromMilliseconds(600));
        s.Key(Key.Down);
        Assert.Equal("10:15", chip.Label);
        s.Clock.Advance(TimeSpan.FromMilliseconds(600));
        Assert.True(chip.IsVisible); // 1200 ms after the first step, 600 after the second
        s.Clock.Advance(s_hold - TimeSpan.FromMilliseconds(600));
        Assert.False(chip.IsVisible);
        Assert.Equal(0, s.Clock.ActiveTimers);
    }

    [AvaloniaFact]
    public void Shift_shows_the_new_end_on_the_chip_and_the_live_region_says_Resized()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        s.Focus(id);

        s.Key(Key.Down, RawInputModifiers.Shift);
        Assert.Equal("10:45", s.Vm.Schedule.Chip.Label);
        Assert.Equal("Resized Write the weekly update, 09:30 to 10:45", s.Timeline.LiveRegion.Text);

        s.Key(Key.Down);
        Assert.Equal("09:45", s.Vm.Schedule.Chip.Label);
        Assert.Equal("Moved Write the weekly update, 09:45 to 11:00", s.Timeline.LiveRegion.Text);

        // The same message again is announced again: the text is cleared first, then set on a posted job.
        var texts = new List<string?>();
        s.Timeline.LiveRegion.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty)
            {
                texts.Add((string?)e.NewValue);
            }
        };
        s.Key(Key.Up);
        s.Key(Key.Down);
        Assert.Equal(["", "Moved Write the weekly update, 09:30 to 10:45", "", "Moved Write the weekly update, 09:45 to 11:00"], texts);
    }

    [AvaloniaFact]
    public void Another_day_hides_the_chip_at_once()
    {
        using var s = new Scene(Timed("Chip", 9, 30, 60));
        s.Focus(s.Ids[0]);
        s.Key(Key.Down);
        Assert.True(s.Vm.Schedule.Chip.IsVisible);

        s.Vm.Show(new NavState(AppView.Day, s_free.AddDays(1)));
        Settle(s.Window);
        Assert.False(s.Vm.Schedule.Chip.IsVisible);
        Assert.Equal(0, s.Clock.ActiveTimers);
    }

    [AvaloniaFact]
    public void With_animations_off_the_chip_still_holds_for_900_ms()
    {
        var was = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false;
        try
        {
            Assert.Equal(s_hold, MotionTimes.ChipHold);
            using var s = new Scene(Timed("Chip", 9, 30, 60));
            s.Focus(s.Ids[0]);
            s.Key(Key.Down);
            Assert.Equal(new TimeOnly(9, 45), s.Task(s.Ids[0]).Time);
            Assert.True(s.Vm.Schedule.Chip.IsVisible);
            s.Clock.Advance(s_hold - s_tick);
            Assert.True(s.Vm.Schedule.Chip.IsVisible);
            s.Clock.Advance(s_tick);
            Assert.False(s.Vm.Schedule.Chip.IsVisible);
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = was;
        }
    }

    [AvaloniaFact]
    public void The_app_clock_hides_the_chip_on_its_own_on_the_ui_thread()
    {
        using var s = new Scene(Timed("Chip", 9, 30, 60));
        s.Vm.Schedule.HoldClock = null; // the app's default: a one-shot DispatcherTimer
        s.Focus(s.Ids[0]);
        var shown = DateTime.UtcNow;
        s.Key(Key.Down);
        Assert.True(s.Vm.Schedule.Chip.IsVisible);

        Assert.True(Pump.Until(() => !s.Vm.Schedule.Chip.IsVisible, TimeSpan.FromSeconds(5)));
        Assert.True(DateTime.UtcNow - shown >= s_hold - TimeSpan.FromMilliseconds(50));
    }

    // ---- Enter, Space ------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Enter_opens_the_task()
    {
        using var s = new Scene(Timed("Open me", 9, 30, 60));
        var id = s.Ids[0];
        s.Focus(id);

        s.Key(Key.Enter);
        Assert.Equal([id], s.Harness.Commands.Edits);
        Assert.Empty(s.Changes);
    }

    [AvaloniaFact]
    public void Space_toggles_done_once_even_when_held_and_never_opens()
    {
        using var s = new Scene(Timed("Toggle me", 9, 30, 60));
        var id = s.Ids[0];
        var open = s.Focus(id);

        for (var i = 0; i < 3; i++)
        {
            s.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); // held: auto-repeat
            Settle(s.Window);
        }

        Assert.True(s.Task(id).Done);
        Assert.Single(s.Changes);
        s.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Settle(s.Window);
        Assert.True(s.Task(id).Done);
        Assert.Empty(s.Harness.Commands.Edits); // the open button never clicked
        Assert.Same(open, s.Focused);

        PressSpace(s.Window);
        Assert.False(s.Task(id).Done);
        Assert.Equal(2, s.Changes.Count);
        Assert.Empty(s.Harness.Commands.Edits);
    }

    // ---- Tab ---------------------------------------------------------------------------------------------------------

    /// <summary>Focuses the first block by time and presses Tab through the blocks; returns the visited block ids.</summary>
    private static List<long> TabWalk(Window window, ScheduleTimeline timeline, ScheduleViewModel vm)
    {
        var first = vm.Blocks.MinBy(b => b.TabOrder)!;
        Assert.True(Part<Button>(timeline.BlockOf(first)!, "PART_Open").Focus(NavigationMethod.Tab));
        Settle(window);
        var walk = new List<long> { first.Id };
        while (true)
        {
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Settle(window);
            if (window.FocusManager?.GetFocusedElement() is not Button { Name: "PART_Open", DataContext: ScheduleBlockViewModel b })
            {
                return walk;
            }

            walk.Add(b.Id);
        }
    }

    private static List<long> ByTime(ScheduleViewModel vm) =>
        [.. vm.Blocks.OrderBy(b => b.StartMinutes).ThenByDescending(b => Math.Max(b.DurationMinutes, 30)).ThenBy(b => b.Id).Select(b => b.Id)];

    [AvaloniaFact]
    public void Tab_walks_the_blocks_by_time_on_Today()
    {
        using var harness = new ScreenHarness();
        var vm = new TodayViewModel(harness.Services);
        vm.Show(new NavState(AppView.Today, harness.App.DayClock.Today));
        var view = new TodayView { DataContext = vm };
        var window = ScreenHarness.Host(view);
        Settle(window);

        Assert.True(vm.Schedule.Blocks.Count >= 5);
        Assert.Equal(ByTime(vm.Schedule), TabWalk(window, view.Schedule, vm.Schedule));
        window.Close();
    }

    [AvaloniaFact]
    public void Tab_walks_the_blocks_by_time_on_Day_also_after_a_key_step_reorders_them()
    {
        using var s = new Scene(Timed("Late", 16, 0, 60), Timed("Long", 11, 0, 90), Timed("Short", 11, 0, 30), Timed("Early", 8, 0, 30));
        var (late, longer, shorter, early) = (s.Ids[0], s.Ids[1], s.Ids[2], s.Ids[3]);
        Assert.Equal([early, longer, shorter, late], TabWalk(s.Window, s.Timeline, s.Vm.Schedule)); // equal starts: longer first
        var order = s.Vm.Schedule.Blocks.Select(b => b.Id).ToList();

        // "Late" goes to 08:15, then ↑ to 08:00: it keeps its place in the collection (and its container) but is now
        // first by time (same start as "Early", longer).
        s.Harness.App.Store.Update(s.Task(late) with { Time = new TimeOnly(8, 15) });
        var container = s.Timeline.BlockOf(s.BlockVm(late));
        s.Focus(late);
        s.Key(Key.Up);
        Assert.Equal(new TimeOnly(8, 0), s.Task(late).Time);
        Assert.Equal(order, s.Vm.Schedule.Blocks.Select(b => b.Id));
        Assert.Same(container, s.Timeline.BlockOf(s.BlockVm(late)));
        Assert.Equal([late, early, longer, shorter], TabWalk(s.Window, s.Timeline, s.Vm.Schedule));
    }

    // ---- ← / → in Day --------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Left_and_Right_on_a_focused_block_still_go_to_the_previous_and_next_day()
    {
        using var shell = ShellFixture.Open(AppView.Day, new DateOnly(2026, 11, 16));
        var timeline = shell.Window.GetVisualDescendants().OfType<ScheduleTimeline>().Single(t => t.IsEffectivelyVisible);
        var block = timeline.Blocks().First();
        var open = Part<Button>(block, "PART_Open");
        Assert.True(open.Focus(NavigationMethod.Tab));
        shell.Settle();
        var writes = 0;
        shell.App.Store.Changed += (_, _) => writes++;

        shell.Key(Key.Right);
        Assert.Equal(new DateOnly(2026, 11, 17), shell.Vm.Nav.Anchor);
        Assert.Equal(AppView.Day, shell.Vm.Nav.View);
        shell.Key(Key.Left);
        Assert.Equal(new DateOnly(2026, 11, 16), shell.Vm.Nav.Anchor);
        Assert.Equal(0, writes);
    }
}
