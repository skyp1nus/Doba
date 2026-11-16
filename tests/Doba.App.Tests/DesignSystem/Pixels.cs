using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Doba.App.Tests.DesignSystem;

/// <summary>Reads pixels of a headless rendered frame (Skia, Rgba8888 / Bgra8888).</summary>
internal static class Pixels
{
    /// <summary>Renders <paramref name="window"/> and returns the colour at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static Color At(Window window, int x, int y)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
        return At(frame, x, y);
    }

    public static Color At(WriteableBitmap bitmap, int x, int y)
    {
        using var fb = bitmap.Lock();
        var bytes = new byte[4];
        Marshal.Copy(fb.Address + (y * fb.RowBytes) + (x * 4), bytes, 0, 4);
        return fb.Format == PixelFormat.Bgra8888
            ? Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0])
            : Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2]);
    }

    /// <summary>True when every channel differs by at most <paramref name="tolerance"/>.</summary>
    public static bool Near(Color a, Color b, int tolerance = 3) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    public static Color Resource(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) switch
        {
            true when value is Color c => c,
            true when value is ISolidColorBrush b => b.Color,
            _ => throw new KeyNotFoundException(key),
        };
}

/// <summary>Runs the real dispatcher loop (timers included — <c>RunJobs</c> only drains queued jobs) until a condition holds.</summary>
internal static class Pump
{
    public static bool Until(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var frame = new Avalonia.Threading.DispatcherFrame();
        var poll = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(10), Avalonia.Threading.DispatcherPriority.Background, (_, _) =>
        {
            if (condition() || DateTime.UtcNow > deadline)
            {
                frame.Continue = false;
            }
        });
        poll.Start();
        Avalonia.Threading.Dispatcher.UIThread.PushFrame(frame);
        poll.Stop();
        return condition();
    }
}
