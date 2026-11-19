using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;

namespace Doba.App.Tests;

/// <summary>
/// Renders windows offscreen and writes PNGs. Writing the tracked images in docs/screenshots/ is opt-in
/// (<c>DOBA_SCREENSHOTS=1</c>); by default PNGs go to the git-ignored artifacts/screenshots/, so test runs in parallel
/// worktrees never rewrite tracked binaries. Tests that read a PNG back must use the path <see cref="Capture"/> returns.
/// </summary>
public static class Screenshots
{
    /// <summary>Environment variable that, when "1", makes <see cref="Directory"/> point at docs/screenshots/.</summary>
    public const string OptInVariable = "DOBA_SCREENSHOTS";

    /// <summary>True when this run refreshes the committed screenshots (<c>DOBA_SCREENSHOTS=1</c>).</summary>
    public static bool WritesDocs => Environment.GetEnvironmentVariable(OptInVariable) == "1";

    /// <summary>docs/screenshots/ when <see cref="WritesDocs"/>, else artifacts/screenshots/ (created if missing).</summary>
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
            var path = WritesDocs
                ? Path.Combine(root, "docs", "screenshots")
                : Path.Combine(root, "artifacts", "screenshots");
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
