using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Motion;
using Doba.App.Tests.Support;
using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Navigation;
using static Doba.App.Tests.Screens.Inbox.ListScreenKit;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Shell;

/// <summary>
/// The design canvas's "Schedule states" artboards, captured from the real 1600×1000 window with DemoData and cropped to
/// the same 420 px frames (tools/design-render/jobs.js renders the references, docs/design/screens/schedule-*.png):
/// normal, hover, drag, resize, two and three side by side, the "+2" list, plus the keyboard focus ring. Hover, drag and
/// resize are produced with real pointer events; a drag is cancelled with Esc after the shot, so nothing is written.
/// </summary>
public class ScheduleScreenshotTests
{
    private const RawInputModifiers Left = RawInputModifiers.LeftMouseButton;

    /// <summary>The canvas frames: Main at 1600×1000 seen through a 420 px window at left 1176.</summary>
    private static readonly PixelRect s_main = new(1176, 161, 420, 300);
    private static readonly PixelRect s_two = new(1176, 393, 420, 100);
    private static readonly PixelRect s_three = new(1176, 491, 420, 140);
    private static readonly PixelRect s_overflow = new(1176, 251, 420, 250);

    /// <summary>Where the reference renderer parks the pointer (an empty part of the title bar).</summary>
    private static readonly Point s_park = new(300, 20);

    private const string Weekly = "Write the weekly update";

    private static ScheduleTimeline Timeline(ShellFixture shell) =>
        shell.Window.GetVisualDescendants().OfType<ScheduleTimeline>().Single(t => t.IsEffectivelyVisible);

    private static ScheduleViewModel Schedule(ScheduleTimeline t) => (ScheduleViewModel)t.DataContext!;

    private static ScheduleBlockViewModel BlockVm(ScheduleTimeline t, string title) =>
        Schedule(t).Blocks.Single(b => b.Row.Title == title);

    private static Point Centre(ShellFixture shell, Control c) =>
        c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), shell.Window)!.Value;

    private static ShellFixture OpenToday()
    {
        var shell = ShellFixture.Open(AppView.Today);
        shell.Window.MouseMove(s_park);
        shell.Settle();
        return shell;
    }

    private static void AssertPng(string path) => Assert.True(new FileInfo(path).Length > 2_000, path);

    [AvaloniaFact]
    public void Normal()
    {
        using var shell = OpenToday();
        var t = Timeline(shell);
        Assert.Null(Schedule(t).Dragged);
        Assert.DoesNotContain(t.Blocks(), b => b.IsPointerOver);
        AssertPng(shell.Capture("schedule-normal", s_main));
    }

    [AvaloniaFact]
    public void Hover()
    {
        using var shell = OpenToday();
        var t = Timeline(shell);
        var root = t.BlockOf(BlockVm(t, Weekly))!;
        shell.Window.MouseMove(Centre(shell, Part<Button>(root, "PART_Open")));
        shell.Settle();
        Assert.True(root.IsPointerOver);
        Assert.Equal(1, Part<Border>(root, "PART_Grip").Opacity);
        AssertPng(shell.Capture("schedule-hover", s_main));
    }

    [AvaloniaFact]
    public void Drag()
    {
        using var shell = OpenToday();
        AppMotion.SystemAnimationsEnabled = true; // the copy's lift (scale 1.02 + shadow) is skipped with animations off
        var t = Timeline(shell);
        var vm = BlockVm(t, Weekly);
        var root = t.BlockOf(vm)!;
        var from = Centre(shell, Part<Button>(root, "PART_Open"));

        // 09:30 → 11:15 (the canvas's drag artboard): 105 min = 84 px, plus 3 px that snap back to the slot.
        shell.Window.MouseMove(from);
        shell.Window.MouseDown(from, MouseButton.Left);
        shell.Window.MouseMove(from + new Point(0, 20), Left);
        shell.Window.MouseMove(from + new Point(0, 87), Left);
        shell.Settle();
        Assert.Same(vm, Schedule(t).Dragged);
        Assert.Equal(ScheduleDragMode.Move, Schedule(t).DragMode);
        Assert.Equal(new ScheduleSlot(675, 60), Schedule(t).DragSlot);
        Assert.Equal(AppMotion.LiftScale, Animator.Shown(Part<Panel>(t, "PART_CopyLift")).ScaleX);
        AssertPng(shell.Capture("schedule-drag", s_main));

        shell.Key(Key.Escape);
        shell.Window.MouseUp(from + new Point(0, 87), MouseButton.Left);
        shell.Settle();
        Assert.Null(Schedule(t).Dragged);
        Assert.Equal(570, BlockVm(t, Weekly).StartMinutes);
    }

    [AvaloniaFact]
    public void Resize()
    {
        using var shell = OpenToday();
        var t = Timeline(shell);
        var vm = BlockVm(t, Weekly);
        var root = t.BlockOf(vm)!;
        var from = Centre(shell, Part<Border>(root, "PART_Grip"));

        // 09:30–10:30 → 09:30–11:15 (the canvas's resize artboard): 45 min = 36 px.
        shell.Window.MouseMove(from);
        shell.Window.MouseDown(from, MouseButton.Left);
        shell.Window.MouseMove(from + new Point(0, 10), Left);
        shell.Window.MouseMove(from + new Point(0, 36), Left);
        shell.Settle();
        Assert.Same(vm, Schedule(t).Dragged);
        Assert.Equal(ScheduleDragMode.Resize, Schedule(t).DragMode);
        Assert.Equal(new ScheduleSlot(570, 105), Schedule(t).DragSlot);
        AssertPng(shell.Capture("schedule-resize", s_main));

        shell.Key(Key.Escape);
        shell.Window.MouseUp(from + new Point(0, 36), MouseButton.Left);
        shell.Settle();
        Assert.Equal(60, BlockVm(t, Weekly).DurationMinutes);
    }

    [AvaloniaFact]
    public void Two_side_by_side()
    {
        using var shell = OpenToday();
        var t = Timeline(shell);
        Assert.Equal(149, BlockVm(t, "Grocery delivery window").Width);
        AssertPng(shell.Capture("schedule-two", s_two));
    }

    [AvaloniaFact]
    public void Three_side_by_side()
    {
        using var shell = OpenToday();
        var t = Timeline(shell);
        Assert.Equal(98, BlockVm(t, "Pair session — auth refactor").Width);
        AssertPng(shell.Capture("schedule-three", s_three));
    }

    [AvaloniaFact]
    public void Overflow_list()
    {
        using var shell = ShellFixture.Open(AppView.Day, new DateOnly(2026, 11, 16));
        var t = Timeline(shell);
        var more = t.OverflowList.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_More");
        Assert.Equal("+2", Schedule(t).Overflows.Single().Text);
        shell.Click(more);
        shell.Window.MouseMove(s_park);
        shell.Settle();
        Assert.True(t.ListPopover.IsVisible);
        AssertPng(shell.Capture("schedule-overflow", s_overflow));
    }

    [AvaloniaFact]
    public void Keyboard_focus()
    {
        using var shell = OpenToday();
        var t = Timeline(shell);
        var open = Part<Button>(t.BlockOf(BlockVm(t, Weekly))!, "PART_Open");
        Assert.True(open.Focus(NavigationMethod.Tab));
        shell.Settle();
        Assert.True(open.IsKeyboardFocusWithin);
        AssertPng(shell.Capture("schedule-focus", s_main));
    }
}
