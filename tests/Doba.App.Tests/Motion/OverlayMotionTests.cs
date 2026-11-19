using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Dialogs;
using Doba.App.Dialogs.Search;
using Doba.App.Motion;
using Doba.App.Shell;
using Doba.App.Tests.DesignSystem;
using Doba.Core.Navigation;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.MotionChecks;

/// <summary>
/// Motion spec "Оверлеї": the shade and the task window open (220 / 320 ms ease-out) and close (150 ms ease-in) on the
/// render thread; focus goes to the title only after the entrance; the save is written and focus returns only after the
/// exit; done is called once under rapid open/close/open and early completion; the content behind is blurred while
/// open; popovers enter in 180 ms from their top-left corner and close at once; with OS animations off it is all instant.
/// </summary>
public class OverlayMotionTests
{
    private static readonly MotionState PanelFrom = new(0, 0, AppMotion.DialogY, AppMotion.DialogScale, AppMotion.DialogScale);
    private static readonly MotionState PanelTo = new(0, 0, AppMotion.DialogExitY, AppMotion.DialogExitScale, AppMotion.DialogExitScale);

    private sealed class CountingOverlay : OverlayViewModel
    {
        public int Closed { get; private set; }

        public override void OnClosed() => Closed++;

        public void Close() => RequestClose();
    }

    [AvaloniaFact]
    public void Default_overlay_transitions_are_the_animated_ones()
    {
        Assert.IsType<AnimatedOverlayTransitions>(OverlayHost.OverlayTransitions);
        Assert.Equal(PanelFrom, AnimatedOverlayTransitions.PanelEnterFrom);
        Assert.Equal(PanelTo, AnimatedOverlayTransitions.PanelExitTo);
    }

    [AvaloniaFact]
    public void Dialog_enters_shade_220_and_window_320_ease_out_then_focuses_the_title()
    {
        using var shell = new AnimatedShell(AppView.Week, blur: true);
        var host = shell.Overlays;
        shell.Vm.NewTask();
        Assert.Null(host.Blurred);                                               // the blur starts with the shade, at 0
        shell.Pump();

        Transit.Is(Animator.Running(host.Shade), MotionState.Hidden, MotionState.Rest, AppMotion.Shade, AppMotion.EaseOut);
        var entrance = Animator.Running(host.OverlayPanel);
        Transit.Is(entrance, PanelFrom, MotionState.Rest, AppMotion.DialogIn, AppMotion.EaseOut);

        // Scaled around the laid-out panel's centre (the panel was laid out before the entrance started).
        var visual = ElementComposition.GetElementVisual(host.OverlayPanel)!;
        Assert.True(host.OverlayPanel.Bounds.Height > 100);
        Assert.Equal(host.OverlayPanel.Bounds.Width / 2, visual.CenterPoint.X, 3);
        Assert.Equal(host.OverlayPanel.Bounds.Height / 2, visual.CenterPoint.Y, 3);

        // Nothing is re-laid out: the panel sits at its resting place, only the render-thread offsets move.
        Assert.Equal(150, host.OverlayPanel.TranslatePoint(default, shell.Window)!.Value.Y);
        Assert.Equal(1, host.OverlayPanel.Opacity);
        Assert.Null(host.OverlayPanel.RenderTransform);

        var view = Assert.IsType<TaskDialogView>(host.OverlayView);
        Assert.False(view.TitleBox.IsFocused);                                   // not during the entrance
        Assert.Same(host.OverlayPanel, shell.Focused);                           // parked: keys never reach the page

        Animator.Running(host.Shade)!.Complete();
        Assert.False(view.TitleBox.IsFocused);                                   // the shade alone does not end it
        entrance!.Complete();
        shell.Pump();
        Assert.True(view.TitleBox.IsFocused);                                    // then focus goes to the title
        Assert.Equal(host.FullBlurRadius, host.BlurRadius);
    }

    [AvaloniaFact]
    public void Dialog_exits_in_150ms_ease_in_and_saves_only_when_the_exit_has_ended()
    {
        using var shell = new AnimatedShell(AppView.Week);
        var host = shell.Overlays;
        shell.Vm.NewTask();
        shell.Settle();
        var dialog = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        var count = shell.App.Store.All.Count;
        shell.Window.KeyTextInput("Buy oat milk");
        shell.Key(Key.Enter);

        Transit.Is(Animator.Running(host.Shade), MotionState.Rest, MotionState.Hidden, AppMotion.DialogExit, AppMotion.EaseIn);
        var exit = Animator.Running(host.OverlayPanel);
        Transit.Is(exit, MotionState.Rest, PanelTo, AppMotion.DialogExit, AppMotion.EaseIn);
        Assert.Same(dialog, shell.Vm.Overlay);                                   // still on screen during the exit
        Assert.Equal(count, shell.App.Store.All.Count);                          // nothing written yet

        exit!.Complete();
        shell.Pump();
        Assert.Null(shell.Vm.Overlay);
        Assert.False(host.IsVisible);
        Assert.Equal(count + 1, shell.App.Store.All.Count);
        Assert.Contains(shell.App.Store.All, t => t.Title == "Buy oat milk");
    }

    [AvaloniaFact]
    public void Esc_during_the_entrance_exits_from_where_the_window_is_and_never_focuses_the_title()
    {
        using var shell = new AnimatedShell(AppView.Week);
        var host = shell.Overlays;
        var before = shell.Window.Header.NewTaskButton;
        Assert.True(before.Focus());
        shell.Vm.NewTask();
        shell.Pump();
        var entrance = Animator.Running(host.OverlayPanel)!;
        var view = Assert.IsType<TaskDialogView>(host.OverlayView);

        shell.Key(Key.Escape);
        Assert.True(entrance.WasCancelled);                                       // superseded by the exit
        var exit = Animator.Running(host.OverlayPanel)!;
        Assert.Equal(PanelTo, exit.To);
        Assert.True(exit.From.Opacity < 1, $"exit starts where the entrance is ({exit.From})");
        Assert.True(exit.From.Y > 0);
        shell.Pump();
        Assert.False(view.TitleBox.IsFocused);
        Assert.False(before.IsFocused);                                          // not back before the exit ends

        exit.Complete();
        shell.Pump();
        Assert.Null(shell.Vm.Overlay);
        Assert.True(before.IsFocused);
    }

    [AvaloniaFact]
    public void Rapid_open_close_open_calls_each_callback_once_and_ends_visible()
    {
        var shade = new Border { Background = Brushes.Transparent };
        var panel = new Border
        {
            Width = 100, Height = 100,
            Background = new SolidColorBrush(Pixels.Resource("Color.Text.Primary")),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        using var stage = new Stage(new Panel { Children = { shade, panel } }, width: 200, height: 200);
        var t = new AnimatedOverlayTransitions();
        var opened = 0;
        var done = new int[3];

        t.Open(shade, panel, () => opened++);
        t.Close(shade, panel, () => done[0]++);
        Assert.Equal(1, opened);                                                  // the superseded entrance reports once
        Assert.Equal(0, done[0]);
        t.Open(shade, panel, () => opened++);                                    // reopened during the exit
        Assert.Equal(1, done[0]);                                                 // the superseded exit still finishes once
        t.Close(shade, panel, () => done[1]++);
        t.Close(shade, panel, () => done[2]++);
        Assert.Equal([1, 1, 0], done);
        Assert.Equal(2, opened);

        Animator.Running(panel)!.Cancel();                                       // cancelled: done once, element shown
        Assert.Equal([1, 1, 1], done);
        Animator.Running(shade)?.Cancel();
        Assert.True(Pixels.Near(Pixels.Resource("Color.Text.Primary"), Pixels.At(stage.Window, 50, 50), 6));

        t.Open(shade, panel, () => opened++);
        Animator.Running(panel)!.Complete();
        Animator.Running(shade)!.Complete();
        Assert.Equal(3, opened);
        Assert.Equal([1, 1, 1], done);
        Assert.True(Pixels.Near(Pixels.Resource("Color.Text.Primary"), Pixels.At(stage.Window, 50, 50), 6));
    }

    [AvaloniaFact]
    public void Early_completion_during_the_exit_closes_once_and_the_late_exit_changes_nothing()
    {
        using var shell = new AnimatedShell(AppView.Week);
        var host = shell.Overlays;
        var overlay = new CountingOverlay();
        shell.Vm.OpenOverlay(overlay);
        shell.Settle();
        overlay.Close();
        shell.Pump();
        var exit = Animator.Running(host.OverlayPanel)!;
        Assert.True(shell.Vm.IsOverlayClosing);

        shell.Vm.CompletePendingOverlayClose();                                  // e.g. the window closing mid-exit
        Assert.Null(shell.Vm.Overlay);
        Assert.Equal(1, overlay.Closed);
        Assert.False(host.IsVisible);
        exit.Complete();                                                          // the exit ending later changes nothing
        shell.Pump();
        Assert.Equal(1, overlay.Closed);

        // The next overlay opens normally, on top of whatever the old exit left.
        var next = new CountingOverlay();
        shell.Vm.OpenOverlay(next);
        shell.Pump();
        Transit.Is(Animator.Running(host.OverlayPanel), PanelFrom, MotionState.Rest, AppMotion.DialogIn, AppMotion.EaseOut);
        shell.Settle();
        Assert.True(host.IsVisible);
        Assert.Same(host.OverlayPanel, shell.Focused);
        Assert.Null(Animator.Running(host.OverlayPanel));
        next.Close();
        shell.Settle();
        Assert.Equal(1, next.Closed);
    }

    [AvaloniaFact]
    public void Superseding_overlay_during_an_exit_does_not_close_the_new_one()
    {
        using var shell = new AnimatedShell(AppView.Week);
        var first = new CountingOverlay();
        shell.Vm.OpenOverlay(first);
        shell.Settle();
        first.Close();
        shell.Pump();

        var second = new CountingOverlay();
        shell.Vm.OpenOverlay(second);                                            // replaces the leaving one at once
        shell.Settle();
        Assert.Equal(1, first.Closed);
        Assert.Same(second, shell.Vm.Overlay);
        Assert.Equal(0, second.Closed);
        Assert.True(shell.Overlays.IsVisible);
    }

    [AvaloniaFact]
    public void Focus_returns_to_a_task_title_after_the_exit_animation()
    {
        using var shell = new AnimatedShell(AppView.Week);
        var title = shell.OnScreen<TaskCard>().First().GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_TitleButton");
        bool? focusedWhenOpened = null;
        shell.Vm.PropertyChanging += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Overlay) && shell.Vm.Overlay is null)
            {
                focusedWhenOpened ??= title.IsFocused;
            }
        };
        shell.Click(title);
        Assert.True(focusedWhenOpened);                                          // the click focused it, then opened the dialog
        Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        shell.Settle();
        var view = Assert.IsType<TaskDialogView>(shell.Overlays.OverlayView);
        Assert.True(view.TitleBox.IsFocused);

        shell.Key(Key.Escape);
        Assert.NotNull(shell.Vm.Overlay);                                         // exit running
        Assert.False(title.IsFocused);
        shell.Settle();
        Assert.Null(shell.Vm.Overlay);
        Assert.True(title.IsFocused);
    }

    [AvaloniaFact]
    public void Focus_returns_to_New_task_after_the_exit_animation()
    {
        using var shell = new AnimatedShell(AppView.Week);
        var button = shell.Window.Header.NewTaskButton;
        shell.Click(button);
        Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        shell.Settle();

        shell.Key(Key.Escape);
        Assert.False(button.IsFocused);
        Animator.Running(shell.Overlays.OverlayPanel)!.Complete();
        shell.Pump();
        Assert.Null(shell.Vm.Overlay);
        Assert.True(button.IsFocused);
    }

    [AvaloniaFact]
    public void Content_behind_an_open_overlay_is_blurred_like_the_css_backdrop_filter()
    {
        using var shell = new AnimatedShell(AppView.Week, blur: true);
        var host = shell.Overlays;
        var behind = shell.Window.GetVisualDescendants().OfType<Grid>().First(g => ReferenceEquals(g.Parent, host.Parent));
        Assert.Null(behind.Effect);

        shell.Vm.NewTask();
        shell.Settle();
        Assert.Same(behind, host.Blurred);
        var blur = Assert.IsAssignableFrom<IBlurEffect>(behind.Effect);
        Assert.Equal(OverlayHost.CssBlurToRadius(OverlayHost.BackdropBlurCss), blur.Radius, 3);
        Assert.Equal(OverlayHost.BackdropBlurCss, OverlayHost.RadiusToCssBlur(blur.Radius), 3);
        Assert.Null(shell.Window.MainPanel.Effect);                              // one effect, on the whole content

        shell.Key(Key.Escape);                                                   // fades out with the shade (next test)
        shell.Settle();
        Assert.Null(shell.Vm.Overlay);
        Assert.Null(behind.Effect);
        Assert.Null(host.Blurred);
    }

    [AvaloniaFact]
    public void Blur_follows_the_shade_frame_by_frame_and_rests_only_at_none_or_full()
    {
        // While the shade animates, every frame sets σ = the shade's drawn opacity × the full σ. A slow linear exit (held
        // until the test releases it) makes the per-frame value measurable.
        var slowExit = new SlowShadeExit();
        var previous = OverlayHost.OverlayTransitions;
        OverlayHost.OverlayTransitions = slowExit;
        try
        {
            FollowsTheSlowExit(slowExit);
        }
        finally
        {
            OverlayHost.OverlayTransitions = previous;
        }
    }

    private sealed class SlowShadeExit : IOverlayTransitions
    {
        private readonly AnimatedOverlayTransitions _animated = new();

        public MotionHandle? Shade { get; private set; }

        public Action? Done { get; private set; }

        public void Open(Control shade, Control panel) => _animated.Open(shade, panel);

        public void Open(Control shade, Control panel, Action opened) => _animated.Open(shade, panel, opened);

        public void Close(Control shade, Control panel, Action done)
        {
            Shade = Animator.Animate(shade, MotionState.Rest, MotionState.Hidden, 4000, new LinearEasing());
            Done = done;
        }
    }

    private static void FollowsTheSlowExit(SlowShadeExit exit)
    {
        using var shell = new AnimatedShell(AppView.Week, blur: true);
        var host = shell.Overlays;
        shell.Vm.NewTask();
        shell.Settle();
        var full = host.FullBlurRadius;
        Assert.Equal(full, host.BlurRadius);

        shell.Key(Key.Escape);
        var slow = exit.Shade!;
        Thread.Sleep(200);
        shell.Pump();
        var expected = OverlayHost.CssBlurToRadius(OverlayHost.RadiusToCssBlur(full) * slow.Current.Opacity);
        Assert.InRange(host.BlurRadius, 0.01, full - 0.01);
        Assert.Equal(expected, host.BlurRadius, 0.5);
        var before = host.BlurRadius;
        Thread.Sleep(200);
        shell.Pump();
        Assert.True(host.BlurRadius < before, $"the blur keeps following the shade ({host.BlurRadius} after {before})");

        slow.Cancel();                                                           // the shade at rest: a closing overlay rests at none
        shell.Pump();
        Assert.Equal(0, host.BlurRadius);
        Assert.Null(host.Blurred);
        Assert.NotNull(shell.Vm.Overlay);
        exit.Done!();
        shell.Settle();
        Assert.Null(shell.Vm.Overlay);
        Assert.Null(host.Blurred);
    }

    [AvaloniaFact]
    public void A_search_hit_replacing_the_palette_keeps_the_shade_and_the_blur_and_only_the_window_enters()
    {
        using var shell = new AnimatedShell(AppView.Week, blur: true);
        var host = shell.Overlays;
        var before = shell.Window.Header.NewTaskButton;
        Assert.True(before.Focus());
        shell.Vm.OpenSearch();
        shell.Settle();
        var blur = host.Blurred?.Effect;
        Assert.NotNull(blur);
        Assert.Equal(host.FullBlurRadius, host.BlurRadius);

        shell.Vm.EditTask(shell.App.Store.All[0].Id);                            // what a search hit does
        Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        Assert.Null(Animator.Running(host.Shade));                               // the shade stays up: no flash
        Transit.Is(Animator.Running(host.OverlayPanel), PanelFrom, MotionState.Rest, AppMotion.DialogIn, AppMotion.EaseOut);
        Assert.True(host.IsVisible);
        Assert.Same(blur, host.Blurred?.Effect);                                 // the blur stays, unchanged
        shell.Pump();
        Assert.Null(Animator.Running(host.Shade));
        Assert.Same(blur, host.Blurred?.Effect);
        Assert.False(before.IsFocused);

        shell.Settle();
        Assert.True(Assert.IsType<TaskDialogView>(host.OverlayView).TitleBox.IsFocused);
        shell.Key(Key.Escape);
        shell.Settle();
        Assert.Null(shell.Vm.Overlay);
        Assert.True(before.IsFocused);                                           // back where it was before the search
        Assert.Null(host.Blurred);
    }

    [AvaloniaFact]
    public void A_replacement_during_the_entrance_lets_the_shade_finish_its_entrance()
    {
        using var shell = new AnimatedShell(AppView.Week, blur: true);
        var host = shell.Overlays;
        shell.Vm.OpenSearch();
        shell.Pump();
        var shade = Animator.Running(host.Shade);
        Assert.NotNull(shade);
        var first = Animator.Running(host.OverlayPanel)!;

        shell.Vm.EditTask(shell.App.Store.All[0].Id);
        Assert.Same(shade, Animator.Running(host.Shade));                       // not restarted from 0
        Assert.True(first.WasCancelled);
        Transit.Is(Animator.Running(host.OverlayPanel), PanelFrom, MotionState.Rest, AppMotion.DialogIn, AppMotion.EaseOut);
        shell.Settle();
        Assert.True(Assert.IsType<TaskDialogView>(host.OverlayView).TitleBox.IsFocused);
        Assert.Equal(host.FullBlurRadius, host.BlurRadius);
    }

    [AvaloniaFact]
    public void An_overlay_removed_without_an_exit_and_not_replaced_puts_blur_and_focus_back_before_the_next_frame()
    {
        using var shell = new AnimatedShell(AppView.Week, blur: true);
        var host = shell.Overlays;
        var before = shell.Window.Header.NewTaskButton;
        Assert.True(before.Focus());
        shell.Vm.OpenSearch();
        shell.Settle();

        shell.Vm.CloseOverlay();                                                 // removed directly, nothing follows
        Assert.False(host.IsVisible);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();                        // the jobs that run before any frame
        Assert.Null(host.Blurred);
        Assert.True(before.IsFocused);
    }

    [AvaloniaFact]
    public void Text_typed_right_after_N_reaches_the_title_once_the_entrance_has_ended()
    {
        using var shell = new AnimatedShell(AppView.Week);
        shell.Key(Key.N);
        var view = Assert.IsType<TaskDialogView>(shell.Overlays.OverlayView);
        shell.Window.KeyTextInput("Buy");
        shell.Pump();
        Assert.False(view.TitleBox.IsFocused);                                   // still entering: focus waits
        Assert.True(string.IsNullOrEmpty(view.TitleBox.Text));
        shell.Window.KeyTextInput(" milk");

        shell.Settle();
        Assert.True(view.TitleBox.IsFocused);
        Assert.Equal("Buy milk", view.TitleBox.Text);
        shell.Window.KeyTextInput("!");                                          // and typing goes on normally
        Assert.Equal("Buy milk!", view.TitleBox.Text);
    }

    [AvaloniaFact]
    public void Backspace_during_the_entrance_takes_back_typed_text_and_Enter_does_nothing()
    {
        using var shell = new AnimatedShell(AppView.Week);
        var count = shell.App.Store.All.Count;
        shell.Key(Key.N);
        var view = Assert.IsType<TaskDialogView>(shell.Overlays.OverlayView);
        shell.Window.KeyTextInput("Buyx");
        shell.Key(Key.Back);
        shell.Key(Key.Enter);                                                    // still entering: not a save
        Assert.False(view.TitleBox.IsFocused);

        shell.Settle();
        Assert.True(view.TitleBox.IsFocused);
        Assert.Equal("Buy", view.TitleBox.Text);
        Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        Assert.Equal(count, shell.App.Store.All.Count);
    }

    [AvaloniaFact]
    public void Text_typed_right_after_Ctrl_K_reaches_the_search_query()
    {
        using var shell = new AnimatedShell(AppView.Week);
        shell.Key(Key.K, RawInputModifiers.Control);
        var palette = Assert.IsType<SearchPaletteViewModel>(shell.Vm.Overlay);
        shell.Window.KeyTextInput("milk");
        shell.Settle();
        Assert.Equal("milk", palette.Query);
    }

    [AvaloniaFact]
    public void Text_typed_during_an_entrance_that_is_then_closed_goes_nowhere()
    {
        using var shell = new AnimatedShell(AppView.Week);
        shell.Key(Key.N);
        shell.Window.KeyTextInput("Buy");
        shell.Key(Key.Escape);
        shell.Settle();
        Assert.Null(shell.Vm.Overlay);

        shell.Key(Key.N);
        shell.Settle();
        var view = Assert.IsType<TaskDialogView>(shell.Overlays.OverlayView);
        Assert.True(string.IsNullOrEmpty(view.TitleBox.Text));
    }

    [AvaloniaFact]
    public void Popover_enters_in_180ms_from_its_top_left_corner_and_closes_at_once()
    {
        using var shell = new AnimatedShell(AppView.Week);
        shell.Vm.NewTask();
        shell.Settle();
        var view = Assert.IsType<TaskDialogView>(shell.Overlays.OverlayView);
        var anchor = view.Anchors[0];
        shell.Click(anchor.Children[0]);
        Assert.True(anchor.IsShowing);
        var popover = anchor.Popover!;
        Transit.Is(Animator.Running(popover), new MotionState(0, 0, AppMotion.PopY, AppMotion.PopScale, AppMotion.PopScale),
            MotionState.Rest, AppMotion.Pop, AppMotion.EaseOut);
        var visual = ElementComposition.GetElementVisual(popover)!;
        Assert.Equal(0, visual.CenterPoint.X);
        Assert.Equal(0, visual.CenterPoint.Y);

        shell.Key(Key.Escape);                                                    // closes the popover, not the dialog
        Assert.False(anchor.IsShowing);
        Assert.Null(Animator.Running(popover));
        Assert.NotNull(shell.Vm.Overlay);
        Assert.Null(Animator.Running(shell.Overlays.OverlayPanel));              // the window itself did not move
    }

    [AvaloniaFact]
    public void Schedule_plus_N_list_pops_in_from_its_corner_and_closes_at_once()
    {
        using var shell = new AnimatedShell(AppView.Day, new DateOnly(2026, 11, 16));
        var schedule = shell.OnScreen<ScheduleTimeline>().Single();
        var more = schedule.OverflowList.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_More");
        shell.Click(more);
        var list = schedule.ListPopover;
        Assert.True(list.IsVisible);
        // sbPop: opacity 0 → 1, translateY −4 → 0, scale 0.98 → 1, 180 ms ease-out, from the top right (under its "+N").
        Transit.Is(Animator.Running(list), new MotionState(0, 0, AppMotion.PopY, AppMotion.PopScale, AppMotion.PopScale),
            MotionState.Rest, AppMotion.Pop, AppMotion.EaseOut);
        var visual = ElementComposition.GetElementVisual(list)!;
        Assert.Equal(232, visual.CenterPoint.X);
        Assert.Equal(0, visual.CenterPoint.Y);

        shell.Key(Key.Escape);                                                    // instant, like the prototype's unmount
        Assert.False(list.IsVisible);
        Assert.Null(Animator.Running(list));
    }

    [AvaloniaFact]
    public void With_animations_off_the_schedule_list_opens_at_once()
    {
        using var shell = new AnimatedShell(AppView.Day, new DateOnly(2026, 11, 16), animations: false);
        var schedule = shell.OnScreen<ScheduleTimeline>().Single();
        shell.Click(schedule.OverflowList.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_More"));
        Assert.True(schedule.ListPopover.IsVisible);
        Assert.Null(Animator.Running(schedule.ListPopover));
        Assert.Equal(1, ElementComposition.GetElementVisual(schedule.ListPopover)!.Opacity);
    }

    [AvaloniaFact]
    public void With_animations_off_the_overlay_opens_focuses_and_closes_at_once()
    {
        using var shell = new AnimatedShell(AppView.Week, animations: false, blur: true);
        var host = shell.Overlays;
        shell.Vm.NewTask();
        Assert.Null(Animator.Running(host.Shade));
        Assert.Null(Animator.Running(host.OverlayPanel));
        shell.Pump();
        var view = Assert.IsType<TaskDialogView>(host.OverlayView);
        Assert.True(view.TitleBox.IsFocused);
        Assert.NotNull(host.Blurred);

        shell.Window.KeyTextInput("Call the bank");
        var count = shell.App.Store.All.Count;
        shell.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Null(shell.Vm.Overlay);                                            // closed and saved synchronously
        Assert.Equal(count + 1, shell.App.Store.All.Count);
        Assert.Null(Animator.Running(host.OverlayPanel));
        Assert.Null(host.Blurred);
        shell.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
    }
}
