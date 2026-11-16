using Doba.Core.Navigation;
using Doba.Core.Projections;

namespace Doba.Core.Tests;

public class NavigatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static NavState S(AppView v, int y = 2026, int m = 11, int d = 15) => new(v, new DateOnly(y, m, d));

    private static void Is(NavResult r, AppView view, DateOnly anchor, TransitionKind kind)
    {
        Assert.Equal(kind, r.Transition);
        Assert.Equal(view, r.State.View);
        Assert.Equal(anchor, r.State.Anchor);
    }

    [Fact]
    public void Zoom_direction_follows_calendar_depth()
    {
        var d = new DateOnly(2026, 11, 18);
        Is(Navigator.Zoom(S(AppView.Year), AppView.Month, d), AppView.Month, d, TransitionKind.ZoomIn);
        Is(Navigator.Zoom(S(AppView.Week), AppView.Day, d), AppView.Day, d, TransitionKind.ZoomIn);
        Is(Navigator.Zoom(S(AppView.Day), AppView.Year, d), AppView.Year, d, TransitionKind.ZoomOut);
        Is(Navigator.Zoom(S(AppView.Week), AppView.Week, d), AppView.Week, d, TransitionKind.Next);
        // From outside the calendar every level is "deeper" (Inbox › day group link, Inbox › Later → month).
        Is(Navigator.Zoom(S(AppView.Inbox), AppView.Day, d), AppView.Day, d, TransitionKind.ZoomIn);
        Is(Navigator.Zoom(S(AppView.Inbox), AppView.Month, d), AppView.Month, d, TransitionKind.ZoomIn);
    }

    [Fact]
    public void Shift_moves_by_level_and_snaps_to_today_for_year_and_month()
    {
        Is(Navigator.Shift(S(AppView.Day), 1, Today), AppView.Day, new(2026, 11, 16), TransitionKind.Next);
        Is(Navigator.Shift(S(AppView.Week), -1, Today), AppView.Week, new(2026, 11, 8), TransitionKind.Prev);
        Is(Navigator.Shift(S(AppView.Month, 2026, 12, 1), 1, Today), AppView.Month, new(2027, 1, 1), TransitionKind.Next);
        Is(Navigator.Shift(S(AppView.Month, 2026, 1, 31), -1, Today), AppView.Month, new(2025, 12, 1), TransitionKind.Prev);
        Is(Navigator.Shift(S(AppView.Month, 2026, 12, 1), -1, Today), AppView.Month, Today, TransitionKind.Prev);
        Is(Navigator.Shift(S(AppView.Year, 2025, 11, 1), 1, Today), AppView.Year, Today, TransitionKind.Next);
        Is(Navigator.Shift(S(AppView.Year, 2026, 3, 9), 1, Today), AppView.Year, new(2027, 3, 1), TransitionKind.Next);
        Is(Navigator.Shift(S(AppView.Month, 2026, 1, 15), 12, Today), AppView.Month, new(2027, 1, 1), TransitionKind.Next);
    }

    [Theory]
    [InlineData(AppView.Inbox)]
    [InlineData(AppView.Today)]
    [InlineData(AppView.Settings)]
    public void Shift_is_a_no_op_outside_the_calendar(AppView v)
    {
        var s = S(v);
        var r = Navigator.Shift(s, 1, Today);
        Assert.Equal(TransitionKind.None, r.Transition);
        Assert.Same(s, r.State);
        Assert.Equal(TransitionKind.None, Navigator.Shift(S(AppView.Week), 0, Today).Transition);
    }

    [Theory]
    [InlineData(AppView.Inbox)]
    [InlineData(AppView.Today)]
    [InlineData(AppView.Settings)]
    public void GoToday_outside_the_calendar_zooms_into_week(AppView v)
    {
        var s = v == AppView.Settings ? Navigator.OpenSettings(S(AppView.Day, 2026, 3, 1)).State : S(v, 2026, 3, 1);
        var r = Navigator.GoToday(s, Today);
        Is(r, AppView.Week, Today, TransitionKind.ZoomIn);
        Assert.Null(r.State.Back);
    }

    [Fact]
    public void GoToday_in_the_calendar_slides_towards_today()
    {
        Is(Navigator.GoToday(S(AppView.Month, 2027, 2, 1), Today), AppView.Month, Today, TransitionKind.Prev);
        Is(Navigator.GoToday(S(AppView.Day, 2026, 1, 1), Today), AppView.Day, Today, TransitionKind.Next);
        Is(Navigator.GoToday(S(AppView.Week), Today), AppView.Week, Today, TransitionKind.Next);
    }

    [Fact]
    public void GoSection_direction_and_no_op()
    {
        var d = new DateOnly(2026, 11, 20);
        Is(Navigator.GoSection(S(AppView.Week), AppView.Inbox, d), AppView.Inbox, d, TransitionKind.SectionUp);
        Is(Navigator.GoSection(S(AppView.Inbox), AppView.Today, Today), AppView.Today, Today, TransitionKind.SectionDown);
        Is(Navigator.GoSection(S(AppView.Today), AppView.Inbox, d), AppView.Inbox, d, TransitionKind.SectionUp);
        Is(Navigator.GoSection(S(AppView.Day), AppView.Today, Today), AppView.Today, Today, TransitionKind.SectionUp);
        // Same section index but a different view still animates (index ≥ current).
        Is(Navigator.GoSection(S(AppView.Week), AppView.Month, d), AppView.Month, d, TransitionKind.SectionDown);
        var s = S(AppView.Inbox);
        var r = Navigator.GoSection(s, AppView.Inbox, d);
        Assert.Equal(TransitionKind.None, r.Transition);
        Assert.Same(s, r.State);
    }

    [Fact]
    public void GoCalendar_from_each_screen()
    {
        var a = new DateOnly(2026, 11, 20);
        Is(Navigator.GoCalendar(new NavState(AppView.Inbox, a)), AppView.Week, a, TransitionKind.SectionDown);
        Is(Navigator.GoCalendar(new NavState(AppView.Today, a)), AppView.Week, a, TransitionKind.SectionDown);
        Is(Navigator.GoCalendar(Navigator.OpenSettings(new NavState(AppView.Month, a)).State), AppView.Week, a, TransitionKind.ZoomIn);
        Is(Navigator.GoCalendar(new NavState(AppView.Day, a)), AppView.Week, a, TransitionKind.ZoomOut);
        foreach (var v in new[] { AppView.Year, AppView.Month, AppView.Week })
        {
            var s = new NavState(v, a);
            var r = Navigator.GoCalendar(s);
            Assert.Equal(TransitionKind.None, r.Transition);
            Assert.Same(s, r.State);
        }
    }

    [Fact]
    public void Settings_remember_where_they_were_opened_from()
    {
        var day = new NavState(AppView.Day, new DateOnly(2026, 11, 20), SettingsTab.Data);
        var open = Navigator.OpenSettings(day);
        Assert.Equal(TransitionKind.SettingsIn, open.Transition);
        Assert.Equal(AppView.Settings, open.State.View);
        Assert.Equal(SettingsTab.Data, open.State.Tab);
        Assert.Equal(AppView.Day, open.State.Back!.View);
        Assert.Equal(new DateOnly(2026, 11, 20), open.State.Back.Anchor);

        var again = Navigator.OpenSettings(open.State);
        Assert.Equal(TransitionKind.None, again.Transition);

        var tab = Navigator.SetTab(open.State, SettingsTab.Windows);
        Assert.Equal(AppView.Day, tab.State.Back!.View);

        var back = Navigator.BackFromSettings(tab.State, Today);
        Is(back, AppView.Day, new DateOnly(2026, 11, 20), TransitionKind.SettingsOut);
        Assert.Null(back.State.Back);
        Assert.Equal(SettingsTab.Windows, back.State.Tab);

        // Without a remembered state: Week at today.
        Is(Navigator.BackFromSettings(new NavState(AppView.Settings, new DateOnly(2026, 1, 1)), Today), AppView.Week, Today, TransitionKind.SettingsOut);
        // Not in Settings: nothing to go back from.
        Assert.Equal(TransitionKind.None, Navigator.BackFromSettings(S(AppView.Week), Today).Transition);
    }

    [Fact]
    public void SetTab_direction()
    {
        var s = new NavState(AppView.Settings, Today, SettingsTab.Calendar);
        var next = Navigator.SetTab(s, SettingsTab.Shortcuts);
        Assert.Equal(TransitionKind.Next, next.Transition);
        Assert.Equal(SettingsTab.Shortcuts, next.State.Tab);
        Assert.Equal(TransitionKind.Prev, Navigator.SetTab(s, SettingsTab.Appearance).Transition);
        var none = Navigator.SetTab(s, SettingsTab.Calendar);
        Assert.Equal(TransitionKind.None, none.Transition);
        Assert.Same(s, none.State);
    }

    [Fact]
    public void Leaving_settings_by_any_route_drops_back()
    {
        var settings = Navigator.OpenSettings(S(AppView.Month)).State;
        Assert.Null(Navigator.GoToday(settings, Today).State.Back);
        Assert.Null(Navigator.GoCalendar(settings).State.Back);
        Assert.Null(Navigator.GoSection(settings, AppView.Inbox, Today).State.Back);
    }
}

public class HeaderProjectionTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    [Fact]
    public void Week_subtitle_shows_the_first_month_only_when_months_differ()
    {
        Assert.Equal("9 – 15 Nov", HeaderProjection.Build(new NavState(AppView.Week, Today), Today, DayOfWeek.Monday).Subtitle);
        Assert.Equal("30 Nov – 6 Dec", HeaderProjection.Build(new NavState(AppView.Week, new DateOnly(2026, 12, 3)), Today, DayOfWeek.Monday).Subtitle);
        var yearEnd = HeaderProjection.Build(new NavState(AppView.Week, new DateOnly(2026, 12, 31)), Today, DayOfWeek.Monday);
        Assert.Equal("28 Dec – 3 Jan", yearEnd.Subtitle);
        Assert.Equal("Week 53", yearEnd.Title);
        Assert.Equal(["2026", "December"], yearEnd.Crumbs.Select(c => c.Label));
    }

    [Fact]
    public void Sunday_start_week_header()
    {
        var h = HeaderProjection.Build(new NavState(AppView.Week, Today), Today, DayOfWeek.Sunday);
        Assert.Equal("Week 47", h.Title);
        Assert.Equal("15 – 21 Nov", h.Subtitle);
        var day = HeaderProjection.Build(new NavState(AppView.Day, Today), Today, DayOfWeek.Sunday);
        Assert.Equal("Week 47", day.Crumbs[2].Label);
    }

    [Fact]
    public void Day_header_and_crumb_targets()
    {
        var d = new DateOnly(2026, 11, 16);
        var h = HeaderProjection.Build(new NavState(AppView.Day, d), Today, DayOfWeek.Monday);
        Assert.Equal("16 November", h.Title);
        Assert.Equal("Monday", h.Subtitle);
        Assert.Equal(["2026", "November", "Week 47"], h.Crumbs.Select(c => c.Label));
        Assert.Equal([new NavTarget(AppView.Year, d), new NavTarget(AppView.Month, d), new NavTarget(AppView.Week, d)], h.Crumbs.Select(c => c.Target));
        Assert.Equal("Sunday · Today", HeaderProjection.Build(new NavState(AppView.Day, Today), Today, DayOfWeek.Monday).Subtitle);
    }

    [Fact]
    public void Plain_headers()
    {
        var inbox = HeaderProjection.Build(new NavState(AppView.Inbox, Today), Today, DayOfWeek.Monday);
        Assert.Equal("Inbox", inbox.Title);
        Assert.Equal("All tasks", Assert.Single(inbox.Crumbs).Label);
        Assert.Null(inbox.Crumbs[0].Target);
        var today = HeaderProjection.Build(new NavState(AppView.Today, new DateOnly(2026, 1, 1)), new DateOnly(2026, 11, 16), DayOfWeek.Monday);
        Assert.Equal("16 November", today.Title);
        Assert.Equal("Monday · Today", today.Crumbs[0].Label);
        var year = HeaderProjection.Build(new NavState(AppView.Year, Today), Today, DayOfWeek.Monday);
        Assert.Equal(("Calendar", "2026"), (year.Crumbs[0].Label, year.Title));
        var month = HeaderProjection.Build(new NavState(AppView.Month, Today), Today, DayOfWeek.Monday);
        Assert.Equal("November", month.Title);
        Assert.Equal(new NavTarget(AppView.Year, Today), month.Crumbs[0].Target);
    }

    [Theory]
    [InlineData(SettingsTab.Appearance, "Appearance")]
    [InlineData(SettingsTab.Calendar, "Calendar")]
    [InlineData(SettingsTab.Tasks, "Tasks")]
    [InlineData(SettingsTab.Windows, "Windows")]
    [InlineData(SettingsTab.Data, "Data")]
    [InlineData(SettingsTab.Shortcuts, "Keyboard shortcuts")]
    public void Settings_titles(SettingsTab tab, string title)
    {
        var h = HeaderProjection.Build(new NavState(AppView.Settings, Today, tab), Today, DayOfWeek.Monday);
        Assert.Equal(title, h.Title);
        Assert.Equal("Settings", Assert.Single(h.Crumbs).Label);
    }
}
