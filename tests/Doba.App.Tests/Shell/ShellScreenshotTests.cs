using Avalonia.Headless.XUnit;
using Doba.App.Tests.Support;
using Doba.Core.Model;
using Doba.Core.Navigation;

namespace Doba.App.Tests.Shell;

/// <summary>
/// Full-window screenshots at 1600×1000 with DemoData, one per reference image in docs/design/screens/ (the state each
/// reference shows is listed in tools/design-render/jobs.js). Month, Day and the sidebars show their package stubs
/// until those packages land.
/// </summary>
public class ShellScreenshotTests
{
    public static TheoryData<string, AppView, SettingsTab> Screens => new()
    {
        { "inbox", AppView.Inbox, SettingsTab.Appearance },
        { "today", AppView.Today, SettingsTab.Appearance },
        { "year", AppView.Year, SettingsTab.Appearance },
        { "month", AppView.Month, SettingsTab.Appearance },
        { "week", AppView.Week, SettingsTab.Appearance },
        { "day", AppView.Day, SettingsTab.Appearance },
        { "settings-appearance", AppView.Settings, SettingsTab.Appearance },
        { "settings-calendar", AppView.Settings, SettingsTab.Calendar },
        { "settings-tasks", AppView.Settings, SettingsTab.Tasks },
        { "settings-windows", AppView.Settings, SettingsTab.Windows },
        { "settings-data", AppView.Settings, SettingsTab.Data },
        { "settings-shortcuts", AppView.Settings, SettingsTab.Shortcuts },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Screens))]
    public void Screen_renders(string name, AppView view, SettingsTab tab)
    {
        using var shell = ShellFixture.Open(view, tab: tab);
        AssertPng(shell.Capture(name));
    }

    [AvaloniaFact]
    public void Sidebar_collapsed_renders()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        shell.Vm.ToggleSidebar();
        AssertPng(shell.Capture("sidebar-collapsed"));
    }

    [AvaloniaFact]
    public void Density_compact_renders()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        shell.App.Settings.Update(s => s with { Density = Density.Compact });
        AssertPng(shell.Capture("density-compact"));
    }

    private static void AssertPng(string path) => Assert.True(new FileInfo(path).Length > 10_000, path);
}
