using Avalonia.Headless.XUnit;
using Doba.App.Tests.Support;
using Doba.Core.Demo;
using Doba.Core.Navigation;
using Doba.Core.Time;

namespace Doba.App.Tests.Shell;

/// <summary>The shell across midnight, and how often the sidebar re-projects (real MainViewModel on DemoData).</summary>
public sealed class ShellDayAndRefreshTests
{
    private static void NextDay(ShellFixture shell)
    {
        var clock = Assert.IsType<FixedTimeProvider>(shell.App.Clock);
        clock.Advance(TimeSpan.FromHours(2)); // 22:26 → 00:26 the next day
        shell.App.DayClock.Check();
        shell.Settle();
    }

    [AvaloniaTheory]
    [InlineData(AppView.Today)]
    [InlineData(AppView.Inbox)]
    public void After_midnight_the_sidebar_Calendar_opens_the_week_of_the_new_day(AppView section)
    {
        using var shell = ShellFixture.Open(section);
        var tomorrow = DemoData.Today.AddDays(1);

        NextDay(shell);

        Assert.Equal(section, shell.Vm.Nav.View);
        Assert.Equal(tomorrow, shell.Vm.Nav.Anchor);
        Assert.Equal(tomorrow, shell.Vm.Current!.State!.Anchor);
        shell.Vm.GoCalendar();
        shell.Settle();
        Assert.Equal(AppView.Week, shell.Vm.Nav.View);
        Assert.Equal(tomorrow, shell.Vm.Nav.Anchor);
    }

    [AvaloniaFact]
    public void A_calendar_the_owner_is_browsing_keeps_its_anchor_at_midnight()
    {
        var browsed = DemoData.Today.AddDays(-20);
        using var shell = ShellFixture.Open(AppView.Week, browsed);

        NextDay(shell);

        Assert.Equal(browsed, shell.Vm.Nav.Anchor);
    }

    [AvaloniaFact]
    public void Inbox_reached_from_a_browsed_calendar_keeps_that_anchor_at_midnight()
    {
        var browsed = DemoData.Today.AddDays(-20);
        using var shell = ShellFixture.Open(AppView.Week, browsed);
        shell.Navigate(AppView.Inbox);

        NextDay(shell);

        Assert.Equal(browsed, shell.Vm.Nav.Anchor);
    }

    [AvaloniaFact]
    public void One_change_projects_the_sidebar_once_and_navigation_not_at_all()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var sidebar = shell.Vm.Sidebar;
        var task = shell.App.Store.All.First(t => !t.Done && t.Date is not null);

        var before = sidebar.Projections;
        shell.App.Store.ToggleDone(task.Id);
        shell.Settle();
        Assert.Equal(before + 1, sidebar.Projections);

        before = sidebar.Projections;
        shell.Navigate(AppView.Inbox);
        shell.Navigate(AppView.Today);
        shell.Navigate(AppView.Month);
        shell.Vm.ToggleSidebar();
        shell.Vm.ToggleSidebar();
        shell.Navigate(AppView.Settings);
        shell.Vm.BackFromSettings();
        shell.Settle();
        Assert.Equal(before, sidebar.Projections);

        before = sidebar.Projections;
        shell.Vm.ToggleTagFilter("work");
        shell.Settle();
        Assert.Equal(before + 1, sidebar.Projections);

        before = sidebar.Projections;
        NextDay(shell);
        Assert.Equal(before + 1, sidebar.Projections);
    }
}
