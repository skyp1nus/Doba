using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;

namespace Doba.App.Tests;

/// <summary>Renders windows offscreen and writes PNGs to docs/screenshots/.</summary>
public static class Screenshots
{
    public static string Directory
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Doba.sln")))
            {
                dir = dir.Parent;
            }

            var root = dir?.FullName ?? throw new InvalidOperationException("Repository root (Doba.sln) not found.");
            var path = Path.Combine(root, "docs", "screenshots");
            System.IO.Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>Lays out, renders and saves the window; returns the file path.</summary>
    public static string Capture(Window window, string name)
    {
        window.UpdateLayout();
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var path = Path.Combine(Directory, name + ".png");
        frame.Save(path, PngBitmapEncoderOptions.Default);
        return path;
    }
}
