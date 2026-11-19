using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Doba.App.Tests.DesignSystem;
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
    public void Panel_hairline_chevrons_and_sidebar_counts_sit_on_the_reference_pixels()
    {
        // Measured in docs/design/screens/week.png and sidebar-collapsed.png (ink = luminance above 110).
        using var shell = ShellFixture.Open(AppView.Week);
        using (var frame = Frame(shell.Window))
        {
            // The main panel's translucent hairline lies over the panel's own fill (CSS background-clip: border-box).
            var edge = Pixels.Resource("Brush.Overlay.Edge");
            var fill = Pixels.Resource("Brush.Bg.Main");
            var hairline = Over(edge, fill);
            Assert.True(Pixels.Near(hairline, Pixels.At(frame, 232, 300), 1), $"left hairline {Pixels.At(frame, 232, 300)}");
            Assert.True(Pixels.Near(hairline, Pixels.At(frame, 600, 40), 1), $"top hairline {Pixels.At(frame, 600, 40)}");

            Assert.Equal((1398, 1402, 89, 97), Ink(frame, 1386, 80, 1415, 102));          // ‹ in its 30 px button
            Assert.Equal((1434, 1438, 89, 97), Ink(frame, 1422, 80, 1451, 102));          // ›
            Assert.Equal((200, 210, 196, 203), Ink(frame, 190, 188, 226, 212));           // "work 12"
            Assert.Equal((206, 210, 258, 265), Ink(frame, 190, 250, 226, 274));           // "learning 6"
        }

        shell.Vm.ToggleSidebar();
        shell.Settle();
        using (var frame = Frame(shell.Window))
        {
            Assert.True(Pixels.Near(Over(Pixels.Resource("Brush.Overlay.Edge"), Pixels.Resource("Brush.Bg.Main")),
                Pixels.At(frame, 56, 300), 1), $"rail hairline {Pixels.At(frame, 56, 300)}");
            Assert.Equal((197, 203), Rows(Ink(frame, 21, 194, 34, 207)));                 // W
            Assert.Equal((25, 30, 228, 234), Ink(frame, 21, 225, 34, 238));               // P
            Assert.Equal((259, 265), Rows(Ink(frame, 21, 256, 34, 269)));                 // L
        }

        static (int, int) Rows((int, int, int Top, int Bottom) box) => (box.Top, box.Bottom);
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

    private static WriteableBitmap Frame(Window window)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
    }

    /// <summary><paramref name="top"/> (with its alpha) composited over the opaque <paramref name="under"/>.</summary>
    private static Color Over(Color top, Color under)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round((t * a) + (u * (1 - a)));
        return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }

    /// <summary>The bounding box (left, right, top, bottom) of the pixels brighter than luminance 110 in the area.</summary>
    private static (int Left, int Right, int Top, int Bottom) Ink(WriteableBitmap frame, int x0, int y0, int x1, int y1)
    {
        int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var c = Pixels.At(frame, x, y);
                if ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B) > 110)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return (left, right, top, bottom);
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
