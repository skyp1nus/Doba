using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Motion;
using Doba.App.Screens.Day;
using Doba.App.Tests.DesignSystem;
using Doba.App.Tests.Support;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using Doba.Core.Store;
using static Doba.App.Tests.Screens.Inbox.ListScreenKit;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.MotionChecks;

/// <summary>
/// The schedule's motion (motion spec «Розклад» › «Рух», the prototype's <c>sbLift</c> / <c>sbFade</c> / <c>sbFlip</c>)
/// with real headless mouse and key events: every Animator handle's from / to / duration / easing (lift, copy and chip
/// glide, origin fade, neighbour dim, landing and its shadow, Esc flight, neighbour FLIP, key step, resize shadow, "+N"
/// pop), mid-flight continuity, only opacity / translation / scale animating, rapid repeats leaving nothing stuck, and
/// OS animations off (everything instant; the copy follows the pointer without lift, shadow or flight).
/// Finishing timers are held (<see cref="Animator.HoldFinishTimers"/>), so a handle runs until a test completes it.
/// </summary>
public class ScheduleMotionTests
{
    /// <summary>A day without demo tasks.</summary>
    private static readonly DateOnly s_free = new(2026, 11, 20);

    private const RawInputModifiers Left = RawInputModifiers.LeftMouseButton;

    private static readonly MotionState s_lifted = MotionState.Zoomed(AppMotion.LiftScale);

    /// <summary>A Day screen on <see cref="s_free"/> holding <paramref name="tasks"/>, built with OS animations on or off,
    /// with finishing timers held, a manual chip clock and no real autoscroll frames.</summary>
    private sealed class Scene : IDisposable
    {
        private readonly bool _animationsWere = AppMotion.SystemAnimationsEnabled;
        private readonly bool _holdWas = Animator.HoldFinishTimers;

        public Scene(params TaskItem[] tasks)
            : this(true, tasks)
        {
        }

        public Scene(bool animations, params TaskItem[] tasks)
        {
            AppMotion.SystemAnimationsEnabled = animations;
            Animator.HoldFinishTimers = true;
            Harness = new ScreenHarness();
            Ids = [.. tasks.Select(t => Harness.App.Store.Add(t).Id)];
            Vm = new DayViewModel(Harness.Services);
            Vm.Show(new NavState(AppView.Day, s_free));
            Vm.Schedule.HoldClock = new Doba.App.Tests.Controls.ManualClock();
            View = new DayView { DataContext = Vm };
            Window = ScreenHarness.Host(View, ScreenHarness.ContentWidth, ScreenHarness.ContentHeight);
            Timeline.Drag.RequestFrame = _ => { };
            Settle(Window);
            Harness.App.Store.Changed += (_, e) => Changes.Add(e);
        }

        public ScreenHarness Harness { get; }

        public long[] Ids { get; }

        public DayViewModel Vm { get; }

        public DayView View { get; }

        public Window Window { get; }

        public ScheduleTimeline Timeline => View.Schedule;

        public ScheduleViewModel Schedule => Vm.Schedule;

        public List<TaskStoreChangedEventArgs> Changes { get; } = [];

        public TaskItem Task(long id) => Harness.App.Store.Get(id)!;

        public ScheduleBlockViewModel BlockVm(long id) => Schedule.Blocks.Single(b => b.Id == id);

        public Panel Root(long id) => Timeline.BlockOf(BlockVm(id))!;

        public Control Container(long id) => Timeline.ContainerOf(BlockVm(id))!;

        public T PartOf<T>(long id, string name)
            where T : Control => Part<T>(Root(id), name);

        public Point Centre(Control c)
        {
            Window.UpdateLayout();
            return c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), Window)!.Value;
        }

        public Point Body(long id) => Centre(PartOf<Button>(id, "PART_Open"));

        public Point Grip(long id) => Centre(PartOf<Border>(id, "PART_Grip"));

        public void Down(Point p) => Window.MouseDown(p, MouseButton.Left);

        public void Move(Point p, RawInputModifiers mods = Left)
        {
            Window.MouseMove(p, mods);
            Settle(Window);
        }

        public void Up(Point p)
        {
            Window.MouseUp(p, MouseButton.Left);
            Settle(Window);
        }

        public void Pull(Point p, double dy)
        {
            Down(p);
            Move(p + new Point(0, dy));
        }

        public void Key(Key key, RawInputModifiers mods = RawInputModifiers.None)
        {
            Window.KeyPress(key, mods, PhysicalKey.None, null);
            Window.KeyRelease(key, mods, PhysicalKey.None, null);
            Settle(Window);
        }

        public void Focus(long id)
        {
            var open = PartOf<Button>(id, "PART_Open");
            Assert.True(open.Focus(NavigationMethod.Tab));
            Settle(Window);
        }

        public static MotionHandle? Running(Visual v) => Animator.Running(v);

        /// <summary>Every Animator animation under the timeline.</summary>
        public List<MotionHandle> Handles() =>
            [.. Timeline.GetVisualDescendants().Select(Animator.Running).OfType<MotionHandle>()];

        /// <summary>Completes every running animation (and the ones their callbacks start) under the timeline.</summary>
        public void Finish()
        {
            for (var i = 0; i < 8 && Handles() is { Count: > 0 } running; i++)
            {
                running.ForEach(h => h.Complete());
            }

            Assert.Empty(Handles());
            Settle(Window);
        }

        public void Dispose()
        {
            Window.Close();
            Harness.Dispose();
            Animator.HoldFinishTimers = _holdWas;
            AppMotion.SystemAnimationsEnabled = _animationsWere;
        }
    }

    private static TaskItem Timed(string title, int hour, int minute, int duration) =>
        new(0, title, string.Empty, s_free, null, new TimeOnly(hour, minute), duration, Priority.None, null, false);

    private static double TopOf(int hour, int minute) =>
        ScheduleLayout.BlockTop((hour * 60) + minute, ScheduleLayout.DesignHourHeight);

    private static MotionState Dimmed => new(Transit.Number("Opacity.Schedule.Under"), 0, 0, 1, 1);

    private static void NothingRuns(Scene s) => Assert.Empty(s.Handles());

    // ---- Lift and steps ------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_lift_scales_the_copy_fades_its_shadow_and_chip_in_and_fades_the_origin_content_and_dims_the_neighbours()
    {
        using var s = new Scene(Timed("A", 9, 0, 60), Timed("B", 9, 30, 60), Timed("D", 12, 0, 60));
        var (a, b, d) = (s.Ids[0], s.Ids[1], s.Ids[2]);
        s.Pull(s.Body(a), 4);
        Assert.True(s.Timeline.CopyPanel.IsVisible);

        var lift = Scene.Running(s.Timeline.CopyLiftPanel);
        Transit.Is(lift, MotionState.Rest, s_lifted, AppMotion.Lift, AppMotion.EaseOut);
        var copy = s.Schedule.Copy;
        Assert.Equal(new RelativePoint(copy.Width / 2, copy.Height / 2, RelativeUnit.Absolute), lift!.Origin); // its centre
        Assert.True(s.Timeline.CopyShadow.IsVisible);
        Transit.Is(Scene.Running(s.Timeline.CopyShadow), MotionState.Hidden, MotionState.Rest, AppMotion.Lift, AppMotion.EaseOut);
        Transit.Is(Scene.Running(s.PartOf<Border>(a, "PART_ContentBox")), MotionState.Rest, MotionState.Hidden, AppMotion.Fade, AppMotion.EaseOut);
        Transit.Is(Scene.Running(s.PartOf<Panel>(b, "PART_Body")), MotionState.Rest, Dimmed, AppMotion.Fade, AppMotion.EaseOut);
        Assert.Null(Scene.Running(s.PartOf<Panel>(d, "PART_Body"))); // not under the copy
        Transit.Is(Scene.Running(s.Timeline.ChipPanel), MotionState.Hidden, MotionState.Rest, AppMotion.Fade, AppMotion.EaseOut);
        Assert.Null(Scene.Running(s.Container(a))); // the origin stays where it is
        Assert.Empty(s.Changes);
    }

    [AvaloniaFact]
    public void The_copy_and_the_chip_glide_to_each_slot_from_where_they_are_drawn()
    {
        using var s = new Scene(Timed("A", 9, 0, 60));
        var a = s.Ids[0];
        var p = s.Body(a);
        s.Pull(p, 4);
        var copyPanel = s.Timeline.CopyPanel;
        var chip = s.Timeline.ChipPanel;
        var layout = (copyPanel.Margin, copyPanel.Bounds.Size);

        s.Move(p + new Point(0, 12)); // 09:15
        var step = Scene.Running(copyPanel);
        Transit.Is(step, MotionState.Offset(0, TopOf(9, 0) - TopOf(9, 15)), MotionState.Rest, AppMotion.Snap, AppMotion.EaseOut);
        Assert.Equal(TopOf(9, 15), copyPanel.TranslatePoint(default, s.Timeline.GridPanel)!.Value.Y); // at rest on its slot
        var chipStep = Scene.Running(chip);
        Assert.NotNull(chipStep);
        Assert.Equal(-12, chipStep.From.Y); // the fade-in had no offset
        Assert.InRange(chipStep.From.Opacity, 0, 1); // continues the fade-in
        Assert.Equal(MotionState.Rest, chipStep.To);
        Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.Snap), chipStep.Duration);
        Assert.Same(AppMotion.EaseOut, chipStep.Easing);

        s.Move(p + new Point(0, 24)); // 09:30 before the last glide ended: the next one starts where the copy is
        var next = Scene.Running(copyPanel);
        Assert.NotSame(step, next);
        Assert.True(step!.WasCancelled);
        Assert.InRange(next!.From.Y, -24, -12);
        Assert.Equal(MotionState.Rest, next.To);
        Assert.InRange(Scene.Running(chip)!.From.Y, -24, -12);

        // Layout never moves: the copy's box and margin stay, only its translation changes.
        Assert.Equal(layout, (copyPanel.Margin, copyPanel.Bounds.Size));
        Assert.Equal(0, copyPanel.Bounds.Y);
        s.Up(p + new Point(0, 24));
    }

    [AvaloniaFact]
    public void Neighbours_dim_under_the_copy_and_come_back_continuing_from_where_they_are()
    {
        using var s = new Scene(Timed("A", 9, 0, 60), Timed("B", 9, 30, 60));
        var (a, b) = (s.Ids[0], s.Ids[1]);
        var body = s.PartOf<Panel>(b, "PART_Body");
        var p = s.Body(a);
        s.Pull(p, 4);
        var dim = Scene.Running(body);
        Transit.Is(dim, MotionState.Rest, Dimmed, AppMotion.Fade, AppMotion.EaseOut);

        s.Move(p + new Point(0, 12)); // 09:15: still over B — the same dim keeps running
        Assert.Same(dim, Scene.Running(body));

        s.Move(p + new Point(0, 144)); // 12:00: B is clear again
        var back = Scene.Running(body);
        Assert.True(dim!.WasCancelled);
        Assert.NotNull(back);
        Assert.InRange(back.From.Opacity, Dimmed.Opacity, 1);
        Assert.Equal(MotionState.Rest, back.To);
        Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.Fade), back.Duration);
        back.Complete();

        s.Move(p + new Point(0, 12)); // over B again: from rest
        Transit.Is(Scene.Running(body), MotionState.Rest, Dimmed, AppMotion.Fade, AppMotion.EaseOut);
        Scene.Running(body)!.Complete();
        Assert.Equal(Dimmed, Animator.Shown(body)); // held at 0.55 between steps
        Assert.Equal(1, body.Opacity); // the body's own opacity stays 1; the dim is composition only

        s.Key(Key.Escape);
        Transit.Is(Scene.Running(body), Dimmed, MotionState.Rest, AppMotion.Fade, AppMotion.EaseOut);
        s.Up(p + new Point(0, 12));
    }

    // ---- Landing --------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_drop_lands_from_the_copy_scaling_down_with_its_shadow_out_and_the_neighbours_slide_to_their_new_place()
    {
        using var s = new Scene(Timed("A", 9, 0, 60), Timed("B", 9, 30, 60));
        var (a, b) = (s.Ids[0], s.Ids[1]);
        var bVm = s.BlockVm(b);
        Assert.Equal(207, bVm.X); // second of two columns
        var p = s.Body(a);
        s.Pull(p, 4);
        s.Move(p + new Point(0, 144)); // 12:00
        s.Finish(); // lifted and arrived

        s.Up(p + new Point(0, 144));
        Assert.Equal(new TimeOnly(12, 0), s.Task(a).Time);
        Assert.Single(s.Changes);
        Assert.False(s.Timeline.CopyPanel.IsVisible); // gone at once
        Assert.False(s.Timeline.ChipPanel.IsVisible);
        Assert.Null(Scene.Running(s.Timeline.CopyPanel));
        Assert.Equal(MotionState.Rest, Animator.Shown(s.Timeline.CopyLiftPanel)); // reset for the next drag
        Assert.Equal(MotionState.Rest, Animator.Shown(s.Timeline.CopyShadow));

        // The block flies from the copy (same place, lifted) into its slot.
        var aVm = s.BlockVm(a);
        var container = s.Container(a);
        var land = Scene.Running(container);
        Transit.Is(land, new MotionState(1, 0, 0, AppMotion.LiftScale, AppMotion.LiftScale), MotionState.Rest, AppMotion.Land, AppMotion.EaseOut);
        Assert.Equal(new RelativePoint(aVm.X + (aVm.Width / 2), aVm.Top + (aVm.Height / 2), RelativeUnit.Absolute), land!.Origin);
        Assert.Equal(1, container.ZIndex); // above the other blocks while it lands
        Assert.Equal(MotionState.Rest, Animator.Shown(s.PartOf<Border>(a, "PART_ContentBox"))); // its content is back at once

        var shadow = s.PartOf<Border>(a, "PART_Shadow");
        Assert.Contains("landing", s.Root(a).Classes);
        Assert.True(shadow.IsVisible);
        Transit.Is(Scene.Running(shadow), MotionState.Rest, MotionState.Hidden, AppMotion.Land, AppMotion.EaseIn);

        // B has the column to itself now: it slides from x 207 to 54; its width jumps.
        Assert.Equal(54, bVm.X);
        Assert.Equal(302, s.Root(b).Bounds.Width);
        Transit.Is(Scene.Running(s.Container(b)), MotionState.Offset(207 - 54, 0), MotionState.Rest, AppMotion.Land, AppMotion.EaseOut);

        s.Finish();
        Assert.Equal(0, container.ZIndex);
        Assert.DoesNotContain("landing", s.Root(a).Classes);
        Assert.False(shadow.IsVisible);
        Assert.Equal(MotionState.Rest, Animator.Shown(shadow));
        Assert.Equal(MotionState.Rest, Animator.Shown(container));
    }

    [AvaloniaFact]
    public void Esc_flies_the_block_back_from_the_copy_to_its_old_place_and_writes_nothing()
    {
        using var s = new Scene(Timed("A", 9, 0, 60));
        var a = s.Ids[0];
        var p = s.Body(a);
        s.Pull(p, 4);
        s.Move(p + new Point(0, 48)); // 10:00
        s.Finish();

        s.Key(Key.Escape);
        Assert.Empty(s.Changes);
        Assert.Equal(new TimeOnly(9, 0), s.Task(a).Time);
        Transit.Is(Scene.Running(s.Container(a)),
            new MotionState(1, 0, TopOf(10, 0) - TopOf(9, 0), AppMotion.LiftScale, AppMotion.LiftScale), MotionState.Rest,
            AppMotion.Land, AppMotion.EaseOut);
        Transit.Is(Scene.Running(s.PartOf<Border>(a, "PART_Shadow")), MotionState.Rest, MotionState.Hidden, AppMotion.Land, AppMotion.EaseIn);
        s.Up(p + new Point(0, 48));
        Assert.Empty(s.Changes);
    }

    [AvaloniaFact]
    public void Esc_mid_glide_and_mid_lift_flies_back_from_where_the_copy_is_drawn()
    {
        using var s = new Scene(Timed("A", 9, 0, 60));
        var a = s.Ids[0];
        var p = s.Body(a);
        s.Pull(p, 4);
        s.Move(p + new Point(0, 48)); // the copy glides from 09:00 to 10:00; nothing finished

        s.Key(Key.Escape);
        var back = Scene.Running(s.Container(a));
        Assert.NotNull(back);
        Assert.InRange(back.From.Y, 0, 48);
        Assert.InRange(back.From.ScaleX, 1, AppMotion.LiftScale);
        Assert.Equal(MotionState.Rest, back.To);
        Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.Land), back.Duration);
        Assert.InRange(Scene.Running(s.PartOf<Border>(a, "PART_Shadow"))!.From.Opacity, 0, 1);
        s.Up(p + new Point(0, 48));
    }

    [AvaloniaFact]
    public void A_resize_fades_the_shadow_in_and_out_while_the_height_jumps()
    {
        using var s = new Scene(Timed("A", 9, 0, 60));
        var a = s.Ids[0];
        var root = s.Root(a);
        var shadow = s.PartOf<Border>(a, "PART_Shadow");
        var grip = s.Grip(a);

        s.Pull(grip, 2);
        Assert.True(shadow.IsVisible);
        Transit.Is(Scene.Running(shadow), MotionState.Hidden, MotionState.Rest, AppMotion.Hover, AppMotion.EaseStd);

        s.Move(grip + new Point(0, 12)); // 75 min
        Assert.Equal(ScheduleLayout.BlockHeight(75, ScheduleLayout.DesignHourHeight), root.Bounds.Height); // jumps
        Assert.Null(Scene.Running(s.Container(a)));
        Assert.All(s.Handles(), h => Assert.True(ReferenceEquals(h.Target, shadow) || ReferenceEquals(h.Target, s.Timeline.ChipPanel)));

        s.Up(grip + new Point(0, 12));
        Assert.Equal(75, s.Task(a).DurationMinutes);
        Assert.Contains("landing", root.Classes);
        Assert.True(shadow.IsVisible);
        var fade = Scene.Running(shadow);
        Assert.NotNull(fade);
        Assert.InRange(fade.From.Opacity, 0, 1); // from where the fade-in was
        Assert.Equal(MotionState.Hidden, fade.To);
        Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.Hover), fade.Duration);
        Assert.Same(AppMotion.EaseStd, fade.Easing);
        Assert.Null(Scene.Running(s.Container(a))); // it did not move

        s.Finish();
        Assert.False(shadow.IsVisible);
        Assert.DoesNotContain("landing", root.Classes);
    }

    // ---- Keyboard -------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_key_step_slides_the_block_from_its_old_place_by_snap_and_the_neighbours_by_land()
    {
        using var s = new Scene(Timed("A", 9, 0, 60), Timed("B", 10, 0, 60));
        var (a, b) = (s.Ids[0], s.Ids[1]);
        s.Focus(a);

        s.Key(Key.Down); // 09:15–10:15 now overlaps B: A takes the first column, B the second
        Assert.Equal(new TimeOnly(9, 15), s.Task(a).Time);
        Assert.Equal(207, s.BlockVm(b).X);
        var step = Scene.Running(s.Container(a));
        Transit.Is(step, MotionState.Offset(0, TopOf(9, 0) - TopOf(9, 15)), MotionState.Rest, AppMotion.Snap, AppMotion.EaseOut);
        Assert.Equal(1, s.Container(a).ZIndex);
        var slide = Scene.Running(s.Container(b));
        Transit.Is(slide, MotionState.Offset(54 - 207, 0), MotionState.Rest, AppMotion.Land, AppMotion.EaseOut);
        Transit.Is(Scene.Running(s.Timeline.ChipPanel), MotionState.Hidden, MotionState.Rest, AppMotion.Fade, AppMotion.EaseOut);

        s.Key(Key.Down); // again before anything ended
        var next = Scene.Running(s.Container(a));
        Assert.True(step!.WasCancelled);
        Assert.InRange(next!.From.Y, -24, -12);
        Assert.Same(slide, Scene.Running(s.Container(b))); // B did not move again: its slide goes on
        var chip = Scene.Running(s.Timeline.ChipPanel);
        Assert.Equal(-12, chip!.From.Y);
        Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.Snap), chip.Duration);

        s.Finish();
        Assert.Equal(0, s.Container(a).ZIndex);
        Assert.Same(s.PartOf<Button>(a, "PART_Open"), s.Window.FocusManager?.GetFocusedElement()); // focus kept
    }

    // ---- "+N" list ---------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_plus_n_list_pops_from_its_corner()
    {
        using var s = new Scene(Timed("A", 10, 0, 60), Timed("B", 10, 0, 60), Timed("C", 10, 0, 60), Timed("D", 10, 0, 60));
        var overflow = Assert.Single(s.Schedule.Overflows);
        var more = s.Timeline.MoreOf(overflow)!;
        Click(s.Window, more);
        var pop = Scene.Running(s.Timeline.ListPopover);
        Transit.Is(pop, new MotionState(0, 0, AppMotion.PopY, AppMotion.PopScale, AppMotion.PopScale), MotionState.Rest,
            AppMotion.Pop, AppMotion.EaseOut);
        Assert.Equal(RelativeUnit.Absolute, pop!.Origin.Unit);
        Assert.Equal(ScheduleLayout.PopoverWidth, pop.Origin.Point.X); // the top-right corner under the "+N"
        Assert.Equal(0, pop.Origin.Point.Y);
    }

    // ---- What may animate -----------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void During_a_drag_only_opacity_translation_and_scale_animate_and_layout_never_does()
    {
        using var s = new Scene(Timed("A", 9, 0, 60), Timed("B", 9, 30, 60), Timed("C", 9, 45, 30));
        var (a, b) = (s.Ids[0], s.Ids[1]);
        var allowed = new HashSet<string> { "PART_Body", "PART_ContentBox", "PART_Shadow", "PART_Copy", "PART_CopyLift", "PART_CopyShadow", "PART_Chip", "PART_List" };
        var bRoot = s.Root(b);
        var bLayout = (bRoot.Margin, bRoot.Bounds);
        var p = s.Body(a);

        s.Pull(p, 4);
        var seen = new HashSet<Visual>();
        for (var dy = 16; dy <= 160; dy += 12)
        {
            s.Move(p + new Point(0, dy));
            foreach (var h in s.Handles())
            {
                seen.Add(h.Target);
                // Animator handles are composition opacity, translation and scale on the layers made for them.
                Assert.True(h.Target is Control { Name: { } name } && allowed.Contains(name), $"{h.Target} animates");
            }

            Assert.Equal(bLayout, (bRoot.Margin, bRoot.Bounds)); // neighbours are not re-laid out
        }

        Assert.Contains(s.Timeline.CopyPanel, seen);
        Assert.Contains(s.Timeline.ChipPanel, seen);

        // XAML transitions in the timeline animate only opacity and colours: never a margin, size or transform.
        foreach (var v in s.Timeline.GetVisualDescendants().OfType<Animatable>())
        {
            foreach (var t in v.Transitions?.OfType<TransitionBase>() ?? [])
            {
                Assert.NotNull(t.Property);
                Assert.True(t.Property == Visual.OpacityProperty || typeof(IBrush).IsAssignableFrom(t.Property.PropertyType)
                    || t.Property.PropertyType == typeof(Color), $"{v.GetType().Name} transitions {t.Property.Name}");
            }
        }

        s.Up(p + new Point(0, 160));
        Assert.All(s.Handles(), h => Assert.True(h.Target is ContentPresenter || h.Target is Control { Name: { } n } && allowed.Contains(n)));
    }

    [AvaloniaFact]
    public void Rapid_drags_drops_cancels_and_key_steps_leave_no_stuck_opacity_offset_or_scale()
    {
        using var s = new Scene(Timed("A", 9, 0, 60), Timed("B", 9, 30, 60), Timed("C", 13, 0, 30));
        var (a, b) = (s.Ids[0], s.Ids[1]);
        for (var i = 0; i < 4; i++)
        {
            var p = s.Body(a);
            s.Pull(p, 4);
            s.Move(p + new Point(0, 24 + (12 * i)));
            s.Up(p + new Point(0, 24 + (12 * i))); // drop, landing still running

            p = s.Body(a);
            s.Pull(p, 4); // the next drag right away
            s.Move(p - new Point(0, 24));
            s.Key(Key.Escape);
            s.Up(p - new Point(0, 24));

            var grip = s.Grip(b);
            s.Pull(grip, 2);
            s.Move(grip + new Point(0, 12));
            s.Up(grip + new Point(0, 12));

            s.Focus(a);
            s.Key(Key.Up);
            s.Key(Key.Down, RawInputModifiers.Shift);
        }

        s.Finish();
        Assert.All(s.Timeline.GetVisualDescendants(), v => Assert.Equal(MotionState.Rest, Animator.Shown(v)));
        Assert.All(s.Schedule.Blocks, vm =>
        {
            Assert.False(vm.IsUnderCopy);
            Assert.False(vm.IsOrigin);
            var container = s.Timeline.ContainerOf(vm)!;
            Assert.Equal(0, container.ZIndex);
            Assert.DoesNotContain("landing", s.Timeline.BlockOf(vm)!.Classes);
            Assert.False(Part<Border>(s.Timeline.BlockOf(vm)!, "PART_Shadow").IsVisible);
        });
        Assert.False(s.Timeline.CopyPanel.IsVisible);
    }

    // ---- OS animations off ------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Animations_off_the_copy_follows_the_pointer_without_lift_shadow_or_flight()
    {
        using var s = new Scene(false, Timed("A", 9, 0, 60), Timed("B", 9, 30, 60));
        var (a, b) = (s.Ids[0], s.Ids[1]);
        var p = s.Body(a);

        s.Pull(p, 4);
        NothingRuns(s);
        Assert.False(s.Timeline.CopyShadow.IsVisible); // no shadow
        Assert.Equal(MotionState.Rest, Animator.Shown(s.Timeline.CopyLiftPanel)); // no lift scale
        Assert.Equal(MotionState.Hidden, Animator.Shown(s.PartOf<Border>(a, "PART_ContentBox"))); // origin content gone at once
        Assert.Equal(Dimmed, Animator.Shown(s.PartOf<Panel>(b, "PART_Body"))); // dimmed at once
        Assert.True(s.Timeline.ChipPanel.IsVisible);
        Assert.Equal(MotionState.Rest, Animator.Shown(s.Timeline.ChipPanel));

        s.Move(p + new Point(0, 48)); // 10:00: the copy is there at once
        NothingRuns(s);
        Assert.Equal(TopOf(10, 0), s.Timeline.CopyPanel.TranslatePoint(default, s.Timeline.GridPanel)!.Value.Y);
        Assert.Equal(MotionState.Rest, Animator.Shown(s.Timeline.CopyPanel));
        Assert.Equal(Dimmed, Animator.Shown(s.PartOf<Panel>(b, "PART_Body"))); // 10:00–11:00 still covers B

        s.Move(p + new Point(0, 144)); // 12:00: B is clear, back at once
        NothingRuns(s);
        Assert.Equal(MotionState.Rest, Animator.Shown(s.PartOf<Panel>(b, "PART_Body")));
        s.Up(p + new Point(0, 144));
        Assert.Equal(new TimeOnly(12, 0), s.Task(a).Time);
        NothingRuns(s);
        Assert.Equal(MotionState.Rest, Animator.Shown(s.Container(a))); // no flight, no landing scale
        Assert.Equal(MotionState.Rest, Animator.Shown(s.Container(b)));
        Assert.DoesNotContain("landing", s.Root(a).Classes);
        Assert.False(s.PartOf<Border>(a, "PART_Shadow").IsVisible);
        Assert.Equal(MotionState.Rest, Animator.Shown(s.PartOf<Border>(a, "PART_ContentBox")));
    }

    [AvaloniaFact]
    public void Animations_off_the_dim_is_drawn_at_once_and_goes_at_once()
    {
        using var s = new Scene(false, Timed("A", 9, 0, 60), Timed("B", 9, 30, 60));
        var (a, b) = (s.Ids[0], s.Ids[1]);
        var bVm = s.BlockVm(b);
        // A point on B's frame clear of its text, checkbox and grip (the copy stays over A's column).
        var probe = s.Root(b).TranslatePoint(new Point(bVm.Width - 6, bVm.Height / 2), s.Window)!.Value;
        var at = ((int)probe.X, (int)probe.Y);
        var rest = Pixels.At(s.Window, at.Item1, at.Item2);

        var p = s.Body(a);
        s.Pull(p, 4);
        var dimmed = Pixels.At(s.Window, at.Item1, at.Item2);
        Assert.False(Pixels.Near(rest, dimmed, 1), $"B is drawn dimmed ({rest} → {dimmed})");

        s.Key(Key.Escape);
        s.Up(p + new Point(0, 4));
        Assert.True(Pixels.Near(rest, Pixels.At(s.Window, at.Item1, at.Item2), 1));
        NothingRuns(s);
    }

    [AvaloniaFact]
    public void Animations_off_a_resize_and_a_key_step_are_instant()
    {
        using var s = new Scene(false, Timed("A", 9, 0, 60), Timed("B", 10, 0, 60));
        var (a, b) = (s.Ids[0], s.Ids[1]);
        var grip = s.Grip(a);
        s.Pull(grip, 2);
        NothingRuns(s);
        Assert.True(s.PartOf<Border>(a, "PART_Shadow").IsVisible); // the resize state, drawn at once
        s.Move(grip + new Point(0, 12));
        s.Up(grip + new Point(0, 12));
        NothingRuns(s);
        Assert.False(s.PartOf<Border>(a, "PART_Shadow").IsVisible);

        s.Focus(a);
        s.Key(Key.Down);
        NothingRuns(s);
        Assert.Equal(207, s.BlockVm(b).X);
        Assert.Equal(MotionState.Rest, Animator.Shown(s.Container(a)));
        Assert.Equal(MotionState.Rest, Animator.Shown(s.Container(b)));
        Assert.True(s.Schedule.Chip.IsVisible); // the 900 ms hold is information, not motion
        Assert.Equal(MotionState.Rest, Animator.Shown(s.Timeline.ChipPanel));

        // The hover layer and the grip have zero-length transitions.
        Assert.Equal(TimeSpan.Zero, Transit.Of(s.PartOf<Border>(a, "PART_Hover"), Visual.OpacityProperty).Duration);
        Assert.Equal(TimeSpan.Zero, Transit.Of(s.PartOf<Border>(a, "PART_Grip"), Visual.OpacityProperty).Duration);
    }
}
