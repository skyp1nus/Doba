using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Doba.App.Screens.Today;
using Doba.App.Tests.Support;
using Doba.App.Theme;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Time;

namespace Doba.App.Tests.Shell;

/// <summary>The page header renders HeaderProjection; settings, filter and clock changes apply live.</summary>
public class ShellHeaderTests
{
    private static readonly DateOnly Today = DemoData.Today;

    public static TheoryData<AppView, SettingsTab> Views => new()
    {
        { AppView.Inbox, SettingsTab.Appearance },
        { AppView.Today, SettingsTab.Appearance },
        { AppView.Year, SettingsTab.Appearance },
        { AppView.Month, SettingsTab.Appearance },
        { AppView.Week, SettingsTab.Appearance },
        { AppView.Day, SettingsTab.Appearance },
        { AppView.Settings, SettingsTab.Appearance },
        { AppView.Settings, SettingsTab.Shortcuts },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Views))]
    public void Header_shows_the_projection(AppView view, SettingsTab tab)
    {
        using var shell = ShellFixture.Open(view, tab: tab);
        var expected = HeaderProjection.Build(shell.Vm.Nav, Today, DayOfWeek.Monday);
        var header = shell.Vm.Header;
        AssertModel(expected, header.Model);
        Assert.Equal(expected.Title, header.Title);
        Assert.Equal(expected.Subtitle, header.Subtitle);
        Assert.Equal(expected.Crumbs.Select(c => c.Label), header.Crumbs.Select(c => c.Label));
        Assert.Equal(expected.Crumbs.Select(c => c.Target is not null), header.Crumbs.Select(c => c.IsLink));

        // What the window draws: upper-case crumbs with "/" between them, title + subtitle.
        var ui = shell.Window.Header;
        var crumbTexts = ui.CrumbList.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Text is { Length: > 0 }).Select(t => t.Text!).ToList();
        var expectedTexts = expected.Crumbs.SelectMany((c, i) => i == 0
            ? new[] { c.Label.ToUpperInvariant() }
            : ["/", c.Label.ToUpperInvariant()]).ToList();
        Assert.Equal(expectedTexts, crumbTexts);
        Assert.Equal(expected.Title + " ", string.Concat(ui.HeadingTitle.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)));
        Assert.Equal(expected.Subtitle, ui.HeadingSubtitle.Text);

        // Calendar-only controls, the zoom switch's segment, New task and the sidebar toggle.
        var calendar = shell.Vm.Nav.IsCalendar;
        Assert.Equal(calendar, ui.CalendarActions.IsEffectivelyVisible);
        Assert.Equal(calendar, ui.TodayButton.IsEffectivelyVisible);
        Assert.Equal(calendar, ui.PreviousButton.IsEffectivelyVisible && ui.NextButton.IsEffectivelyVisible);
        Assert.Equal(calendar ? view - AppView.Year : -1, ui.ZoomSwitch.SelectedIndex);
        Assert.Equal(view != AppView.Settings, ui.NewTaskButton.IsEffectivelyVisible);
        Assert.Equal(view != AppView.Settings, ui.SidebarToggle.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Header_controls_navigate()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var ui = shell.Window.Header;

        shell.Click(ui.NextButton);
        Assert.Equal(Today.AddDays(7), shell.Vm.Nav.Anchor);
        shell.Click(ui.PreviousButton);
        shell.Click(ui.PreviousButton);
        Assert.Equal(Today.AddDays(-7), shell.Vm.Nav.Anchor);
        shell.Click(ui.TodayButton);
        Assert.Equal(new NavState(AppView.Week, Today), shell.Vm.Nav);

        // Segments: Month, then the "2026" crumb (Year), then the zoom switch's Day.
        var segments = ui.ZoomSwitch.GetRealizedContainers().Cast<Control>().ToList();
        shell.Click(segments[1]);
        Assert.Equal(AppView.Month, shell.Vm.Nav.View);
        Assert.Equal(1, ui.ZoomSwitch.SelectedIndex);
        var yearCrumb = ui.CrumbList.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible);
        shell.Click(yearCrumb);
        Assert.Equal(AppView.Year, shell.Vm.Nav.View);
        Assert.Equal(0, ui.ZoomSwitch.SelectedIndex);
        shell.Click(segments[3]);
        Assert.Equal(new NavState(AppView.Day, Today), shell.Vm.Nav);

        shell.Click(ui.SidebarToggle);
        Assert.True(shell.Vm.IsSidebarCollapsed);
        Assert.Equal("Expand sidebar", shell.Vm.Header.SidebarToggleLabel);
        shell.Click(ui.SidebarToggle);
        Assert.False(shell.Vm.IsSidebarCollapsed);

        shell.Click(ui.NewTaskButton);
        Assert.IsType<Doba.App.Dialogs.TaskDialogViewModel>(shell.Vm.Overlay);
    }

    [AvaloniaFact]
    public void Setting_the_same_zoom_index_does_not_navigate()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var count = 0;
        shell.Vm.Navigated += (_, _) => count++;
        shell.Vm.Header.ZoomIndex = 2;
        shell.Vm.Shift(1);
        Assert.Equal(1, count);
        Assert.Equal(2, shell.Vm.Header.ZoomIndex);
    }

    [AvaloniaFact]
    public void Filter_change_refreshes_the_active_screen_and_the_sidebar()
    {
        using var shell = ShellFixture.Open(AppView.Today);
        var workTag = Assert.Single(shell.Vm.Sidebar.Tags, t => t.Name == "work");
        Assert.False(workTag.IsActive);
        var rows = shell.Screen<TodayViewModel>().Groups.SelectMany(g => g.Items).ToList();
        Assert.All(rows, r => Assert.False(r.Dimmed));

        shell.Vm.ToggleTagFilter("work");
        Assert.Equal("work", shell.Vm.FilterTag);
        Assert.Equal("work", shell.Vm.Screens.FilterTag());
        Assert.True(workTag.IsActive);
        Assert.Contains(rows, r => r.Dimmed);
        Assert.All(rows.Where(r => r.Dimmed), r => Assert.NotEqual("#work", r.TagLabel));

        shell.Vm.ToggleTagFilter("work");
        Assert.Null(shell.Vm.FilterTag);
        Assert.False(workTag.IsActive);
        Assert.All(rows, r => Assert.False(r.Dimmed));
    }

    [AvaloniaFact]
    public void Store_changes_refresh_the_sidebar_and_add_tag_normalises()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        Assert.DoesNotContain(shell.Vm.Sidebar.Tags, t => t.Name == "deep-work");
        Assert.Equal("deep-work", shell.Vm.AddTag("# Deep Work"));
        Assert.Null(shell.Vm.AddTag("work"));
        Assert.Null(shell.Vm.AddTag("  "));
        Assert.Contains("deep-work", shell.App.Store.Tags);
        Assert.Single(shell.Vm.Sidebar.Tags, t => t.Name == "deep-work");
    }

    [AvaloniaFact]
    public void Accent_density_transparency_and_week_start_apply_live()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var app = Application.Current!;

        shell.App.Settings.Update(s => s with { Accent = Accent.Sand });
        Assert.Equal(AccentPalette.ColorOf(Accent.Sand), Brush(app, ThemeService.AccentBrushKey).Color);

        shell.App.Settings.Update(s => s with { Density = Density.Compact });
        var (key, _, compact) = ThemeService.DensityTable[0];
        Assert.Equal(compact, app.Resources[key]);

        shell.App.Settings.Update(s => s with { WindowTransparency = 40 });
        Assert.Equal((byte)Math.Round(0.40 * 255), Brush(app, ThemeService.ChromeBrushKey).Color.A);

        Assert.Equal("9 – 15 Nov", shell.Vm.Header.Subtitle);
        shell.App.Settings.Update(s => s with { WeekStart = DayOfWeek.Sunday });
        AssertModel(HeaderProjection.Build(shell.Vm.Nav, Today, DayOfWeek.Sunday), shell.Vm.Header.Model);
        Assert.Equal("15 – 21 Nov", shell.Vm.Header.Subtitle);
    }

    [AvaloniaFact]
    public void Header_follows_the_day_change()
    {
        using var shell = ShellFixture.Open(AppView.Today);
        Assert.Equal("15 November", shell.Vm.Header.Title);
        ((FixedTimeProvider)shell.App.Clock).Advance(TimeSpan.FromHours(2));
        shell.App.DayClock.Check();
        Assert.Equal("16 November", shell.Vm.Header.Title);
        Assert.Equal("Monday · Today", shell.Vm.Header.Crumbs[0].Label);
    }

    private static void AssertModel(HeaderModel expected, HeaderModel? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Subtitle, actual.Subtitle);
        Assert.Equal(expected.Crumbs, actual.Crumbs);
    }

    private static SolidColorBrush Brush(Application app, string key) =>
        app.TryGetResource(key, app.ActualThemeVariant, out var v) && v is SolidColorBrush b ? b : throw new InvalidOperationException(key);
}
