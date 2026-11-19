using Avalonia;

namespace Doba.App.Shell;

/// <summary>
/// The window's opening size: the design's 1600×1000 frame where the screen has room for it, otherwise the screen's
/// working area (taskbar excluded) less a margin, never below the minimum size the layout is built for (1100×700).
/// On a 1920×1080 display at 125 % the working area is about 1536×824 logical px, so the window opens at 1504×792
/// instead of running its caption buttons off the right edge.
/// </summary>
public static class WindowPlacement
{
    /// <summary>Logical px kept free between the window and each edge of the working area.</summary>
    public const double ScreenMargin = 16;

    /// <summary>The opening size for <paramref name="desired"/> on a screen whose working area is
    /// <paramref name="workingArea"/> device pixels at <paramref name="scaling"/>.</summary>
    public static Size Fit(Size desired, Size minimum, PixelRect workingArea, double scaling)
    {
        if (scaling <= 0 || workingArea.Width <= 0 || workingArea.Height <= 0)
        {
            return desired;
        }

        var room = new Size(
            Math.Floor(workingArea.Width / scaling) - 2 * ScreenMargin,
            Math.Floor(workingArea.Height / scaling) - 2 * ScreenMargin);
        return new Size(
            Math.Max(minimum.Width, Math.Min(desired.Width, room.Width)),
            Math.Max(minimum.Height, Math.Min(desired.Height, room.Height)));
    }
}
