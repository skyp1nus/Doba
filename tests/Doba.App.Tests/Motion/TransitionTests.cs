using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Motion;
using Doba.App.Platform;
using Doba.App.Screens.Week;
using Doba.App.Shell;
using Doba.App.Tests.DesignSystem;
using Doba.App.Tests.Support;
using Doba.App.Theme;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Navigation;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Motion;

/// <summary>
/// The navigation transitions of the motion spec ("Навігаційні переходи") as played by <see cref="ContentTransitions"/>
/// in the real shell, asserted on the <see cref="Animator"/> handles (from/to, duration, easing, origin) and on rendered
/// frames. Animations are switched on after the shell opens; nothing waits on the wall clock: handles are completed
/// explicitly and frames are forced.
/// </summary>
public sealed class TransitionTests
{
    private static readonly DateOnly Today = DemoData.Today;

    /// <summary>
    /// Largest per-channel difference accepted between a settled frame and the same frame after every target is reset.
    /// Re-rendering re-blends anti-aliased edges by a few levels and XAML hover / colour transitions keep running on the
    /// wall clock (up to ~30 levels on the brightest button edge); content left faded, offset or scaled differs by far
    /// more (text on the panel ≈ 200 levels).
    /// </summary>
    private const int RestTolerance = 48;

    // ---- One handle per kind -----------------------------------------------------------------------------------------

    public static TheoryData<TransitionKind> EnterKinds() =>
    [
        TransitionKind.ZoomIn, TransitionKind.ZoomOut, TransitionKind.Next, TransitionKind.Prev, TransitionKind.SettingsIn,
        TransitionKind.SettingsOut,
    ];

    [AvaloniaTheory]
    [MemberData(nameof(EnterKinds))]
    public void Each_kind_swaps_first_then_enters_with_the_spec_values(TransitionKind kind)
    {
        using var shell = Live(AppView.Week);
        var t = shell.Window.Targets!;
        var swaps = 0;

        ShellComposition.Transitions.Run(kind, null, () => swaps++, t);

        Assert.Equal(1, swaps); // instant swap, before the entrance
        var content = Assert.IsType<MotionHandle>(Animator.Running(t.Content));
        Assert.Same(AppMotion.EaseOut, content.Easing);
        Assert.Equal(MotionState.Rest, content.To);
        switch (kind)
        {
            case TransitionKind.ZoomIn:
                AssertZoom(content, AppMotion.ZoomInFrom, ContentTransitions.DefaultZoomOrigin);
                break;
            case TransitionKind.ZoomOut:
                AssertZoom(content, AppMotion.ZoomOutFrom, ContentTransitions.DefaultZoomOrigin);
                break;
            case TransitionKind.Next:
                Assert.Equal(MotionState.Offset(AppMotion.SlideX, 0, 0), content.From);
                Assert.Equal(AppMotion.Ms(AppMotion.Slide), content.Duration);
                break;
            case TransitionKind.Prev:
                Assert.Equal(MotionState.Offset(-AppMotion.SlideX, 0, 0), content.From);
                Assert.Equal(AppMotion.Ms(AppMotion.Slide), content.Duration);
                break;
            case TransitionKind.SettingsIn:
                AssertZoom(content, AppMotion.ZoomInFrom, ContentTransitions.SettingsZoomOrigin);
                AssertSidebarSlide(t.SettingsSidebar!, AppMotion.SettingsSidebarX);
                Assert.Null(Animator.Running(t.AppSidebar!));
                break;
            case TransitionKind.SettingsOut:
                AssertZoom(content, AppMotion.ZoomOutFrom, ContentTransitions.SettingsZoomOrigin);
                AssertSidebarSlide(t.AppSidebar!, -AppMotion.SettingsSidebarX);
                Assert.Null(Animator.Running(t.SettingsSidebar!));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        AssertTitleEntrance(t);
        Finish(shell);
        AssertAtRest(shell);
        Assert.Equal(1, swaps);
    }

    [AvaloniaFact]
    public void None_just_swaps()
    {
        using var shell = Live(AppView.Week);
        var t = shell.Window.Targets!;
        var swaps = 0;
        ShellComposition.Transitions.Run(TransitionKind.None, null, () => swaps++, t);
        Assert.Equal(1, swaps);
        AssertNothingRuns(t);
    }

    [AvaloniaFact]
    public void Zoom_in_starts_from_the_click_point_and_zoom_out_from_the_default_origin()
    {
        // Motion spec checklist 2: "zoom in from the click point, zoom out from the centre".
        using var held = HoldTimers(); // the headless click pumps the dispatcher
        using var shell = Live(AppView.Week);
        var t = shell.Window.Targets!;
        var week = shell.Window.ScreenHost.VisibleView!;
        var header = week.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("day-header")).ElementAt(2);
        var expected = ZoomOriginOf(week, header);

        ClickWithoutSettling(shell, header);

        Assert.Equal(AppView.Day, shell.Vm.Nav.View);
        var zoomIn = Assert.IsType<MotionHandle>(Animator.Running(t.Content));
        AssertZoom(zoomIn, AppMotion.ZoomInFrom, new RelativePoint(expected.X, expected.Y, RelativeUnit.Relative));
        Assert.InRange(expected.X, 0.5, 0.75); // third of four columns
        Assert.InRange(expected.Y, 0, 0.15);   // the header row at the top
        AssertTitleEntrance(t);

        // The same origin passes straight through IAppCommands.ZoomTo.
        Finish(shell);
        shell.Vm.SetZoomLevel(AppView.Week);
        Finish(shell);
        var origin = new Point(0.2, 0.8);
        shell.Vm.ZoomTo(AppView.Day, Today, origin);
        AssertZoom(Animator.Running(t.Content)!, AppMotion.ZoomInFrom, new RelativePoint(0.2, 0.8, RelativeUnit.Relative));
        Finish(shell);

        // Header switch Day → Year: zoom out around the default 50 % 40 %.
        shell.Vm.SetZoomLevel(AppView.Year);
        AssertZoom(Animator.Running(t.Content)!, AppMotion.ZoomOutFrom, ContentTransitions.DefaultZoomOrigin);
        Assert.Equal(new RelativePoint(0.5, 0.4, RelativeUnit.Relative), ContentTransitions.DefaultZoomOrigin);
        AssertTitleEntrance(t);
        Finish(shell);
        AssertAtRest(shell);
    }

    [AvaloniaFact]
    public void Settings_zoom_around_50_30_and_slide_the_sidebar_that_appears()
    {
        using var shell = Live(AppView.Week);
        var t = shell.Window.Targets!;
        Assert.Equal(new RelativePoint(0.5, 0.3, RelativeUnit.Relative), ContentTransitions.SettingsZoomOrigin);

        shell.Vm.OpenSettings();
        Assert.True(t.SettingsSidebar!.IsVisible);
        AssertZoom(Animator.Running(t.Content)!, AppMotion.ZoomInFrom, ContentTransitions.SettingsZoomOrigin);
        AssertSidebarSlide(t.SettingsSidebar, AppMotion.SettingsSidebarX);
        AssertTitleEntrance(t);
        Finish(shell);

        shell.Vm.SetSettingsTab(SettingsTab.Shortcuts); // tabs slide like periods
        Assert.Equal(MotionState.Offset(AppMotion.SlideX, 0, 0), Animator.Running(t.Content)!.From);
        Finish(shell);

        shell.Vm.BackFromSettings();
        Assert.True(t.AppSidebar!.IsVisible);
        AssertZoom(Animator.Running(t.Content)!, AppMotion.ZoomOutFrom, ContentTransitions.SettingsZoomOrigin);
        AssertSidebarSlide(t.AppSidebar, -AppMotion.SettingsSidebarX);
        AssertTitleEntrance(t);
        Finish(shell);
        AssertAtRest(shell);
    }

    // ---- Sections ------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Sections_exit_first_swap_after_the_exit_and_enter_from_below_going_down()
    {
        // Motion spec checklist 3: Inbox → Today → Calendar comes from below, back from above; the sidebar highlight
        // slides with it.
        using var held = HoldTimers();
        using var shell = Live(AppView.Inbox);
        var t = shell.Window.Targets!;
        var highlight = shell.Window.AppSidebar.GetVisualDescendants().OfType<SidebarHighlight>().Single();
        (AppView, int)[] steps = [(AppView.Today, +1), (AppView.Week, +1), (AppView.Today, -1), (AppView.Inbox, -1)];
        foreach (var (target, dir) in steps)
        {
            var before = shell.Vm.Current;
            var highlightBefore = highlight.Index;
            if (target == AppView.Week)
            {
                shell.Vm.GoCalendar();
            }
            else
            {
                shell.Vm.GoSection(target);
            }

            // Exit: the old screen is still shown and leaves opposite to where the new one comes from.
            Assert.Equal(target, shell.Vm.Nav.View);
            Assert.Same(before, shell.Vm.Current);
            Assert.True(((ContentTransitions)ShellComposition.Transitions).IsSwapPending(t));
            var exit = Assert.IsType<MotionHandle>(Animator.Running(t.Content));
            Assert.Equal(MotionState.Rest, exit.From);
            Assert.Equal(MotionState.Offset(0, -AppMotion.SectionExitY * dir, 0), exit.To);
            Assert.Equal(AppMotion.Ms(AppMotion.Exit), exit.Duration);
            Assert.Same(AppMotion.EaseIn, exit.Easing);
            Assert.Null(Animator.Running(t.Title)); // the title changes with the content, after the exit
            // So does the sidebar highlight (prototype: `view` changes in `go`, after the exit): it has not moved yet.
            Assert.Equal(highlightBefore, highlight.Index);
            Assert.Null(Animator.Running(highlight));

            // The exit ends: swap, the exit is replaced (not left holding opacity 0), then the entrance.
            exit.Complete();

            // The sidebar highlight slides (380 ms ease-out, one row per section step) together with the entrance.
            var slide = Assert.IsType<MotionHandle>(Animator.Running(highlight));
            Assert.Equal(AppMotion.Ms(AppMotion.Indicator), slide.Duration);
            Assert.Same(AppMotion.EaseOut, slide.Easing);
            Assert.Equal(dir * AppMotion.NavStep, -slide.From.Y, 3);

            Assert.NotSame(before, shell.Vm.Current);
            Assert.False(((ContentTransitions)ShellComposition.Transitions).IsSwapPending(t));
            var enter = Assert.IsType<MotionHandle>(Animator.Running(t.Content));
            Assert.NotSame(exit, enter);
            Assert.False(exit.WasCancelled);
            Assert.Equal(MotionState.Offset(0, AppMotion.SectionEnterY * dir, 0), enter.From);
            Assert.Equal(MotionState.Rest, enter.To);
            Assert.Equal(AppMotion.Ms(AppMotion.Section), enter.Duration);
            Assert.Same(AppMotion.EaseOut, enter.Easing);
            AssertTitleEntrance(t);
            Finish(shell);
        }

        AssertAtRest(shell);
    }

    [AvaloniaFact]
    public void A_click_during_a_section_exit_swaps_at_once_and_starts_the_new_step()
    {
        using var shell = Live(AppView.Week);
        var t = shell.Window.Targets!;
        var swaps = Count(shell);

        shell.Vm.GoSection(AppView.Today);
        var exit = Animator.Running(t.Content)!;
        Assert.Equal(0, swaps.Swaps);

        shell.Vm.GoSection(AppView.Inbox); // before the exit has finished
        Assert.True(exit.WasCancelled);
        Assert.Equal(1, swaps.Swaps); // the pending Today swap happened now, in order
        var second = Animator.Running(t.Content)!;
        Assert.Equal(MotionState.Offset(0, AppMotion.SectionExitY, 0), second.To); // going up: exit downwards

        shell.Vm.ZoomTo(AppView.Day, Today, new Point(0.5, 0.5)); // zoom during the second exit
        Assert.Equal(3, swaps.Swaps);
        Finish(shell);
        Assert.Equal(swaps.Runs, swaps.Swaps);
        Assert.Equal(AppView.Day, shell.Vm.Nav.View);
        AssertAtRest(shell);
    }

    // ---- Rapid input, launch, OS animations off, cached views -----------------------------------------------------------

    [AvaloniaFact]
    public void Thirty_rapid_mixed_navigations_leave_the_content_fully_visible()
    {
        // Motion spec checklist 4: rapid repeated clicks do not break state or leave invisible content.
        using var shell = Live(AppView.Week);
        var counter = Count(shell);
        var vm = shell.Vm;
        Action[] clicks =
        [
            () => vm.GoSection(AppView.Inbox),
            () => vm.GoSection(AppView.Today),
            vm.GoCalendar,
            () => vm.Shift(1),
            () => vm.ZoomTo(AppView.Day, Today.AddDays(1), new Point(0.3, 0.6)),
            () => vm.SetZoomLevel(AppView.Year),
            () => vm.Shift(-1),
            vm.GoToday,
            vm.OpenSettings,
            () => vm.SetSettingsTab(SettingsTab.Data),
            vm.BackFromSettings,
            () => vm.SetZoomLevel(AppView.Month),
        ];
        for (var i = 0; i < 30; i++)
        {
            clicks[(i * 7) % clicks.Length]();
            Frame(shell); // a frame between clicks, nothing completed
        }

        Assert.True(counter.Runs >= 15, $"only {counter.Runs} of 30 clicks navigated");
        Finish(shell);
        Assert.Equal(counter.Runs, counter.Swaps);
        Assert.Same(vm.ScreenFor(vm.Nav.View), vm.Current);
        Assert.Same(shell.Window.ScreenHost.ViewOf(vm.Current!), shell.Window.ScreenHost.VisibleView);
        AssertAtRest(shell);
    }

    [AvaloniaFact]
    public void Launch_plays_once_without_holding_up_the_first_frame()
    {
        using var held = HoldTimers();
        using var live = new AnimatedShell();
        var window = live.Window;
        var t = window.Targets!;
        var launch = Assert.IsType<MotionHandle>(Animator.Running(t.Content));
        Assert.Equal(MotionState.Offset(0, AppMotion.LaunchY, 0), launch.From);
        Assert.Equal(MotionState.Rest, launch.To);
        Assert.Equal(AppMotion.Ms(AppMotion.Launch), launch.Duration);
        Assert.Same(AppMotion.EaseOut, launch.Easing);
        Assert.Null(Animator.Running(t.Title));

        // Frames render while it runs (it never waits for anything): the frame drawn now shows the content area at the
        // launch's start (faded, 12 px low), not at rest, so it was produced mid-animation.
        var midLaunch = Capture(window);
        Assert.True(launch.IsAnimating);
        launch.Complete();
        var settled = Capture(window);
        var content = (int)t.Content.TranslatePoint(default, window)!.Value.Y;
        var moved = Differing(midLaunch, settled, window, content, (int)window.Bounds.Height);
        Assert.True(moved.Count > 1000, $"only {moved.Count} channels of the content area differ from the settled frame");
        Assert.Empty(Differing(midLaunch, settled, window, 0, content)); // the title bar and header do not move

        window.Hide();
        window.Show();
        Assert.Null(Animator.Running(t.Content)); // once per window, not per show
    }

    [AvaloniaFact]
    public void With_animations_off_every_kind_swaps_at_once_and_ends_at_rest()
    {
        using var shell = ShellFixture.Open(AppView.Week); // the fixture opens with OS animations off
        Assert.False(AppMotion.SystemAnimationsEnabled);
        var t = shell.Window.Targets!;
        foreach (var kind in Enum.GetValues<TransitionKind>())
        {
            var swaps = 0;
            ShellComposition.Transitions.Run(kind, new Point(0.1, 0.9), () => swaps++, t);
            Assert.Equal(1, swaps);
            AssertNothingRuns(t);
        }

        ShellComposition.Transitions.Launch(t);
        AssertNothingRuns(t);

        var counter = Count(shell);
        shell.Vm.GoSection(AppView.Inbox);
        Assert.Equal((1, 1), (counter.Runs, counter.Swaps));
        shell.Vm.ZoomTo(AppView.Day, Today, new Point(0.5, 0.5));
        Assert.Equal((2, 2), (counter.Runs, counter.Swaps));
        AssertNothingRuns(t);
        AssertAtRest(shell);
    }

    [AvaloniaFact]
    public void Transitions_never_rebuild_the_screens_visual_trees()
    {
        using var shell = Live(AppView.Week);
        var vm = shell.Vm;
        var host = shell.Window.ScreenHost;
        AppView[] views = [AppView.Week, AppView.Inbox, AppView.Today, AppView.Day, AppView.Year, AppView.Month, AppView.Settings];
        void Go(AppView view)
        {
            switch (view)
            {
                case AppView.Inbox or AppView.Today: vm.GoSection(view); break;
                case AppView.Settings: vm.OpenSettings(); break;
                default:
                    if (vm.Nav.View == AppView.Settings)
                    {
                        vm.BackFromSettings();
                    }

                    vm.ZoomTo(view, Today);
                    break;
            }
        }

        foreach (var view in views)
        {
            Go(view);
            Finish(shell);
        }

        var before = views.ToDictionary(v => v, v => (View: host.ViewOf(vm.ScreenFor(v))!, Tree: host.ViewOf(vm.ScreenFor(v))!.GetVisualDescendants().First()));
        var count = host.CachedViewCount;
        foreach (var view in Enumerable.Reverse(views).Concat(views))
        {
            Go(view);
            Frame(shell);
            Finish(shell);
        }

        Assert.Equal(count, host.CachedViewCount);
        foreach (var view in views)
        {
            var now = host.ViewOf(vm.ScreenFor(view))!;
            Assert.Same(before[view].View, now);
            Assert.Same(before[view].Tree, now.GetVisualDescendants().First());
        }
    }

    [AvaloniaFact]
    public void The_at_rest_frame_check_sees_content_left_hidden_or_offset()
    {
        using var shell = Live(AppView.Week);
        var content = shell.Window.Targets!.Content;
        Animator.SlideTo(content, 0, -AppMotion.SectionExitY).Complete(); // an exit that was never followed by an entrance
        Assert.True(OffRest(shell.Window).Count > 1000);
        Animator.SlideTo(content, 0, AppMotion.SectionExitY, fade: false).Complete(); // visible but 10 px off
        Assert.True(OffRest(shell.Window).Count > 1000);
        Animator.Animate(content, MotionState.Rest, MotionState.Zoomed(0.99), AppMotion.Zoom, AppMotion.EaseOut).Complete();
        Assert.True(OffRest(shell.Window).Count > 100); // 1 % short of full size
        Assert.Empty(OffRest(shell.Window));
    }

    [AvaloniaFact]
    public void Content_moved_by_a_transition_is_clipped_below_the_page_header()
    {
        // The prototype's body is overflow: hidden under the header: a zoom out around a low click point or a section
        // entering from above never draws over the breadcrumbs / title.
        using var shell = Live(AppView.Week);
        var w = shell.Window;
        var content = w.Targets!.Content;
        var below = (int)w.ContentClip.TranslatePoint(default, w)!.Value.Y;
        var header = (int)w.Header.TranslatePoint(default, w)!.Value.Y;
        Assert.Equal(below, (int)Math.Round(header + w.Header.Bounds.Height)); // the clip starts where the header ends
        var rest = Capture(w);

        MotionState[] held =
        [
            MotionState.Zoomed(AppMotion.ZoomOutFrom),            // 1.10 around 50 % 95 %: grows ~80 px upwards
            MotionState.Offset(0, -AppMotion.SectionEnterY),      // a section entering from above, at its start
        ];
        foreach (var state in held)
        {
            Animator.Animate(content, MotionState.Rest, state, AppMotion.Zoom, AppMotion.EaseOut,
                new RelativePoint(0.5, 0.95, RelativeUnit.Relative)).Complete(); // held at that state on the render thread
            var moved = Capture(w);
            Assert.True(Differing(rest, moved, w, below, (int)w.Bounds.Height).Count > 1000); // the content did move
            var over = Differing(rest, moved, w, 0, below);
            Assert.True(over.Count == 0, $"{over.Count} channels above the content area changed: {string.Join(", ", over.Take(12))}");
            Animator.Reset(content);
        }

        Assert.Empty(OffRest(w));
    }
    // ---- Sidebar collapse ---------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Sidebar_collapse_and_expand_slide_340ms_and_end_at_the_exact_widths()
    {
        using var held = HoldTimers(); // the mid-slide geometry below must not depend on how long the frames take
        using var shell = Live(AppView.Week);
        var w = shell.Window;
        var collapse = w.SidebarCollapse!;
        var travel = 232.0 - 56.0;

        shell.Vm.ToggleSidebar();
        var slide = Assert.IsType<MotionHandle>(collapse.Running);
        Assert.Same(w.MainPanel, slide.Target);
        Assert.Equal(AppMotion.Ms(AppMotion.Slide), slide.Duration);
        Assert.Same(AppMotion.EaseOut, slide.Easing);
        Assert.Equal(MotionState.Offset(travel, 0), slide.From); // drawn where it was, at 232
        Assert.Equal(MotionState.Rest, slide.To);
        Frames(shell, 3);
        Assert.True(slide.IsAnimating);
        Assert.Equal(56, X(w.MainPanel, w));                     // laid out at its final place once, at the start
        Assert.Equal(232, w.SidebarColumn.Bounds.Width);         // the sidebar stays under the panel until the end
        slide.Complete();
        Frames(shell, 1);
        AssertSidebar(shell, rail: true);

        shell.Vm.ToggleSidebar();
        slide = Assert.IsType<MotionHandle>(collapse.Running);
        Assert.Equal(AppMotion.Ms(AppMotion.Slide), slide.Duration);
        Assert.Same(AppMotion.EaseOut, slide.Easing);
        Assert.Equal(MotionState.Rest, slide.From);
        Assert.Equal(MotionState.Offset(travel, 0), slide.To);
        Frames(shell, 3);
        Assert.True(slide.IsAnimating);
        Assert.Equal(56, X(w.MainPanel, w));                      // still laid out wide, sliding right
        Assert.Equal(232, w.SidebarColumn.Bounds.Width);          // the full sidebar is uncovered as it slides
        slide.Complete();
        Frames(shell, 1);
        AssertSidebar(shell, rail: false);
    }

    [AvaloniaFact]
    public void A_second_toggle_mid_slide_continues_from_the_panel_and_ends_right()
    {
        using var held = HoldTimers(); // "mid-slide" must not depend on how long the frames take
        using var shell = Live(AppView.Week);
        var collapse = shell.Window.SidebarCollapse!;
        var travel = 232.0 - 56.0;

        shell.Vm.ToggleSidebar();
        var first = collapse.Running!;
        Frames(shell, 2);
        Assert.True(first.IsAnimating);
        shell.Vm.ToggleSidebar(); // expand before the collapse has finished
        Assert.True(first.WasCancelled);
        var second = Assert.IsType<MotionHandle>(collapse.Running);
        Assert.InRange(second.From.X, 0, travel); // from where the panel is drawn, not from a jump
        Assert.Equal(MotionState.Offset(travel, 0), second.To);
        Finish(shell);
        AssertSidebar(shell, rail: false);

        for (var i = 0; i < 5; i++)
        {
            shell.Vm.ToggleSidebar();
            Frame(shell);
        }

        Finish(shell);
        AssertSidebar(shell, rail: true);

        // Entering / leaving Settings while collapsed changes the column at once (its sidebar is always full width).
        shell.Vm.OpenSettings();
        Finish(shell);
        Assert.Null(collapse.Running);
        Assert.Equal(232, shell.Window.SidebarColumn.Bounds.Width);
        Assert.Equal(232, X(shell.Window.MainPanel, shell.Window));
        shell.Vm.BackFromSettings();
        Finish(shell);
        AssertSidebar(shell, rail: true);
    }

    [AvaloniaFact]
    public void A_sidebar_toggle_lays_the_week_out_and_re_projects_it_at_most_once()
    {
        using var shell = Live(AppView.Week);
        var w = shell.Window;
        var week = shell.Screen<WeekViewModel>();
        foreach (var expectRail in new[] { true, false, true, false })
        {
            var models = new List<object?> { week.Model };
            var widths = new List<double> { w.MainPanel.Bounds.Width };
            void OnLayout(object? sender, EventArgs e)
            {
                if (!ReferenceEquals(models[^1], week.Model))
                {
                    models.Add(week.Model);
                }

                if (widths[^1] != w.MainPanel.Bounds.Width)
                {
                    widths.Add(w.MainPanel.Bounds.Width);
                }
            }

            w.LayoutUpdated += OnLayout;
            shell.Vm.ToggleSidebar();
            Frames(shell, 12); // a dozen frames of the slide
            w.SidebarCollapse!.Running?.Complete();
            Frames(shell, 3);
            w.LayoutUpdated -= OnLayout;

            Assert.True(models.Count <= 2, $"the week re-projected {models.Count - 1} times in one toggle");
            Assert.Equal(2, widths.Count); // the main panel was laid out at one new width, once
            AssertSidebar(shell, expectRail);
        }
    }

    // ---- OS animation setting at run time ----------------------------------------------------------------------------

    [AvaloniaFact]
    public void Turning_os_animations_off_and_on_retimes_existing_xaml_transitions()
    {
        using var live = new AnimatedShell(); // built with animations on, so its XAML transitions have durations
        var transition = live.Window.GetVisualDescendants()
            .SelectMany(v => v.Transitions ?? [])
            .OfType<TransitionBase>()
            .First(t => t.Duration > TimeSpan.Zero);
        var duration = transition.Duration;
        var raised = 0;
        void OnChanged(object? sender, EventArgs e) => raised++;
        AppMotion.SystemAnimationsChanged += OnChanged;
        try
        {
            AppMotion.SystemAnimationsEnabled = false;
            Assert.Equal(1, raised);
            Assert.Equal(TimeSpan.Zero, transition.Duration);
            AppMotion.SystemAnimationsEnabled = false; // unchanged: no event
            Assert.Equal(1, raised);

            AppMotion.SystemAnimationsEnabled = true;
            Assert.Equal(2, raised);
            Assert.Equal(duration, transition.Duration);
        }
        finally
        {
            AppMotion.SystemAnimationsChanged -= OnChanged;
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------------

    /// <summary>Opens the shell (animations off while it builds, as the fixture does), then switches OS animations on.</summary>
    private static ShellFixture Live(AppView view)
    {
        var shell = ShellFixture.Open(view);
        AppMotion.SystemAnimationsEnabled = true;
        return shell;
    }

    private static void AssertZoom(MotionHandle handle, double scale, RelativePoint origin)
    {
        Assert.Equal(MotionState.Zoomed(scale, 0), handle.From);
        Assert.Equal(MotionState.Rest, handle.To);
        Assert.Equal(AppMotion.Ms(AppMotion.Zoom), handle.Duration);
        Assert.Same(AppMotion.EaseOut, handle.Easing);
        Assert.Equal(origin, handle.Origin);
    }

    private static void AssertSidebarSlide(Control sidebar, double x)
    {
        var slide = Assert.IsType<MotionHandle>(Animator.Running(sidebar));
        Assert.Equal(MotionState.Offset(x, 0, 0), slide.From);
        Assert.Equal(MotionState.Rest, slide.To);
        Assert.Equal(AppMotion.Ms(AppMotion.Slide), slide.Duration);
        Assert.Same(AppMotion.EaseOut, slide.Easing);
    }

    private static void AssertTitleEntrance(ContentTransitionTargets t)
    {
        var title = Assert.IsType<MotionHandle>(Animator.Running(t.Title));
        Assert.Equal(MotionState.Offset(0, AppMotion.TitleY, 0), title.From);
        Assert.Equal(MotionState.Rest, title.To);
        Assert.Equal(AppMotion.Ms(AppMotion.Title), title.Duration);
        Assert.Same(AppMotion.EaseOut, title.Easing);
    }

    private static void AssertNothingRuns(ContentTransitionTargets t)
    {
        Assert.Null(Animator.Running(t.Content));
        Assert.Null(Animator.Running(t.Title));
        Assert.Null(Animator.Running(t.AppSidebar!));
        Assert.Null(Animator.Running(t.SettingsSidebar!));
    }

    /// <summary>
    /// Nothing animates and every target is at rest: UI-side opacity 1 / no render transform, and the rendered frame is
    /// identical (within <see cref="RestTolerance"/>) to the frame after every target is explicitly reset to its resting state (so nothing is left
    /// faded, offset or scaled on the render thread). The content area is not blank.
    /// </summary>
    private static void AssertAtRest(ShellFixture shell)
    {
        var w = shell.Window;
        var t = w.Targets!;
        Control[] targets = [t.Content, t.Title, t.AppSidebar!, t.SettingsSidebar!, w.MainPanel];
        foreach (var target in targets)
        {
            Assert.Null(Animator.Running(target));
            Assert.Equal(1, target.Opacity);
            Assert.Null(target.RenderTransform);
        }

        Assert.True(t.Content.IsEffectivelyVisible);
        var differing = OffRest(w);
        Assert.True(differing.Count == 0,
            $"{differing.Count} channels differ from the reset frame: {string.Join(", ", differing.Take(12))}");

        // The content area shows something besides the panel colour (it was not left transparent).
        var content = t.Content.TranslatePoint(default, w)!.Value;
        var fill = Pixels.Resource("Brush.Bg.Main");
        var ink = 0;
        using var frame = w.CaptureRenderedFrame()!;
        for (var y = (int)content.Y + 4; y < (int)(content.Y + t.Content.Bounds.Height) - 4; y += 16)
        {
            for (var x = (int)content.X + 4; x < (int)(content.X + t.Content.Bounds.Width) - 4; x += 16)
            {
                if (!Pixels.Near(fill, Pixels.At(frame, x, y), 2))
                {
                    ink++;
                }
            }
        }

        Assert.True(ink > 20, $"the content area looks empty ({ink} sampled pixels differ from the panel)");
    }

    /// <summary>
    /// Renders <paramref name="w"/> fully, resets every transition target (and the main panel) to its resting state,
    /// renders again and lists the channels that differ by more than <see cref="RestTolerance"/>.
    /// </summary>
    private static List<string> OffRest(MainWindow w)
    {
        var t = w.Targets!;
        var settled = Capture(w);
        foreach (var target in new Control[] { t.Content, t.Title, t.AppSidebar!, t.SettingsSidebar!, w.MainPanel })
        {
            Animator.Reset(target);
        }

        var reset = Capture(w);
        return Differing(settled, reset, w, 0, (int)w.Bounds.Height);
    }

    /// <summary>The channels of rows [<paramref name="fromRow"/>, <paramref name="toRow"/>) that differ by more than
    /// <see cref="RestTolerance"/> between two full frames of <paramref name="w"/>.</summary>
    private static List<string> Differing(byte[] a, byte[] b, Window w, int fromRow, int toRow)
    {
        Assert.Equal(a.Length, b.Length);
        var width = (int)w.Bounds.Width;
        var differing = new List<string>();
        var end = Math.Min(a.Length, toRow * width * 4);
        for (var i = Math.Max(0, fromRow * width * 4); i < end; i++)
        {
            if (Math.Abs(a[i] - b[i]) > RestTolerance)
            {
                differing.Add($"({i / 4 % width},{i / 4 / width}) {a[i]}→{b[i]}");
            }
        }

        return differing;
    }

    /// <summary>
    /// Until disposed, animation handles finish only when the test completes, cancels or supersedes them
    /// (<see cref="Animator.HoldFinishTimers"/>), so mid-animation assertions never depend on how long frames take.
    /// </summary>
    private static TimerHold HoldTimers() => new();

    private sealed class TimerHold : IDisposable
    {
        private readonly bool _was = Animator.HoldFinishTimers;

        public TimerHold() => Animator.HoldFinishTimers = true;

        public void Dispose() => Animator.HoldFinishTimers = _was;
    }
    private static void AssertSidebar(ShellFixture shell, bool rail)
    {
        var w = shell.Window;
        var width = rail ? 56 : 232;
        Assert.Null(w.SidebarCollapse!.Running);
        Assert.Equal(rail, w.SidebarCollapse.IsRail);
        Assert.Equal(width, w.SidebarColumn.Bounds.Width);
        Assert.Equal(width, w.MainInset.Bounds.Width);
        Assert.Equal(width, X(w.MainPanel, w));
        Assert.Equal(1600 - width, w.MainPanel.Bounds.Width);
        AssertAtRest(shell);
    }

    private static double X(Control control, Window window) => control.TranslatePoint(default, window)!.Value.X;

    private static Point ZoomOriginOf(Control host, Control source)
    {
        var centre = source.TranslatePoint(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2), host)!.Value;
        return new Point(centre.X / host.Bounds.Width, centre.Y / host.Bounds.Height);
    }

    private static void ClickWithoutSettling(ShellFixture shell, Control target)
    {
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), shell.Window)!.Value;
        shell.Window.MouseMove(centre);
        shell.Window.MouseDown(centre, MouseButton.Left);
        shell.Window.MouseUp(centre, MouseButton.Left);
    }

    /// <summary>One frame: queued jobs, layout, a forced render tick. Running animations keep running.</summary>
    private static void Frame(ShellFixture shell)
    {
        Dispatcher.UIThread.RunJobs();
        shell.Window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Frames(ShellFixture shell, int count)
    {
        for (var i = 0; i < count; i++)
        {
            Frame(shell);
        }
    }

    /// <summary>Completes every running animation (an exit's completion may start an entrance) until nothing runs.</summary>
    private static void Finish(ShellFixture shell)
    {
        for (var i = 0; i < 8; i++)
        {
            Frame(shell);
            foreach (var visual in shell.Window.GetVisualDescendants().ToList())
            {
                Animator.Running(visual)?.Complete();
            }
        }

        Frame(shell);
    }

    /// <summary>A full redraw of <paramref name="window"/> (the whole window is invalidated, so no anti-aliased edge is
    /// re-blended by a partial redraw), as raw pixel bytes.</summary>
    private static byte[] Capture(Window window)
    {
        window.InvalidateVisual();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
        using var fb = frame.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>Counts content transitions and swaps by wrapping the shell's swapper.</summary>
    private static SwapCounter Count(ShellFixture shell)
    {
        var counter = new SwapCounter();
        var inner = shell.Vm.ContentSwapper;
        shell.Vm.ContentSwapper = (kind, origin, swap) =>
        {
            counter.Runs++;
            inner(kind, origin, () =>
            {
                counter.Swaps++;
                swap();
            });
        };
        return counter;
    }

    /// <summary>The real shell window (DemoData, no-op platform) built and shown with OS animations on.</summary>
    private sealed class AnimatedShell : IDisposable
    {
        private readonly bool _animationsWere = AppMotion.SystemAnimationsEnabled;
        private readonly AppServices _app;

        public AnimatedShell()
        {
            AppMotion.SystemAnimationsEnabled = true;
            _app = AppServices.CreateDemo(null, PlatformServices.NoOp());
            Window = ShellComposition.CreateMainWindow(_app, Application.Current!, new NavState(AppView.Week, Today));
            Window.Width = 1600;
            Window.Height = 1000;
            Window.Show();
        }

        public MainWindow Window { get; }

        public void Dispose()
        {
            Window.Close();
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            new ThemeService(Application.Current!).Apply(AppSettings.Default);
            AppMotion.SystemAnimationsEnabled = _animationsWere;
        }
    }

    private sealed class SwapCounter
    {
        public int Runs { get; set; }

        public int Swaps { get; set; }
    }
}
