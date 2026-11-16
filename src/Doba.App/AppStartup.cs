using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Doba.App;

/// <summary>Process-level startup facts: command-line flags and the time-to-first-frame stopwatch.</summary>
internal static class AppStartup
{
    public static Stopwatch Clock { get; } = new();

    /// <summary><c>--perf</c>: log time-to-first-frame (and working set) to stdout.</summary>
    public static bool PerfLogging { get; private set; }

    /// <summary><c>--exit-after-first-frame</c>: quit once the first frame is on screen (CI timing runs).</summary>
    public static bool ExitAfterFirstFrame { get; private set; }

    /// <summary><c>--demo</c>: DemoData and the fixed demo clock, nothing read from or written to disk.</summary>
    public static bool Demo { get; private set; }

    /// <summary><c>--data &lt;folder&gt;</c>: use this data folder instead of %LOCALAPPDATA%\Doba.</summary>
    public static string? DataFolder { get; private set; }

    public static void Begin(string[] args)
    {
        Clock.Start();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--perf": PerfLogging = true; break;
                case "--exit-after-first-frame": ExitAfterFirstFrame = true; break;
                case "--demo": Demo = true; break;
                case "--data" when i + 1 < args.Length: DataFolder = args[++i]; break;
            }
        }
    }

    /// <summary>Hooks the main window's first rendered frame for <c>--perf</c> / <c>--exit-after-first-frame</c>.</summary>
    public static void WatchFirstFrame(Window window, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        if (!PerfLogging && !ExitAfterFirstFrame)
        {
            return;
        }

        window.Opened += (_, _) => window.RequestAnimationFrame(_ =>
        {
            var ms = Clock.Elapsed.TotalMilliseconds;
            if (PerfLogging)
            {
                using var p = Process.GetCurrentProcess();
                Console.WriteLine(FormattableString.Invariant($"doba-perf time-to-first-frame-ms={ms:F0} working-set-mb={p.WorkingSet64 / (1024.0 * 1024.0):F1}"));
            }

            if (ExitAfterFirstFrame)
            {
                lifetime.Shutdown();
            }
        });
    }
}
