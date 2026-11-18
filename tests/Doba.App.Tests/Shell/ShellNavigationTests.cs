using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Doba.App.Screens;
using Doba.App.Screens.Day;
using Doba.App.Screens.Inbox;
using Doba.App.Screens.Settings;
using Doba.App.Screens.Today;
using Doba.App.Screens.Week;
using Doba.App.Shell;
using Doba.App.Tests.Support;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Navigation;

namespace Doba.App.Tests.Shell;

/// <summary>Every navigation path goes through Core's Navigator and is applied by MainViewModel exactly once.</summary>
public class ShellNavigationTests
{
    private static readonly DateOnly Today = DemoData.Today;

    /// <summary>Runs <paramref name="step"/> and checks it against <paramref name="expected"/> (Navigator's own result):
    /// Nav, one Navigated with the right kind/origin, the matching screen active and alone.</summary>
    private static void Step(ShellFixture shell, Action<MainViewModel> step, NavResult expected, Point? origin = null)
    {
        var events = new List<NavigatedEventArgs>();
        var from = shell.Vm.Nav;
        shell.Vm.Navigated += Record;
        try
        {
            step(shell.Vm);
            shell.Settle();
        }
        finally
        {
            shell.Vm.Navigated -= Record;
        }

        Assert.Equal(expected.State, shell.Vm.Nav);
        if (expected.Transition == TransitionKind.None)
        {
            Assert.Empty(events);
            return;
        }

        var e = Assert.Single(events);
        Assert.Equal(from, e.From);
        Assert.Equal(expected.State, e.To);
        Assert.Equal(expected.Transition, e.Kind);
        Assert.Equal(origin, e.Origin);
        Assert.Same(shell.Vm.ScreenFor(expected.State.View), shell.Vm.Current);
        AssertOneActive(shell);

        void Record(object? sender, NavigatedEventArgs args) => events.Add(args);
    }

    private static void AssertOneActive(ShellFixture shell)
    {
        var all = Enum.GetValues<AppView>().Select(shell.Vm.ScreenFor).ToList();
        var active = Assert.Single(all, s => s.IsActive);
        Assert.Same(shell.Vm.Current, active);
        Assert.Equal(shell.Vm.Nav, active.State);
        var visible = shell.Window.ScreenHost.Children.Where(c => c.IsVisible).ToList();
        Assert.Single(visible);
        Assert.Same(active, visible[0].DataContext);
    }

    [AvaloniaFact]
    public void Starts_on_the_requested_screen_with_one_active_view()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        Assert.Equal(new NavState(AppView.Week, Today), shell.Vm.Nav);
        Assert.IsType<WeekViewModel>(shell.Vm.Current);
        Assert.IsType<WeekView>(shell.Window.ScreenHost.VisibleView);
        Assert.Equal(1, shell.Window.ScreenHost.CachedViewCount);
        AssertOneActive(shell);
    }

    [Theory]
    [InlineData(LaunchView.Today, AppView.Today)]
    [InlineData(LaunchView.Week, AppView.Week)]
    [InlineData(LaunchView.Inbox, AppView.Inbox)]
    public void Initial_state_follows_open_on_launch(LaunchView launch, AppView expected) =>
        Assert.Equal(new NavState(expected, Today), MainViewModel.InitialState(new AppSettings { LaunchView = launch }, Today));

    [AvaloniaFact]
    public void Sidebar_sections()
    {
        using var shell = ShellFixture.Open(AppView.Week, new DateOnly(2026, 12, 3));
        Step(shell, vm => vm.GoSection(AppView.Inbox), Navigator.GoSection(shell.Vm.Nav, AppView.Inbox, shell.Vm.Nav.Anchor));
        Step(shell, vm => vm.GoSection(AppView.Inbox), new NavResult(shell.Vm.Nav, TransitionKind.None));
        Step(shell, vm => vm.GoSection(AppView.Today), Navigator.GoSection(shell.Vm.Nav, AppView.Today, Today));
        Assert.Equal(Today, shell.Vm.Nav.Anchor);
        Step(shell, vm => vm.GoSection(AppView.Inbox), Navigator.GoSection(shell.Vm.Nav, AppView.Inbox, Today));
        Assert.Equal(TransitionKind.SectionUp, Navigator.GoSection(new NavState(AppView.Today, Today), AppView.Inbox, Today).Transition);
        Assert.IsType<InboxViewModel>(shell.Vm.Current);
    }

    [AvaloniaFact]
    public void Calendar_section_from_inbox_today_settings_and_day()
    {
        using var shell = ShellFixture.Open(AppView.Inbox);
        Step(shell, vm => vm.GoCalendar(), Navigator.GoCalendar(shell.Vm.Nav));
        Assert.Equal(AppView.Week, shell.Vm.Nav.View);
        Step(shell, vm => vm.GoCalendar(), new NavResult(shell.Vm.Nav, TransitionKind.None));

        Step(shell, vm => vm.ZoomTo(AppView.Day, Today), Navigator.Zoom(shell.Vm.Nav, AppView.Day, Today));
        Step(shell, vm => vm.GoCalendar(), Navigator.GoCalendar(shell.Vm.Nav));
        Assert.Equal(TransitionKind.ZoomOut, Navigator.GoCalendar(new NavState(AppView.Day, Today)).Transition);

        Step(shell, vm => vm.OpenSettings(), Navigator.OpenSettings(shell.Vm.Nav));
        Step(shell, vm => vm.GoCalendar(), Navigator.GoCalendar(shell.Vm.Nav));
        Assert.IsType<WeekViewModel>(shell.Vm.Current);
    }

    [AvaloniaFact]
    public void Zoom_levels_and_clicks_pass_the_origin_through()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        Step(shell, vm => vm.SetZoomLevel(AppView.Month), Navigator.Zoom(shell.Vm.Nav, AppView.Month, Today));
        Step(shell, vm => vm.SetZoomLevel(AppView.Year), Navigator.Zoom(shell.Vm.Nav, AppView.Year, Today));

        var origin = new Point(0.25, 0.75);
        var march = new DateOnly(2026, 3, 1);
        Step(shell, vm => vm.ZoomTo(AppView.Month, march, origin), Navigator.Zoom(shell.Vm.Nav, AppView.Month, march), origin);
        Step(shell, vm => vm.ZoomTo(AppView.Day, march.AddDays(4), new Point(0.1, 0.2)),
            Navigator.Zoom(shell.Vm.Nav, AppView.Day, march.AddDays(4)), new Point(0.1, 0.2));
        Assert.IsType<DayViewModel>(shell.Vm.Current);

        Assert.Throws<ArgumentOutOfRangeException>(() => shell.Vm.SetZoomLevel(AppView.Inbox));
    }

    [AvaloniaFact]
    public void Shift_and_today()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        Step(shell, vm => vm.Shift(1), Navigator.Shift(shell.Vm.Nav, 1, Today));
        Step(shell, vm => vm.Shift(1), Navigator.Shift(shell.Vm.Nav, 1, Today));
        Step(shell, vm => vm.Shift(-1), Navigator.Shift(shell.Vm.Nav, -1, Today));
        Step(shell, vm => vm.GoToday(), Navigator.GoToday(shell.Vm.Nav, Today));
        Assert.Equal(new NavState(AppView.Week, Today), shell.Vm.Nav);

        Step(shell, vm => vm.GoSection(AppView.Inbox), Navigator.GoSection(shell.Vm.Nav, AppView.Inbox, Today));
        Step(shell, vm => vm.Shift(1), new NavResult(shell.Vm.Nav, TransitionKind.None));
        Step(shell, vm => vm.GoToday(), Navigator.GoToday(shell.Vm.Nav, Today));
        Assert.Equal(AppView.Week, shell.Vm.Nav.View);
    }

    [AvaloniaFact]
    public void Settings_open_tabs_and_back_restore_the_remembered_state()
    {
        var anchor = new DateOnly(2027, 2, 10);
        using var shell = ShellFixture.Open(AppView.Month, anchor);
        Step(shell, vm => vm.OpenSettings(), Navigator.OpenSettings(shell.Vm.Nav));
        Assert.Equal(new NavState(AppView.Month, anchor), shell.Vm.Nav.Back);
        Assert.True(shell.Vm.ShowsSettings);
        Step(shell, vm => vm.OpenSettings(), new NavResult(shell.Vm.Nav, TransitionKind.None));

        Step(shell, vm => vm.SetSettingsTab(SettingsTab.Data), Navigator.SetTab(shell.Vm.Nav, SettingsTab.Data));
        Assert.Equal(SettingsTab.Data, shell.Screen<SettingsViewModel>().Tab);
        Step(shell, vm => vm.SetSettingsTab(SettingsTab.Calendar), Navigator.SetTab(shell.Vm.Nav, SettingsTab.Calendar));
        Step(shell, vm => vm.SetSettingsTab(SettingsTab.Calendar), new NavResult(shell.Vm.Nav, TransitionKind.None));
        Assert.Equal(SettingsTab.Calendar, shell.Screen<SettingsViewModel>().Tab);

        Step(shell, vm => vm.BackFromSettings(), Navigator.BackFromSettings(shell.Vm.Nav, Today));
        Assert.Equal(AppView.Month, shell.Vm.Nav.View);
        Assert.Equal(anchor, shell.Vm.Nav.Anchor);
        Assert.False(shell.Vm.ShowsSettings);
        Step(shell, vm => vm.BackFromSettings(), new NavResult(shell.Vm.Nav, TransitionKind.None));
    }

    [AvaloniaFact]
    public void Settings_shows_the_settings_sidebar_at_full_width_even_when_collapsed()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        shell.Vm.ToggleSidebar();
        shell.Settle();
        Assert.Equal(56, shell.Window.SidebarColumn.Bounds.Width);
        Assert.True(shell.Window.AppSidebar.IsVisible);
        Assert.False(shell.Window.SettingsSidebar.IsVisible);

        shell.Vm.OpenSettings();
        shell.Settle();
        Assert.Equal(232, shell.Window.SidebarColumn.Bounds.Width);
        Assert.False(shell.Window.AppSidebar.IsVisible);
        Assert.True(shell.Window.SettingsSidebar.IsVisible);

        shell.Vm.BackFromSettings();
        shell.Settle();
        Assert.Equal(56, shell.Window.SidebarColumn.Bounds.Width);
    }

    [AvaloniaFact]
    public void Screens_and_views_are_created_once_and_reused()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var week = shell.Vm.Current!;
        var weekView = shell.Window.ScreenHost.VisibleView;
        shell.Navigate(AppView.Inbox);
        shell.Navigate(AppView.Today);
        shell.Navigate(AppView.Week);
        Assert.Same(week, shell.Vm.Current);
        Assert.Same(weekView, shell.Window.ScreenHost.VisibleView);
        Assert.Equal(3, shell.Window.ScreenHost.CachedViewCount);
        Assert.Same(shell.Vm.ScreenFor(AppView.Today), shell.Screen<TodayViewModel>());
        AssertOneActive(shell);
    }

    [AvaloniaFact]
    public void Every_swap_runs_through_the_content_transitions()
    {
        var recorder = new RecordingTransitions();
        var previous = ShellComposition.Transitions;
        ShellComposition.Transitions = recorder;
        try
        {
            using var shell = ShellFixture.Open(AppView.Week);
            Assert.Equal(1, recorder.Launches);
            Assert.Same(shell.Window.ScreenHost, recorder.Targets!.Content);
            Assert.Same(shell.Window.Header.TitleBlock, recorder.Targets.Title);
            Assert.Same(shell.Window.AppSidebar, recorder.Targets.AppSidebar);
            Assert.Same(shell.Window.SettingsSidebar, recorder.Targets.SettingsSidebar);

            recorder.Hold = true;
            shell.Vm.GoSection(AppView.Inbox);
            shell.Settle();
            Assert.Equal(AppView.Inbox, shell.Vm.Nav.View);
            Assert.IsType<WeekViewModel>(shell.Vm.Current);                 // the swap waits for the transition
            Assert.Equal("Week 46", shell.Vm.Header.Title);                 // the header changes with the content
            recorder.Release();
            Assert.IsType<InboxViewModel>(shell.Vm.Current);
            Assert.Equal("Inbox", shell.Vm.Header.Title);

            recorder.Hold = false;
            var origin = new Point(0.4, 0.6);
            shell.Vm.ZoomTo(AppView.Day, Today, origin);
            Assert.Equal((TransitionKind.SectionUp, (Point?)null), recorder.Runs[0]);
            Assert.Equal((TransitionKind.ZoomIn, (Point?)origin), recorder.Runs[1]);
            Assert.Equal(2, recorder.Runs.Count);
            Assert.Equal(1, recorder.Launches);
        }
        finally
        {
            ShellComposition.Transitions = previous;
        }
    }

    private sealed class RecordingTransitions : IContentTransitions
    {
        private Action? _pending;

        public bool Hold { get; set; }

        public int Launches { get; private set; }

        public ContentTransitionTargets? Targets { get; private set; }

        public List<(TransitionKind, Point?)> Runs { get; } = [];

        public void Run(TransitionKind kind, Point? origin, Action swap, ContentTransitionTargets targets)
        {
            Runs.Add((kind, origin));
            Targets = targets;
            if (Hold)
            {
                _pending = swap;
            }
            else
            {
                swap();
            }
        }

        public void Launch(ContentTransitionTargets targets)
        {
            Launches++;
            Targets = targets;
        }

        public void Release()
        {
            var swap = _pending;
            _pending = null;
            swap?.Invoke();
        }
    }
}
