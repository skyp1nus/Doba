using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Motion;
using Doba.App.Tests.MotionChecks;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.DesignSystem;

/// <summary>
/// Animator semantics: one completion per handle, cancel restores the resting state (checked on rendered pixels),
/// rapid re-starts never leave content invisible, OS animations off → instant.
/// </summary>
public class AnimatorTests
{
    private static readonly Color Panel = Color.Parse("#EDEDEF");

    private static (Window Window, Border Target) Scene()
    {
        var target = new Border
        {
            Width = 100, Height = 100,
            Background = new SolidColorBrush(Panel),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = new Window { Width = 200, Height = 200, Content = target };
        window.Show();
        window.UpdateLayout();
        return (window, target);
    }

    private static bool Visible(Window window) => Pixels.Near(Panel, Pixels.At(window, 50, 50), 6);

    private static bool Hidden(Window window) => Pixels.Near(Pixels.Resource("Color.Bg.Window"), Pixels.At(window, 50, 50), 6);

    [AvaloniaFact]
    public void Cancelled_fade_out_restores_full_opacity()
    {
        var (window, target) = Scene();
        var calls = 0;
        var handle = Animator.FadeOut(target, AppMotion.Exit, _ => calls++);
        Assert.True(handle.IsAnimating);
        Assert.True(Animator.IsAnimating(target));

        handle.Cancel();

        Assert.False(handle.IsAnimating);
        Assert.True(handle.WasCancelled);
        Assert.Equal(1, calls);
        Assert.True(handle.Finished.IsCompleted);
        Assert.False(handle.Finished.Result);
        Assert.False(Animator.IsAnimating(target));
        Assert.True(Visible(window));

        handle.Cancel();
        handle.Complete();
        Assert.Equal(1, calls); // finishing is idempotent
        window.Close();
    }

    [AvaloniaFact]
    public void Completed_fade_out_hides_and_cancel_of_a_new_entrance_shows_again()
    {
        var (window, target) = Scene();
        var handle = Animator.FadeOut(target);
        handle.Complete();
        Assert.False(handle.WasCancelled);
        Assert.True(handle.Finished.Result);
        Assert.True(Hidden(window));
        Assert.Equal(1.0, target.Opacity); // the UI-side opacity is never touched

        Animator.FadeIn(target).Cancel();
        Assert.True(Visible(window));
        window.Close();
    }

    [AvaloniaFact]
    public void Reset_shows_an_element_left_hidden_by_an_exit()
    {
        var (window, target) = Scene();
        Animator.SlideTo(target, 0, -AppMotion.SectionExitY).Complete();
        Assert.True(Hidden(window));
        Animator.Reset(target);
        Assert.True(Visible(window));
        window.Close();
    }

    [AvaloniaFact]
    public void New_animation_supersedes_the_running_one()
    {
        var (window, target) = Scene();
        MotionHandle? superseded = null;
        var first = Animator.FadeOut(target, AppMotion.Exit, h => superseded = h);
        var second = Animator.SlideFrom(target, 0, AppMotion.SectionEnterY, AppMotion.Section);

        Assert.Same(first, superseded);
        Assert.True(first.WasCancelled);
        Assert.False(first.IsAnimating);
        Assert.True(second.IsAnimating);
        Assert.Same(second, Animator.Running(target));

        second.Complete();
        Assert.True(Visible(window));
        window.Close();
    }

    [AvaloniaFact]
    public void Rapid_repeated_transitions_never_leave_content_invisible()
    {
        var (window, target) = Scene();
        MotionHandle last = Animator.FadeIn(target);
        for (var i = 0; i < 12; i++)
        {
            last = (i % 3) switch
            {
                0 => Animator.FadeOut(target),
                1 => Animator.ZoomFrom(target, AppMotion.ZoomInFrom, new RelativePoint(0.3, 0.7, RelativeUnit.Relative)),
                _ => Animator.SlideFrom(target, AppMotion.SlideX, 0),
            };
            Pixels.At(window, 50, 50); // let a frame commit between clicks
        }

        // The last click was an exit (i = 11 → slide entrance; i = 9 → exit); cancelling whatever runs shows content.
        last.Cancel();
        Assert.True(Visible(window));
        Assert.False(Animator.IsAnimating(target));
        window.Close();
    }

    [AvaloniaFact]
    public void Animations_finish_on_their_own()
    {
        var (window, target) = Scene();
        var finished = false;
        var handle = Animator.SlideFrom(target, AppMotion.SlideX, 0, AppMotion.Exit, onFinished: _ => finished = true);
        Assert.True(Pump.Until(() => !handle.IsAnimating, TimeSpan.FromSeconds(5)));
        Assert.True(finished);
        Assert.False(handle.WasCancelled);
        Assert.Equal(MotionState.Rest, handle.Current);
        Assert.True(Visible(window));
        window.Close();
    }

    [AvaloniaFact]
    public void Os_animations_off_makes_every_helper_instant()
    {
        var (window, target) = Scene();
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false;
        try
        {
            var calls = 0;
            var exit = Animator.FadeOut(target, AppMotion.Exit, _ => calls++);
            Assert.False(exit.IsAnimating);
            Assert.Equal(1, calls);
            Assert.True(Hidden(window));

            var enter = Animator.ZoomFrom(target, AppMotion.ZoomOutFrom, RelativePoint.Center);
            Assert.False(enter.IsAnimating);
            Assert.True(enter.Finished.Result);
            Assert.True(Visible(window));
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Detached_element_completes_synchronously()
    {
        var orphan = new Border();
        var calls = 0;
        var handle = Animator.FadeIn(orphan, AppMotion.Section, _ => calls++);
        Assert.False(handle.IsAnimating);
        Assert.False(handle.WasCancelled);
        Assert.Equal(1, calls);
    }

    [AvaloniaFact]
    public void Current_interpolates_between_from_and_to_while_running()
    {
        var (window, target) = Scene();
        var handle = Animator.SlideFrom(target, 64, 0, AppMotion.SegIndicator, fade: false);
        var current = handle.Current;
        Assert.InRange(current.X, 0, 64);
        Assert.Equal(1, current.Opacity);
        handle.Complete();
        Assert.Equal(MotionState.Rest, handle.Current);
        window.Close();
    }

    [AvaloniaFact]
    public void Controls_move_with_composition_flip_and_keep_their_resting_state()
    {
        var toggle = new Doba.App.Controls.ToggleSwitch();
        var progress = new ProgressLine { Width = 200, Value = 0.25 };
        var segments = new SegmentedSwitch { SelectedIndex = 0, ItemsSource = new[] { "Year", "Month", "Week", "Day" } };
        var window = new Window
        {
            Width = 400, Height = 200,
            Content = new StackPanel { Spacing = 10, Children = { toggle, progress, segments } },
        };
        window.Show();
        window.UpdateLayout();
        Pixels.At(window, 0, 0);

        toggle.IsChecked = true;
        progress.Value = 0.75;
        segments.SelectedIndex = 2;

        var knob = toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Knob");
        Assert.True(Animator.IsAnimating(knob));
        var fill = progress.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");
        Assert.True(Animator.IsAnimating(fill));
        Assert.Equal(0.75, ((ScaleTransform)fill.RenderTransform!).ScaleX, 3);
        var indicator = segments.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
        Assert.True(Animator.IsAnimating(indicator));
        Assert.Equal(2 * AppMotion.SegStep, ((TranslateTransform)indicator.RenderTransform!).X, 1);

        // A rapid second change supersedes the first and starts from where the indicator is drawn (between Year and
        // Week, i.e. within one step of the new Month position), not from its old resting place.
        var first = Animator.Running(indicator)!;
        segments.SelectedIndex = 1;
        var second = Animator.Running(indicator)!;
        Assert.True(first.WasCancelled);
        Assert.Equal(AppMotion.SegStep, ((TranslateTransform)indicator.RenderTransform!).X, 1);
        Assert.InRange(second.From.X, -AppMotion.SegStep - 0.5, AppMotion.SegStep + 0.5);
        window.Close();
    }

    // ---- Retarget / Shown / Destination ------------------------------------------------------------------------------

    private static readonly MotionState s_dimmed = new(0.55, 0, 0, 1, 1);

    [AvaloniaFact]
    public void Shown_and_destination_follow_the_last_animation()
    {
        var (window, target) = Scene();
        Assert.Equal(MotionState.Rest, Animator.Shown(target));
        Assert.Equal(MotionState.Rest, Animator.Destination(target));

        var exit = Animator.FadeOut(target);
        Assert.Equal(MotionState.Hidden, Animator.Destination(target));
        Assert.InRange(Animator.Shown(target).Opacity, 0, 1);

        exit.Complete(); // a finished exit stays hidden on the render thread
        Assert.Equal(MotionState.Hidden, Animator.Shown(target));
        Assert.Equal(MotionState.Hidden, Animator.Destination(target));
        Assert.True(Hidden(window));

        Animator.Reset(target);
        Assert.Equal(MotionState.Rest, Animator.Shown(target));
        Assert.True(Visible(window));

        Animator.SlideTo(target, 0, AppMotion.SectionExitY).Cancel(); // cancel shows the element at rest
        Assert.Equal(MotionState.Rest, Animator.Shown(target));
        window.Close();
    }

    [AvaloniaFact]
    public void Retarget_continues_from_the_end_state_of_the_last_animation()
    {
        var (window, target) = Scene();
        var dim = Animator.Retarget(target, s_dimmed, AppMotion.Fade, AppMotion.EaseOut);
        Transit.Is(dim, MotionState.Rest, s_dimmed, AppMotion.Fade, AppMotion.EaseOut);
        dim.Complete();
        Assert.Equal(s_dimmed, Animator.Shown(target));
        Assert.False(Visible(window)); // drawn at 0.55, not at rest
        Assert.False(Hidden(window));
        Assert.Equal(1.0, target.Opacity); // the UI-side opacity is never touched

        var back = Animator.Retarget(target, MotionState.Rest, AppMotion.Fade, AppMotion.EaseOut);
        Transit.Is(back, s_dimmed, MotionState.Rest, AppMotion.Fade, AppMotion.EaseOut);
        back.Complete();
        Assert.True(Visible(window));
        window.Close();
    }

    [AvaloniaFact]
    public void Retarget_continues_from_where_a_running_animation_is()
    {
        var (window, target) = Scene();
        var slide = Animator.Animate(target, MotionState.Rest, MotionState.Offset(0, 48), AppMotion.Land, AppMotion.EaseOut);
        Pixels.At(window, 0, 0); // a frame between the two
        var back = Animator.Retarget(target, MotionState.Rest, AppMotion.Snap, AppMotion.EaseOut);

        Assert.True(slide.WasCancelled); // superseded
        Assert.True(back.IsAnimating);
        Assert.InRange(back.From.Y, 0, 48);
        Assert.Equal(MotionState.Rest, back.To);
        Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.Snap), back.Duration);
        back.Complete();
        Assert.Equal(MotionState.Rest, Animator.Shown(target));
        window.Close();
    }

    [AvaloniaFact]
    public void Retarget_to_where_it_is_going_or_already_is_starts_nothing_new()
    {
        var (window, target) = Scene();
        var calls = 0;
        var still = Animator.Retarget(target, MotionState.Rest, AppMotion.Fade, AppMotion.EaseOut, onFinished: _ => calls++);
        Assert.False(still.IsAnimating); // already at rest: finished at once
        Assert.Equal(1, calls);

        var dim = Animator.Retarget(target, s_dimmed, AppMotion.Fade, AppMotion.EaseOut);
        Assert.Same(dim, Animator.Retarget(target, s_dimmed, AppMotion.Fade, AppMotion.EaseOut));
        Assert.True(dim.IsAnimating);
        dim.Complete();

        var again = Animator.Retarget(target, s_dimmed, AppMotion.Fade, AppMotion.EaseOut);
        Assert.False(again.IsAnimating);
        Assert.Equal(s_dimmed, Animator.Shown(target));
        window.Close();
    }

    [AvaloniaFact]
    public void Retarget_is_instant_with_os_animations_off()
    {
        var (window, target) = Scene();
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false;
        try
        {
            var dim = Animator.Retarget(target, s_dimmed, AppMotion.Fade, AppMotion.EaseOut);
            Assert.False(dim.IsAnimating);
            Assert.Equal(s_dimmed, Animator.Shown(target));
            var back = Animator.Retarget(target, MotionState.Rest, AppMotion.Fade, AppMotion.EaseOut);
            Assert.False(back.IsAnimating);
            Assert.Equal(s_dimmed, back.From);
            Assert.True(Visible(window));
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_recreated_visual_is_shown_at_rest()
    {
        var (window, target) = Scene();
        Animator.Retarget(target, s_dimmed, AppMotion.Fade, AppMotion.EaseOut).Complete();
        Assert.Equal(s_dimmed, Animator.Shown(target));

        window.Content = null; // detached: its composition visual goes; attached again it gets a new one at rest
        window.Content = target;
        window.UpdateLayout();
        Assert.Equal(MotionState.Rest, Animator.Shown(target));
        Assert.True(Visible(window));
        window.Close();
    }
}
