using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Motion;
using Doba.App.Screens.Day;
using Doba.App.Screens.Today;
using Doba.App.Tests.Support;
using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using Doba.Core.Store;
using static Doba.App.Tests.Screens.Inbox.ListScreenKit;

namespace Doba.App.Tests.Controls;

/// <summary>
/// The schedule's pointer drag (the prototype's <c>sbDown</c> … <c>sbEnd</c>) with real headless mouse events on the
/// window (press, moves with the left button held, release): the click / drag threshold (4 DIP, 2 on the grip), snapping
/// to the absolute 15-minute grid (5 with Alt, also on Alt down / up), the 07:00 / 23:00 edges and the 15-minute minimum,
/// Esc, a lost capture, another day and a store change cancelling, exactly one store update per drop with the block's
/// view-model and container kept, an unspecified length kept on a move, nothing on the same slot, the checkbox and the
/// other buttons never dragging, the neighbours under the copy dimmed, the copy and the chip in place, the cursors,
/// autoscroll frame by frame in a short window, and both Today and Day.
/// </summary>
public class ScheduleDragTests
{
    /// <summary>A day without demo tasks.</summary>
    private static readonly DateOnly s_free = new(2026, 11, 20);

    private const RawInputModifiers Left = RawInputModifiers.LeftMouseButton;

    /// <summary>A Day (or Today) screen holding <paramref name="tasks"/>, with recorders of the store's changes, the
    /// requested frames and the mouse pointer.</summary>
    private sealed class Scene : IDisposable
    {
        public Scene(params TaskItem[] tasks)
            : this(ScreenHarness.ContentHeight, today: false, tasks)
        {
        }

        public Scene(double height, bool today, params TaskItem[] tasks)
        {
            Harness = new ScreenHarness();
            Ids = [.. tasks.Select(t => Harness.App.Store.Add(t).Id)];
            if (today)
            {
                var vm = new TodayViewModel(Harness.Services);
                vm.Show(new NavState(AppView.Today, Harness.App.DayClock.Today));
                Schedule = vm.Schedule;
                var view = new TodayView { DataContext = vm };
                Window = ScreenHarness.Host(view, ScreenHarness.ContentWidth, height);
                Timeline = view.Schedule;
            }
            else
            {
                Day = new DayViewModel(Harness.Services);
                Day.Show(new NavState(AppView.Day, s_free));
                Schedule = Day.Schedule;
                var view = new DayView { DataContext = Day };
                Window = ScreenHarness.Host(view, ScreenHarness.ContentWidth, height);
                Timeline = view.Schedule;
            }

            Schedule.HoldClock = Clock;
            Timeline.Drag.RequestFrame = Frames.Add;
            Settle(Window);
            Harness.App.Store.Changed += (_, e) => Changes.Add(e);
            Window.AddHandler(InputElement.PointerMovedEvent, (_, e) => Pointer = e.Pointer, RoutingStrategies.Tunnel, handledEventsToo: true);
            Window.AddHandler(InputElement.PointerPressedEvent, (_, e) => Pointer = e.Pointer, RoutingStrategies.Tunnel, handledEventsToo: true);
            Window.AddHandler(InputElement.KeyDownEvent, (_, e) => KeysDown.Add((e.Key, e.Handled)), RoutingStrategies.Bubble, handledEventsToo: true);
        }

        public ScreenHarness Harness { get; }

        public ManualClock Clock { get; } = new();

        public long[] Ids { get; }

        public DayViewModel? Day { get; }

        public ScheduleViewModel Schedule { get; }

        public Window Window { get; }

        public ScheduleTimeline Timeline { get; }

        public ScheduleDrag Drag => Timeline.Drag;

        public List<TaskStoreChangedEventArgs> Changes { get; } = [];

        public List<Action<TimeSpan>> Frames { get; } = [];

        public List<(Key Key, bool Handled)> KeysDown { get; } = [];

        /// <summary>The mouse pointer (seen on the window's tunnel).</summary>
        public IPointer? Pointer { get; private set; }

        public TaskItem Task(long id) => Harness.App.Store.Get(id)!;

        public ScheduleBlockViewModel BlockVm(long id) => Schedule.Blocks.Single(b => b.Id == id);

        public Panel Root(long id) => Timeline.BlockOf(BlockVm(id))!;

        public T PartOf<T>(long id, string name)
            where T : Control => Part<T>(Root(id), name);

        /// <summary>The centre of <paramref name="c"/> in window coordinates.</summary>
        public Point Centre(Control c)
        {
            Window.UpdateLayout();
            return c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), Window)!.Value;
        }

        /// <summary>The middle of a block (on its open button, clear of the checkbox and the grip).</summary>
        public Point Body(long id) => Centre(PartOf<Button>(id, "PART_Open"));

        /// <summary>The middle of a block's grip zone.</summary>
        public Point Grip(long id) => Centre(PartOf<Border>(id, "PART_Grip"));

        public void Down(Point p, MouseButton button = MouseButton.Left) => Window.MouseDown(p, button);

        public void Move(Point p, RawInputModifiers mods = Left)
        {
            Window.MouseMove(p, mods);
            Settle(Window);
        }

        public void Up(Point p, MouseButton button = MouseButton.Left)
        {
            Window.MouseUp(p, button);
            Settle(Window);
        }

        /// <summary>Press at <paramref name="p"/>, move by <paramref name="dy"/> (and <paramref name="dx"/>).</summary>
        public void Pull(Point p, double dy, double dx = 0, RawInputModifiers mods = Left)
        {
            Down(p);
            Move(p + new Point(dx, dy), mods);
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

    private static TaskItem Timed(string title, int hour, int minute, int duration, DateOnly? date = null, bool done = false) =>
        new(0, title, string.Empty, date ?? s_free, null, new TimeOnly(hour, minute), duration, Priority.None, null, done);

    private static int Minutes(int hour, int minute) => (hour * 60) + minute;

    private static double TopOf(int minutes) => ScheduleLayout.BlockTop(minutes, ScheduleLayout.DesignHourHeight);

    /// <summary>The drag shows nothing any more and holds no pointer.</summary>
    private static void AssertAtRest(Scene s)
    {
        Assert.Equal(ScheduleDragState.Idle, s.Drag.State);
        Assert.Null(s.Schedule.Dragged);
        Assert.False(s.Schedule.Copy.IsVisible);
        Assert.False(s.Timeline.CopyPanel.IsVisible);
        Assert.False(s.Schedule.Chip.IsVisible);
        Assert.All(s.Schedule.Blocks, b =>
        {
            Assert.False(b.IsOrigin);
            Assert.False(b.IsResizing);
            Assert.False(b.IsUnderCopy);
            Assert.Null(b.ResizePreview);
        });
        Assert.Null(s.Timeline.GridPanel.Cursor);
        Assert.NotSame(s.Timeline.GridPanel, s.Pointer?.Captured);
    }

    // ---- Click / drag threshold ----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_3_px_move_is_a_click_that_opens_the_editor_and_changes_nothing()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var p = s.Body(id);

        s.Pull(p, 3);
        Assert.Equal(ScheduleDragState.Pending, s.Drag.State);
        Assert.False(s.Schedule.Copy.IsVisible);
        s.Move(p + new Point(3, 3)); // diagonal 3 / 3: the larger of |dx| and |dy| is still 3
        Assert.Equal(ScheduleDragState.Pending, s.Drag.State);
        s.Up(p + new Point(3, 3));

        Assert.Equal([id], s.Harness.Commands.Edits);
        Assert.Equal(new TimeOnly(9, 30), s.Task(id).Time);
        Assert.Empty(s.Changes);
        AssertAtRest(s);
    }

    [AvaloniaFact]
    public void At_4_px_the_block_drags_the_grid_takes_the_pointer_and_the_editor_never_opens()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var vm = s.BlockVm(id);
        var open = s.PartOf<Button>(id, "PART_Open");
        var p = s.Body(id);

        s.Down(p);
        Assert.True(open.IsPressed);
        s.Move(p + new Point(-4, 1)); // |dx| = 4 is enough
        Assert.Equal(ScheduleDragState.Moving, s.Drag.State);
        Assert.Same(vm, s.Schedule.Dragged);
        Assert.Same(s.Timeline.GridPanel, s.Pointer!.Captured);
        Assert.False(open.IsPressed); // the open button lost its press with the capture: no click on release
        Assert.True(vm.IsOrigin);
        Assert.True(s.Schedule.Copy.IsVisible);
        Assert.Equal(new ScheduleSlot(Minutes(9, 30), 60), s.Schedule.Copy.Slot);
        Assert.True(s.Schedule.Chip.IsVisible);
        Assert.Equal("09:30", s.Schedule.Chip.Label);

        s.Up(p + new Point(-4, 1));
        Assert.Empty(s.Harness.Commands.Edits);
        Assert.Empty(s.Changes); // still 09:30: nothing to write
        Assert.Equal(new TimeOnly(9, 30), s.Task(id).Time);
        Assert.Equal(string.Empty, s.Timeline.LiveRegion.Text);
        AssertAtRest(s);
    }

    [AvaloniaFact]
    public void On_the_grip_a_1_px_move_does_nothing_and_2_px_start_a_resize()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var vm = s.BlockVm(id);
        var grip = s.Grip(id);

        s.Pull(grip, 1);
        Assert.Equal(ScheduleDragState.Pending, s.Drag.State);
        s.Up(grip + new Point(0, 1));
        Assert.Empty(s.Harness.Commands.Edits); // the grip is not the open button
        Assert.Empty(s.Changes);
        AssertAtRest(s);

        s.Pull(grip, 2);
        Assert.Equal(ScheduleDragState.Resizing, s.Drag.State);
        Assert.True(vm.IsResizing);
        Assert.False(vm.IsOrigin);
        Assert.False(s.Schedule.Copy.IsVisible); // a resize has no copy: the block itself grows
        Assert.Equal("10:30", s.Schedule.Chip.Label); // the new end
        Assert.Same(s.Timeline.GridPanel, s.Pointer!.Captured);
        s.Up(grip + new Point(0, 2));
        Assert.Empty(s.Changes); // 2 px is 2.5 min: the end stays 10:30
        Assert.Empty(s.Harness.Commands.Edits);
        AssertAtRest(s);
    }

    // ---- Resize ----------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_resize_grows_the_block_in_place_and_writes_its_length_once()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var vm = s.BlockVm(id);
        var root = s.Root(id);
        var grip = s.Grip(id);

        s.Pull(grip, 6); // 7.5 min: the end 10:37.5 rounds up to 10:45
        Assert.Equal(new ScheduleSlot(Minutes(9, 30), 75), s.Schedule.DragSlot);
        Assert.Equal(ScheduleLayout.BlockHeight(75, ScheduleLayout.DesignHourHeight), vm.Height);
        Assert.Equal(vm.Height, root.Bounds.Height);
        Assert.Equal(TopOf(Minutes(9, 30)), vm.Top); // in place: the top stays
        Assert.Equal("09:30 – 10:45 · 1h 15m", vm.Meta);
        Assert.Equal("10:45", s.Schedule.Chip.Label);
        Assert.True(Part<Border>(root, "PART_Shadow").IsVisible);
        Assert.Equal(1d, Part<Border>(root, "PART_Grip").GetBaseValue(Visual.OpacityProperty)); // its 180 ms fade aims at 1
        Assert.Empty(s.Changes); // nothing is written while dragging

        s.Up(grip + new Point(0, 6));
        Assert.Single(s.Changes);
        Assert.Equal(TaskChangeKind.Updated, s.Changes[0].Kind);
        Assert.Equal(new TimeOnly(9, 30), s.Task(id).Time);
        Assert.Equal(75, s.Task(id).DurationMinutes);
        Assert.Same(vm, s.BlockVm(id));
        Assert.Same(root, s.Root(id));
        Assert.Equal("09:30 – 10:45", vm.Meta);
        Assert.Equal("Resized Write the weekly update, 09:30 to 10:45", s.Timeline.LiveRegion.Text);
        Assert.Empty(s.Harness.Commands.Edits);
        AssertAtRest(s);
    }

    [AvaloniaFact]
    public void A_resize_keeps_at_least_15_minutes_and_ends_by_23_00()
    {
        using var s = new Scene(Timed("Short", 9, 30, 60), Timed("Late", 22, 0, 30));
        var (a, b) = (s.Ids[0], s.Ids[1]);

        var grip = s.Grip(a);
        s.Pull(grip, -1000);
        Assert.Equal(new ScheduleSlot(Minutes(9, 30), 15), s.Schedule.DragSlot);
        Assert.Equal(24, s.BlockVm(a).Height); // max(24, 12 − 3)
        s.Up(grip + new Point(0, -1000));
        Assert.Equal(15, s.Task(a).DurationMinutes);
        Assert.Equal(new TimeOnly(9, 30), s.Task(a).Time);

        grip = s.Grip(b);
        s.Pull(grip, 1000);
        Assert.Equal(new ScheduleSlot(Minutes(22, 0), 60), s.Schedule.DragSlot);
        Assert.Equal("23:00", s.Schedule.Chip.Label);
        s.Up(grip + new Point(0, 1000));
        Assert.Equal(60, s.Task(b).DurationMinutes);
        Assert.Equal(2, s.Changes.Count);
    }

    // ---- Move: snapping and edges ----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_move_snaps_to_the_absolute_15_minute_grid_and_with_Alt_to_5_minutes()
    {
        using var s = new Scene(Timed("Unaligned", 9, 37, 60));
        var id = s.Ids[0];
        var p = s.Body(id);

        s.Pull(p, 4); // 09:37 + 5 min = 09:42 → 09:45 (not 09:52: the grid is absolute)
        Assert.Equal(Minutes(9, 45), s.Schedule.Copy.Slot.Start);
        s.Move(p + new Point(0, 12)); // 09:52 → 09:45
        Assert.Equal(Minutes(9, 45), s.Schedule.Copy.Slot.Start);
        s.Move(p + new Point(0, 13)); // 09:53.25 → 10:00
        Assert.Equal(Minutes(10, 0), s.Schedule.Copy.Slot.Start);
        s.Move(p + new Point(0, -4)); // 09:32 → 09:30
        Assert.Equal(Minutes(9, 30), s.Schedule.Copy.Slot.Start);
        s.Move(p + new Point(0, -2), Left | RawInputModifiers.Alt); // 09:34.5 → 09:35
        Assert.Equal(Minutes(9, 35), s.Schedule.Copy.Slot.Start);
        s.Move(p + new Point(0, 4), Left | RawInputModifiers.Alt); // 09:42 → 09:40
        Assert.Equal(Minutes(9, 40), s.Schedule.Copy.Slot.Start);
        Assert.Equal("09:40", s.Schedule.Chip.Label);

        s.Up(p + new Point(0, 4));
        Assert.Single(s.Changes);
        Assert.Equal(new TimeOnly(9, 40), s.Task(id).Time);
        Assert.Equal(60, s.Task(id).DurationMinutes);
        Assert.Equal("Moved Unaligned, 09:40 to 10:40", s.Timeline.LiveRegion.Text);
    }

    [AvaloniaFact]
    public void Half_a_step_rounds_up_as_in_JavaScript()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var p = s.Body(s.Ids[0]);
        s.Pull(p, 6); // 7.5 min: 577.5 / 15 = 38.5 → 39 → 09:45
        Assert.Equal(Minutes(9, 45), s.Schedule.Copy.Slot.Start);
        s.Move(p + new Point(0, -6)); // 562.5 / 15 = 37.5 → 38 → 09:30
        Assert.Equal(Minutes(9, 30), s.Schedule.Copy.Slot.Start);
        s.Key(Key.Escape);
    }

    [AvaloniaFact]
    public void Alt_going_down_or_up_during_a_drag_snaps_again_without_a_pointer_move()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var p = s.Body(id);

        s.Pull(p, 6); // 09:37.5 → 09:45
        Assert.Equal(Minutes(9, 45), s.Schedule.Copy.Slot.Start);
        s.Window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        Settle(s.Window);
        Assert.Equal(Minutes(9, 40), s.Schedule.Copy.Slot.Start); // 09:37.5 → 09:40 on the 5-minute grid
        Assert.Contains((Key.LeftAlt, true), s.KeysDown);
        s.Window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Settle(s.Window);
        Assert.Equal(Minutes(9, 45), s.Schedule.Copy.Slot.Start);

        s.Up(p + new Point(0, 6));
        Assert.Equal(new TimeOnly(9, 45), s.Task(id).Time);
    }

    [AvaloniaFact]
    public void A_move_stops_at_07_00_and_at_23_00_minus_its_length()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var p = s.Body(id);

        s.Pull(p, -1000);
        Assert.Equal(new ScheduleSlot(Minutes(7, 0), 60), s.Schedule.Copy.Slot);
        Assert.Equal("07:00", s.Schedule.Chip.Label);
        s.Move(p + new Point(0, 2000));
        Assert.Equal(new ScheduleSlot(Minutes(22, 0), 60), s.Schedule.Copy.Slot);
        Assert.Equal(TopOf(Minutes(22, 0)), s.Schedule.Copy.Y);

        s.Up(p + new Point(0, 2000));
        Assert.Equal(new TimeOnly(22, 0), s.Task(id).Time);
        Assert.Equal(60, s.Task(id).DurationMinutes);
        Assert.Single(s.Changes);
    }

    // ---- One write, in place ---------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_drop_is_one_store_update_in_place_and_the_block_keeps_its_view_model_and_container()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60), Timed("Lunch", 13, 0, 60));
        var id = s.Ids[0];
        var vm = s.BlockVm(id);
        var root = s.Root(id);
        var other = s.BlockVm(s.Ids[1]);
        var p = s.Body(id);

        s.Pull(p, 30);
        s.Move(p + new Point(0, 48)); // + 1 h
        s.Up(p + new Point(0, 48));

        var change = Assert.Single(s.Changes);
        Assert.Equal(TaskChangeKind.Updated, change.Kind);
        Assert.Equal(id, change.New!.Id);
        Assert.Equal(new TimeOnly(10, 30), s.Task(id).Time);
        Assert.Equal(60, s.Task(id).DurationMinutes);
        Assert.Same(vm, s.BlockVm(id));
        Assert.Same(root, s.Root(id));
        Assert.Same(other, s.BlockVm(s.Ids[1]));
        Assert.Equal(TopOf(Minutes(10, 30)), vm.Top);
        Assert.Equal(vm.Top, root.Margin.Top);
        Assert.Equal("Moved Write the weekly update, 10:30 to 11:30", s.Timeline.LiveRegion.Text);
        Assert.Empty(s.Harness.Commands.Edits);
        AssertAtRest(s);
    }

    [AvaloniaFact]
    public void A_move_keeps_an_unspecified_length_unspecified_and_a_resize_writes_one()
    {
        using var s = new Scene(Timed("No length", 9, 30, 0));
        var id = s.Ids[0];

        var p = s.Body(id);
        s.Pull(p, 48);
        Assert.Equal(new ScheduleSlot(Minutes(10, 30), 30), s.Schedule.Copy.Slot);
        s.Up(p + new Point(0, 48));
        Assert.Equal(new TimeOnly(10, 30), s.Task(id).Time);
        Assert.Equal(0, s.Task(id).DurationMinutes);
        Assert.Equal("Moved No length, 10:30 to 11:00", s.Timeline.LiveRegion.Text);

        var grip = s.Grip(id);
        s.Pull(grip, 12);
        s.Up(grip + new Point(0, 12));
        Assert.Equal(new TimeOnly(10, 30), s.Task(id).Time);
        Assert.Equal(45, s.Task(id).DurationMinutes); // the shown 30 + 15
        Assert.Equal(2, s.Changes.Count);
    }

    [AvaloniaFact]
    public void Dropping_on_the_slot_it_started_from_writes_nothing()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var p = s.Body(id);

        s.Pull(p, 30);
        Assert.Equal(Minutes(10, 15), s.Schedule.Copy.Slot.Start);
        s.Move(p + new Point(20, 2));
        Assert.Equal(Minutes(9, 30), s.Schedule.Copy.Slot.Start);
        s.Up(p + new Point(20, 2));

        Assert.Empty(s.Changes);
        Assert.Empty(s.Harness.Commands.Edits);
        Assert.Equal(string.Empty, s.Timeline.LiveRegion.Text);
        AssertAtRest(s);
    }

    // ---- Cancel ----------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Esc_mid_drag_cancels_writes_nothing_and_the_release_does_not_open()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var p = s.Body(id);

        s.Pull(p, 30);
        Assert.Equal(ScheduleDragState.Moving, s.Drag.State);
        s.Key(Key.Escape);
        Assert.Contains((Key.Escape, true), s.KeysDown);
        AssertAtRest(s);
        Assert.Equal("Move cancelled", s.Timeline.LiveRegion.Text);

        s.Move(p + new Point(0, 60));
        s.Up(p + new Point(0, 60));
        Assert.Empty(s.Changes);
        Assert.Empty(s.Harness.Commands.Edits);
        Assert.Equal(new TimeOnly(9, 30), s.Task(id).Time);

        // Esc cancels a resize too.
        var grip = s.Grip(id);
        s.Pull(grip, 24);
        Assert.Equal(90, s.Schedule.DragSlot.Duration);
        s.Key(Key.Escape);
        AssertAtRest(s);
        Assert.Equal(ScheduleLayout.BlockHeight(60, ScheduleLayout.DesignHourHeight), s.BlockVm(id).Height);
        s.Up(grip + new Point(0, 24));
        Assert.Empty(s.Changes);
    }

    [AvaloniaFact]
    public void Losing_the_capture_cancels_the_drag()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var p = s.Body(id);

        // Released by someone else (Alt+Tab ends every capture).
        s.Pull(p, 30);
        s.Pointer!.Capture(null);
        Settle(s.Window);
        AssertAtRest(s);
        Assert.Equal("Move cancelled", s.Timeline.LiveRegion.Text);
        s.Up(p + new Point(0, 30));

        // Taken by another element.
        s.Pull(p, 30);
        Assert.Equal(ScheduleDragState.Moving, s.Drag.State);
        s.Pointer!.Capture(s.Timeline.LiveRegion);
        Settle(s.Window);
        AssertAtRest(s);
        s.Pointer.Capture(null);
        s.Up(p + new Point(0, 30));

        Assert.Empty(s.Changes);
        Assert.Empty(s.Harness.Commands.Edits);
        Assert.Equal(new TimeOnly(9, 30), s.Task(id).Time);
    }

    [AvaloniaFact]
    public void Another_day_or_a_store_change_of_the_dragged_task_cancels_and_another_task_changing_does_not()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var p = s.Body(id);

        // Another task changes: the drag goes on and its drop still writes.
        s.Pull(p, 30);
        s.Harness.App.Store.Add(Timed("Arrives mid-drag", 10, 0, 30));
        Settle(s.Window);
        Assert.Equal(ScheduleDragState.Moving, s.Drag.State);
        Assert.True(s.Schedule.Copy.IsVisible);
        s.Up(p + new Point(0, 30));
        Assert.Equal(new TimeOnly(10, 15), s.Task(id).Time);
        Assert.Equal(2, s.Changes.Count);

        // The dragged task itself changes in the store.
        var p2 = s.Body(id);
        s.Pull(p2, 48);
        s.Harness.App.Store.Update(s.Task(id) with { Title = "Renamed" });
        Settle(s.Window);
        AssertAtRest(s);
        Assert.Equal("Move cancelled", s.Timeline.LiveRegion.Text);
        s.Up(p2 + new Point(0, 48));
        Assert.Equal(3, s.Changes.Count); // only the rename
        Assert.Equal(new TimeOnly(10, 15), s.Task(id).Time);

        // Another day.
        var p3 = s.Body(id);
        s.Pull(p3, 48);
        Assert.Equal(ScheduleDragState.Moving, s.Drag.State);
        s.Day!.Show(new NavState(AppView.Day, s_free.AddDays(1)));
        Settle(s.Window);
        AssertAtRest(s);
        s.Up(p3 + new Point(0, 48));
        Assert.Equal(3, s.Changes.Count);
        Assert.Empty(s.Harness.Commands.Edits);
    }

    // ---- What never drags ------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_press_on_the_checkbox_never_drags()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var check = s.Centre(s.PartOf<ToggleButton>(id, "PART_Check"));

        s.Pull(check, 30);
        Assert.Equal(ScheduleDragState.Idle, s.Drag.State);
        s.Move(check + new Point(0, 60));
        Assert.False(s.Schedule.Copy.IsVisible);
        s.Up(check + new Point(0, 60));

        Assert.Empty(s.Changes); // released away from the checkbox: no toggle either
        Assert.Equal(new TimeOnly(9, 30), s.Task(id).Time);
        AssertAtRest(s);

        // A plain click on it still toggles, and does not open.
        Click(s.Window, s.PartOf<ToggleButton>(id, "PART_Check"));
        Assert.True(s.Task(id).Done);
        Assert.Empty(s.Harness.Commands.Edits);
    }

    [AvaloniaFact]
    public void The_right_and_middle_buttons_never_drag()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var p = s.Body(id);

        foreach (var (button, mods) in new[] { (MouseButton.Right, RawInputModifiers.RightMouseButton), (MouseButton.Middle, RawInputModifiers.MiddleMouseButton) })
        {
            s.Down(p, button);
            s.Move(p + new Point(0, 30), mods);
            Assert.Equal(ScheduleDragState.Idle, s.Drag.State);
            Assert.False(s.Schedule.Copy.IsVisible);
            s.Up(p + new Point(0, 30), button);
        }

        Assert.Empty(s.Changes);
        AssertAtRest(s);
    }

    [AvaloniaFact]
    public void Tasks_under_a_plus_N_are_not_draggable_and_a_drag_closes_the_open_list()
    {
        using var s = new Scene(Timed("A", 10, 0, 60), Timed("B", 10, 0, 45), Timed("C", 10, 0, 30), Timed("D", 10, 0, 20));
        var overflow = Assert.Single(s.Schedule.Overflows);
        var more = s.Timeline.MoreOf(overflow)!;

        var m = s.Centre(more);
        s.Pull(m, 30);
        Assert.Equal(ScheduleDragState.Idle, s.Drag.State);
        s.Up(m + new Point(0, 30));

        Click(s.Window, more);
        Assert.Same(overflow, s.Schedule.OpenOverflow);
        var row = s.Timeline.ListPopover.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_ListOpen");
        var r = s.Centre(row);
        s.Pull(r, 30);
        Assert.Equal(ScheduleDragState.Idle, s.Drag.State);
        s.Up(r + new Point(0, 30));
        Assert.Empty(s.Changes);
        Assert.Empty(s.Harness.Commands.Edits); // released off the row: no open either
        Assert.Same(overflow, s.Schedule.OpenOverflow);

        // A drawn block drags, and the drag closes the list.
        var a = s.Body(s.Ids[0]);
        s.Pull(a, 30);
        Assert.Equal(ScheduleDragState.Moving, s.Drag.State);
        Assert.Null(s.Schedule.OpenOverflow);
        Assert.False(s.Timeline.ListPopover.IsVisible);
        s.Key(Key.Escape);
        s.Up(a + new Point(0, 30));
        Assert.Empty(s.Changes);
    }

    [AvaloniaFact]
    public void During_a_drag_the_block_keys_are_swallowed_and_the_key_chip_gives_way()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        var open = s.PartOf<Button>(id, "PART_Open");
        Assert.True(open.Focus(NavigationMethod.Tab));
        s.Key(Key.Down); // a key step: 09:45, the chip holds
        Assert.Equal(new TimeOnly(9, 45), s.Task(id).Time);
        Assert.True(s.Schedule.Chip.IsVisible);
        Assert.Equal(1, s.Clock.ActiveTimers);

        var p = s.Body(id);
        s.Pull(p, 4);
        Assert.Equal(0, s.Clock.ActiveTimers); // the drag takes the chip over
        Assert.Equal("09:45", s.Schedule.Chip.Label);
        s.Key(Key.Down);
        s.Key(Key.Up, RawInputModifiers.Shift);
        s.Key(Key.Space);
        Assert.Single(s.Changes); // only the key step before the drag
        Assert.False(s.Task(id).Done);
        Assert.Equal(ScheduleDragState.Moving, s.Drag.State);

        s.Up(p + new Point(0, 4));
        Assert.Single(s.Changes);
        Assert.Empty(s.Harness.Commands.Edits);
        AssertAtRest(s); // the chip goes at once on release
    }

    // ---- What the drag shows ----------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Blocks_under_the_copy_dim_to_0_55_without_moving_and_come_back_after_the_drop()
    {
        using var s = new Scene(Timed("A", 9, 0, 60), Timed("B", 9, 30, 60), Timed("D", 12, 0, 60));
        var (a, b, d) = (s.Ids[0], s.Ids[1], s.Ids[2]);
        var bVm = s.BlockVm(b);
        var bBody = s.PartOf<Panel>(b, "PART_Body");
        var dBody = s.PartOf<Panel>(d, "PART_Body");
        var bGeometry = (bVm.X, bVm.Width, bVm.Top);
        var p = s.Body(a);

        s.Pull(p, 4); // the copy still at 09:00: B overlaps it
        Assert.True(bVm.IsUnderCopy);
        Assert.False(s.BlockVm(d).IsUnderCopy);
        Assert.False(s.BlockVm(a).IsUnderCopy); // never the dragged block itself
        // The dim is a composition opacity on the body (ScheduleTimeline.Motion.cs); the body's own opacity stays 1 and
        // the tag-filter dim stays on the root, so the two multiply.
        Assert.Equal(0.55, Animator.Destination(bBody).Opacity, 6);
        Assert.Equal(1, Animator.Destination(dBody).Opacity);
        Assert.Equal(1, bBody.Opacity);
        Assert.Equal(1, s.Root(b).Opacity);

        s.Move(p + new Point(0, 144)); // 12:00
        Assert.Equal(Minutes(12, 0), s.Schedule.Copy.Slot.Start);
        Assert.False(bVm.IsUnderCopy);
        Assert.True(s.BlockVm(d).IsUnderCopy);
        Assert.Equal(1, Animator.Destination(bBody).Opacity);
        Assert.Equal(0.55, Animator.Destination(dBody).Opacity, 6);
        Assert.Equal(bGeometry, (bVm.X, bVm.Width, bVm.Top)); // neighbours are not re-laid out during the drag

        s.Up(p + new Point(0, 144));
        Assert.Equal(new TimeOnly(12, 0), s.Task(a).Time);
        Assert.All(s.Schedule.Blocks, x => Assert.False(x.IsUnderCopy));
        Assert.Equal(1, Animator.Destination(dBody).Opacity);
        Assert.NotEqual(bGeometry.Width, bVm.Width); // after the drop B has the column to itself
    }

    [AvaloniaFact]
    public void A_block_being_resized_dims_the_blocks_under_its_new_length()
    {
        using var s = new Scene(Timed("A", 9, 0, 60), Timed("Later", 10, 30, 30));
        var later = s.BlockVm(s.Ids[1]);
        var grip = s.Grip(s.Ids[0]);
        s.Pull(grip, 2);
        Assert.False(later.IsUnderCopy);
        s.Move(grip + new Point(0, 48)); // the end 11:00 covers 10:30
        Assert.True(later.IsUnderCopy);
        s.Key(Key.Escape);
        Assert.False(later.IsUnderCopy);
    }

    [AvaloniaFact]
    public void The_copy_sits_at_the_new_slot_with_the_origin_x_and_width_and_the_chip_on_its_line()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 0, 60), Timed("Pair session", 9, 30, 60));
        var id = s.Ids[0];
        var vm = s.BlockVm(id);
        var root = s.Root(id);
        Assert.Equal(149, vm.Width); // two columns
        var p = s.Body(id);

        s.Pull(p, 48); // 10:00
        var copy = s.Timeline.CopyPanel;
        var copyAt = copy.TranslatePoint(default, s.Timeline.GridPanel)!.Value;
        Assert.Equal(new Point(vm.X, TopOf(Minutes(10, 0))), copyAt);
        Assert.Equal(new Size(vm.Width, ScheduleLayout.BlockHeight(60, ScheduleLayout.DesignHourHeight)), copy.Bounds.Size);
        Assert.Equal(0, copy.Bounds.Y); // stepped by translation, not by layout
        Assert.Equal("10:00 – 11:00 · 1h", s.Schedule.Copy.Meta);
        Assert.Equal("Write the weekly update", s.Schedule.Copy.Title);

        // The origin: dashed outline, frame and content hidden, still at 09:00.
        Assert.True(Part<ContentControl>(root, "PART_Dash").IsVisible);
        Assert.False(Part<Border>(root, "PART_Frame").IsVisible);
        Assert.Equal(0, Animator.Destination(Part<Border>(root, "PART_ContentBox")).Opacity); // fading out (composition)
        Assert.Equal(TopOf(Minutes(9, 0)), root.TranslatePoint(default, s.Timeline.GridPanel)!.Value.Y);

        // The chip: "10:00" on the 10:00 line, in the hour column.
        var chip = s.Timeline.ChipPanel;
        Assert.True(chip.IsVisible);
        Assert.Equal("10:00", s.Schedule.Chip.Label);
        Assert.Equal(new Point(0, ScheduleLayout.NowTop(Minutes(10, 0), ScheduleLayout.DesignHourHeight) - 8),
            chip.TranslatePoint(default, s.Timeline.GridPanel)!.Value);

        s.Move(p + new Point(0, 60)); // 10:15: one more step
        Assert.Equal(TopOf(Minutes(10, 15)), copy.TranslatePoint(default, s.Timeline.GridPanel)!.Value.Y);
        Assert.Equal("10:15", s.Schedule.Chip.Label);

        s.Key(Key.Escape);
        Assert.False(Part<ContentControl>(root, "PART_Dash").IsVisible);
        Assert.True(Part<Border>(root, "PART_Frame").IsVisible);
        Assert.Equal(MotionState.Rest, Animator.Shown(Part<Border>(root, "PART_ContentBox"))); // back at once
        s.Up(p + new Point(0, 60));
    }

    [AvaloniaFact]
    public void Cursors_hand_on_a_block_size_all_while_moving_size_north_south_on_the_grip_and_while_resizing()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var id = s.Ids[0];
        Assert.Equal("Hand", s.Root(id).Cursor?.ToString());
        Assert.Equal("SizeNorthSouth", s.PartOf<Border>(id, "PART_Grip").Cursor?.ToString());
        Assert.Null(s.Timeline.GridPanel.Cursor);

        var p = s.Body(id);
        s.Pull(p, 10);
        Assert.Same(ScheduleDrag.MoveCursor, s.Timeline.GridPanel.Cursor);
        Assert.Equal("SizeAll", s.Timeline.GridPanel.Cursor?.ToString());
        s.Up(p + new Point(0, 10));
        Assert.Null(s.Timeline.GridPanel.Cursor);

        var grip = s.Grip(id);
        s.Pull(grip, 10);
        Assert.Same(ScheduleDrag.ResizeCursor, s.Timeline.GridPanel.Cursor);
        Assert.Equal("SizeNorthSouth", s.Timeline.GridPanel.Cursor?.ToString());
        s.Key(Key.Escape);
        Assert.Null(s.Timeline.GridPanel.Cursor);
        s.Up(grip + new Point(0, 10));
    }

    // ---- Autoscroll ------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void In_a_short_window_the_edge_zones_autoscroll_frame_by_frame_and_the_slot_follows()
    {
        using var s = new Scene(400, today: false, Timed("Write the weekly update", 12, 0, 60));
        var id = s.Ids[0];
        var scroller = s.Timeline.Scroller;
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height + 1);
        var press = s.Body(id);
        var pressInScroller = s.Window.TranslatePoint(press, scroller)!.Value;
        var scroll0 = scroller.Offset.Y;
        var height = scroller.Bounds.Height;

        // Into the bottom zone, 10 px above the scroller's bottom: round(14 · 30 / 40) = round(10.5) = 11 px a frame.
        var bottom = scroller.TranslatePoint(new Point(pressInScroller.X, height - 10), s.Window)!.Value;
        s.Down(press);
        s.Move(bottom);
        Assert.Equal(ScheduleDragState.Moving, s.Drag.State);
        Assert.Single(s.Frames); // the loop asked for a frame
        Assert.Equal(11, ScheduleEdit.AutoScrollSpeed(height - 10, 0, height));
        Assert.Equal(scroll0, scroller.Offset.Y); // nothing scrolls before a frame

        Assert.True(s.Drag.Tick());
        Assert.Equal(scroll0 + 11, scroller.Offset.Y);
        Assert.True(s.Drag.Tick());
        Assert.Equal(scroll0 + 22, scroller.Offset.Y);
        var travel = (height - 10) - pressInScroller.Y + 22;
        Assert.Equal(ScheduleEdit.Drag(ScheduleDragMode.Move, Minutes(12, 0), 60, travel, false), s.Schedule.Copy.Slot);

        // A frame runs a tick and asks for the next one.
        s.Frames[0](TimeSpan.Zero);
        Assert.Equal(scroll0 + 33, scroller.Offset.Y);
        Assert.Equal(2, s.Frames.Count);

        // Out of the zones: no scroll, and the loop stops.
        var middle = scroller.TranslatePoint(new Point(pressInScroller.X, height / 2), s.Window)!.Value;
        s.Move(middle);
        var rest = scroller.Offset.Y;
        Assert.False(s.Drag.Tick());
        Assert.Equal(rest, scroller.Offset.Y);
        s.Frames[1](TimeSpan.Zero);
        Assert.Equal(2, s.Frames.Count);

        // The top zone scrolls up, at most 14 px a frame (at and beyond the edge).
        var top = scroller.TranslatePoint(new Point(pressInScroller.X, -5), s.Window)!.Value;
        s.Move(top);
        Assert.True(s.Drag.Tick());
        Assert.Equal(rest - 14, scroller.Offset.Y);

        // Down to the end of the extent: the scroll stops there.
        s.Move(bottom);
        for (var i = 0; i < 200; i++)
        {
            s.Drag.Tick();
        }

        var max = scroller.Extent.Height - scroller.Viewport.Height;
        Assert.Equal(max, scroller.Offset.Y);
        Assert.False(s.Drag.Tick());

        var landed = s.Schedule.Copy.Slot;
        travel = (height - 10) - pressInScroller.Y + (max - scroll0);
        Assert.Equal(ScheduleEdit.Drag(ScheduleDragMode.Move, Minutes(12, 0), 60, travel, false), landed);
        s.Up(bottom);
        Assert.Single(s.Changes);
        Assert.Equal(new TimeOnly(landed.Start / 60, landed.Start % 60), s.Task(id).Time);
    }

    [AvaloniaFact]
    public void When_the_grid_fits_there_is_no_autoscroll()
    {
        using var s = new Scene(Timed("Write the weekly update", 9, 30, 60));
        var scroller = s.Timeline.Scroller;
        Assert.False(scroller.Extent.Height > scroller.Viewport.Height + 1);
        var p = s.Body(s.Ids[0]);
        var bottom = scroller.TranslatePoint(new Point(200, scroller.Bounds.Height - 5), s.Window)!.Value;
        s.Down(p);
        s.Move(bottom);
        Assert.Empty(s.Frames);
        Assert.False(s.Drag.Tick());
        Assert.Equal(0, scroller.Offset.Y);
        s.Key(Key.Escape);
        s.Up(bottom);
    }

    // ---- Today -----------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Today_drags_too()
    {
        using var s = new Scene(ScreenHarness.ContentHeight, today: true);
        var today = s.Harness.App.DayClock.Today;
        var block = s.Schedule.Blocks.First(b => !b.Row.Done && b.StartMinutes + b.DurationMinutes + 60 <= ScheduleEdit.LastMinute);
        var id = block.Id;
        var start = block.StartMinutes;
        var p = s.Centre(Part<Button>(s.Timeline.BlockOf(block)!, "PART_Open"));

        s.Pull(p, 48);
        Assert.Equal(ScheduleDragState.Moving, s.Drag.State);
        Assert.Equal(start + 60, s.Schedule.Copy.Slot.Start);
        s.Up(p + new Point(0, 48));

        var change = Assert.Single(s.Changes);
        Assert.Equal(id, change.New!.Id);
        Assert.Equal(today, s.Task(id).Date);
        Assert.Equal(start + 60, (s.Task(id).Time!.Value.Hour * 60) + s.Task(id).Time!.Value.Minute);
        Assert.Same(block, s.Schedule.Blocks.Single(b => b.Id == id));
        Assert.Empty(s.Harness.Commands.Edits);
        AssertAtRest(s);
    }
}
