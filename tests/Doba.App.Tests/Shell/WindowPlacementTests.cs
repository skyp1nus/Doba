using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Doba.App.Shell;
using Doba.App.Tests.Support;

namespace Doba.App.Tests.Shell;

/// <summary>The opening size fits the screen's working area (<see cref="WindowPlacement"/>).</summary>
public sealed class WindowPlacementTests
{
    private static readonly Size Design = new(1600, 1000);
    private static readonly Size Minimum = new(1100, 700);

    [Fact]
    public void A_large_screen_keeps_the_design_frame() =>
        Assert.Equal(Design, WindowPlacement.Fit(Design, Minimum, new PixelRect(0, 0, 2560, 1392), 1.0));

    [Fact]
    public void A_1080p_laptop_at_125_percent_gets_a_window_that_fits_inside_its_working_area()
    {
        // 1920×1032 device px of working area (48 px taskbar) at 125 % = 1536×825.6 logical px.
        var size = WindowPlacement.Fit(Design, Minimum, new PixelRect(0, 0, 1920, 1032), 1.25);

        Assert.Equal(new Size(1536 - 2 * WindowPlacement.ScreenMargin, 825 - 2 * WindowPlacement.ScreenMargin), size);
        Assert.True(size.Width * 1.25 <= 1920 && size.Height * 1.25 <= 1032);
    }

    [Fact]
    public void A_small_screen_never_goes_below_the_minimum_size()
    {
        var size = WindowPlacement.Fit(Design, Minimum, new PixelRect(0, 0, 1366, 728), 1.0);

        Assert.Equal(1366 - 2 * WindowPlacement.ScreenMargin, size.Width);
        Assert.Equal(Minimum.Height, size.Height);
    }

    [Fact]
    public void An_unknown_screen_changes_nothing() =>
        Assert.Equal(Design, WindowPlacement.Fit(Design, Minimum, default, 0));

    [AvaloniaFact]
    public void Fitting_the_main_window_centres_it_and_keeps_it_within_its_limits()
    {
        using var shell = ShellFixture.Open();
        var window = shell.Window;

        window.FitToScreen();

        Assert.InRange(window.Width, window.MinWidth, 1600);
        Assert.InRange(window.Height, window.MinHeight, 1000);
        if (window.Screens.Primary is not null)
        {
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
        }
    }
}
