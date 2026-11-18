using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Doba.App.Tests.Support;
using Doba.Core.Navigation;

namespace Doba.App.Tests.Shell;

/// <summary>Window chrome: caption roles, sizes from the design, and a 1100×700 window that still fits.</summary>
public class ShellChromeTests
{
    [AvaloniaFact]
    public void Caption_buttons_and_drag_area_carry_their_window_roles()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var bar = shell.Window.TitleBar;
        Assert.True(shell.Window.ExtendClientAreaToDecorationsHint);
        Assert.Equal(40, shell.Window.ExtendClientAreaTitleBarHeightHint);
        Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(bar.DragArea));
        Assert.Equal(WindowDecorationsElementRole.MinimizeButton, WindowDecorationProperties.GetElementRole(bar.MinimizeButton));
        Assert.Equal(WindowDecorationsElementRole.MaximizeButton, WindowDecorationProperties.GetElementRole(bar.MaximizeButton));
        Assert.Equal(WindowDecorationsElementRole.CloseButton, WindowDecorationProperties.GetElementRole(bar.CloseButton));
        Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(bar.SearchButton));
        Assert.False(bar.CloseButton.Focusable);
    }

    [AvaloniaFact]
    public void Chrome_has_the_design_sizes()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var w = shell.Window;
        Assert.Equal(40, w.TitleBar.Bounds.Height);
        Assert.Equal(new Rect(1600 - 46, 0, 46, 40), Bounds(w.TitleBar.CloseButton, w));
        Assert.Equal(new Rect(1600 - 92, 0, 46, 40), Bounds(w.TitleBar.MaximizeButton, w));
        Assert.Equal(new Rect(1600 - 138, 0, 46, 40), Bounds(w.TitleBar.MinimizeButton, w));
        Assert.Equal(new Rect(614, 6, 380, 28), Bounds(w.TitleBar.SearchButton, w));     // as in the reference: x 614
        Assert.Equal(new Rect(232, 40, 1368, 960), Bounds(w.MainPanel, w));
        Assert.Equal(232, w.SidebarColumn.Bounds.Width);

        // Page header: 20 px top padding, crumbs 16 high + 4 + 27.6 title line, 30 px buttons on the bottom line,
        // 18 px bottom padding and a 1 px rule (bottom ≈ 127.6, the reference's rule is at y 127).
        var header = w.Header;
        var newTask = Bounds(header.NewTaskButton, w);
        Assert.Equal(1600 - 24, newTask.Right);
        Assert.Equal(30, newTask.Height);
        Assert.Equal(78.6, newTask.Y, 0.5);
        var toggle = Bounds(header.SidebarToggle, w);
        Assert.Equal(new Size(30, 30), toggle.Size);
        Assert.Equal(255, toggle.X);
        Assert.Equal(newTask.Y, toggle.Y, 0.01);
        Assert.Equal(new Point(261 + 36, 61), Bounds(header.CrumbList, w).TopLeft);
        Assert.Equal(16, header.CrumbList.Bounds.Height);
        Assert.Equal(127.6, Bounds(header, w).Bottom, 0.5);

        // Content area = main panel minus header and the body padding 16/24/20/28.
        var content = Bounds(w.ScreenHost, w);
        Assert.Equal(233 + 28, content.X);
        Assert.Equal(1600 - 24, content.Right);
        Assert.Equal(1000 - 20, content.Bottom);
    }

    [AvaloniaFact]
    public void Header_actions_sit_where_the_reference_draws_them()
    {
        // Edges measured in docs/design/screens/week.png (Chromium keeps fractional text widths; Avalonia rounds them
        // up, which Space.Shell.Today / Space.Shell.NewTaskKbd absorb).
        using var shell = ShellFixture.Open(AppView.Week);
        var w = shell.Window;
        var header = w.Header;
        Assert.Equal(new Rect(1477, 0, 99, 30), Horizontal(header.NewTaskButton, w));
        Assert.Equal(new Rect(1554, 0, 16, 18), Horizontal(header.NewTaskKey, w));
        Assert.Equal(new Rect(1422, 0, 30, 30), Horizontal(header.NextButton, w));
        Assert.Equal(new Rect(1386, 0, 30, 30), Horizontal(header.PreviousButton, w));
        Assert.Equal(new Rect(1320, 0, 60, 30), Horizontal(header.TodayButton, w));
        Assert.Equal(1041, Bounds(header.ZoomSwitch, w).X); // its Week pill (index 2) at 1041 + 128 = 1169

        // Every label in the row shares one text line: Today's label is not a pixel lower than New task's.
        static TextBlock Label(Control button, string text) =>
            button.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == text);
        var today = Bounds(Label(header.TodayButton, "Today"), w);
        var newTask = Bounds(Label(header.NewTaskButton, "New task"), w);
        Assert.Equal(newTask.Y, today.Y);
        Assert.Equal(newTask.Height, today.Height);
    }

    [AvaloniaFact]
    public void Collapsed_sidebar_is_the_56px_rail()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        shell.Vm.ToggleSidebar();
        shell.Settle();
        Assert.True(shell.Vm.IsRail);
        Assert.Equal(56, shell.Window.SidebarColumn.Bounds.Width);
        Assert.Equal(56, Bounds(shell.Window.MainPanel, shell.Window).X);
    }

    [AvaloniaFact]
    public void Minimum_size_window_lays_out_without_clipping()
    {
        using var shell = ShellFixture.Open(AppView.Week, width: 1100, height: 700);
        var path = shell.Capture("week-1100x700");
        Assert.True(new FileInfo(path).Length > 10_000);

        var w = shell.Window;
        var window = new Rect(0, 0, 1100, 700);
        Control[] chrome =
        [
            w.TitleBar.SearchButton, w.TitleBar.MinimizeButton, w.TitleBar.MaximizeButton, w.TitleBar.CloseButton,
            w.Header.SidebarToggle, w.Header.CrumbList, w.Header.Heading, w.Header.ZoomSwitch, w.Header.TodayButton,
            w.Header.PreviousButton, w.Header.NextButton, w.Header.NewTaskButton, w.SidebarColumn, w.MainPanel, w.ScreenHost,
        ];
        foreach (var c in chrome)
        {
            Assert.True(c.IsEffectivelyVisible, c.Name);
            var r = Bounds(c, w);
            Assert.True(window.Contains(r), $"{c.Name ?? c.GetType().Name} {r} is outside the window");
            Assert.True(r.Width > 0 && r.Height > 0, c.Name);
        }

        // Header pieces do not overlap: title block left of the zoom switch, buttons in order.
        Assert.True(Bounds(w.Header.Heading, w).Right <= Bounds(w.Header.ZoomSwitch, w).Left);
        Assert.True(Bounds(w.Header.NextButton, w).Right < Bounds(w.Header.NewTaskButton, w).Left);
        Assert.Equal(1100 - 24, Bounds(w.Header.NewTaskButton, w).Right);

        // Every visible control of the chrome (title bar + header) is inside the window.
        foreach (var v in w.TitleBar.GetVisualDescendants().Concat(w.Header.GetVisualDescendants()).OfType<Control>()
                     .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0))
        {
            Assert.True(window.Contains(Bounds(v, w)), $"{v.GetType().Name} {Bounds(v, w)} is outside the window");
        }
    }

    [AvaloniaFact]
    public void Caption_buttons_hover_and_work()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var bar = shell.Window.TitleBar;
        var centre = Bounds(bar.MaximizeButton, shell.Window).Center;
        shell.Window.MouseMove(centre);
        shell.Settle();
        Assert.True(bar.MaximizeButton.IsPointerOver);

        shell.Click(bar.MaximizeButton);
        Assert.Equal(WindowState.Maximized, shell.Window.WindowState);
        shell.Click(bar.MaximizeButton);
        Assert.Equal(WindowState.Normal, shell.Window.WindowState);
    }

    private static Rect Bounds(Visual v, Visual root)
    {
        var topLeft = v.TranslatePoint(default, root) ?? throw new InvalidOperationException("Not in the tree.");
        return new Rect(topLeft, v.Bounds.Size);
    }

    /// <summary>X and size only (the header's vertical position is checked in <see cref="Chrome_has_the_design_sizes"/>).</summary>
    private static Rect Horizontal(Visual v, Visual root)
    {
        var b = Bounds(v, root);
        return new Rect(b.X, 0, b.Width, b.Height);
    }
}
