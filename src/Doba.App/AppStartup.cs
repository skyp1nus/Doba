using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Doba.App.Platform;

namespace Doba.App;

/// <summary>Command-line flags of one launch (see <see cref="StartupFlags.Parse"/>).</summary>
/// <param name="Perf"><c>--perf</c>: log time-to-first-frame (and working set) to stdout.</param>
/// <param name="ExitAfterFirstFrame"><c>--exit-after-first-frame</c>: quit once the first frame is on screen.</param>
/// <param name="Demo"><c>--demo</c>: DemoData and the fixed demo clock; nothing read from or written to disk, no
/// autostart, no global hotkey.</param>
/// <param name="DataFolder"><c>--data &lt;folder&gt;</c>: this data folder instead of %LOCALAPPDATA%\Doba (the
/// registry is never touched).</param>
/// <param name="Minimized"><c>--minimized</c> (what "Launch at startup" passes): start hidden in the tray, or minimised
/// when the tray is off.</param>
/// <param name="SelfCheck"><c>--selfcheck</c>: run the Windows platform diagnostics on a demo window and exit.</param>
internal sealed record StartupFlags(bool Perf, bool ExitAfterFirstFrame, bool Demo, string? DataFolder, bool Minimized, bool SelfCheck)
{
    public static StartupFlags None { get; } = new(false, false, false, null, false, false);

    /// <summary>Flags are case-insensitive; unknown arguments are ignored; <c>--data</c> takes the next argument.</summary>
    public static StartupFlags Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var flags = None;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--perf": flags = flags with { Perf = true }; break;
                case "--exit-after-first-frame": flags = flags with { ExitAfterFirstFrame = true }; break;
                case "--demo": flags = flags with { Demo = true }; break;
                case "--data" when i + 1 < args.Count: flags = flags with { DataFolder = args[++i] }; break;
                case AutostartCommand.MinimizedFlag: flags = flags with { Minimized = true }; break;
                case "--selfcheck": flags = flags with { SelfCheck = true }; break;
            }
        }

        return flags;
    }

    /// <summary>The app runs on %LOCALAPPDATA%\Doba: the only case in which the Run key is reconciled.</summary>
    public bool UsesDefaultDataLocation => !Demo && !SelfCheck && DataFolder is null;
}

/// <summary>Process-level startup facts: command-line flags and the time-to-first-frame stopwatch.</summary>
internal static class AppStartup
{
    public static Stopwatch Clock { get; } = new();

    public static StartupFlags Flags { get; private set; } = StartupFlags.None;

    /// <summary><c>--perf</c>: log time-to-first-frame (and working set) to stdout.</summary>
    public static bool PerfLogging => Flags.Perf;

    /// <summary><c>--exit-after-first-frame</c>: quit once the first frame is on screen (CI timing runs).</summary>
    public static bool ExitAfterFirstFrame => Flags.ExitAfterFirstFrame;

    /// <summary><c>--demo</c>: DemoData and the fixed demo clock, nothing read from or written to disk.</summary>
    public static bool Demo => Flags.Demo;

    /// <summary><c>--data &lt;folder&gt;</c>: use this data folder instead of %LOCALAPPDATA%\Doba.</summary>
    public static string? DataFolder => Flags.DataFolder;

    public static void Begin(string[] args)
    {
        Clock.Start();
        Flags = StartupFlags.Parse(args);
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
