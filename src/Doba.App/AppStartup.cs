using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls.ApplicationLifetimes;
using Doba.App.Diagnostics;
using Doba.App.Platform;

namespace Doba.App;

/// <summary>Command-line flags of one launch (see <see cref="StartupFlags.Parse"/>).</summary>
/// <param name="Perf"><c>--perf</c>: log time-to-first-frame, its phases and memory to stdout.</param>
/// <param name="ExitAfterFirstFrame"><c>--exit-after-first-frame</c>: quit once the first frame is on screen.</param>
/// <param name="Demo"><c>--demo</c>: DemoData and the fixed demo clock; nothing read from or written to disk, no
/// autostart, no global hotkey.</param>
/// <param name="DataFolder"><c>--data &lt;folder&gt;</c>: this data folder instead of %LOCALAPPDATA%\Doba (the
/// registry is never touched).</param>
/// <param name="Minimized"><c>--minimized</c> (what "Launch at startup" passes): start hidden in the tray, or minimised
/// when the tray is off.</param>
/// <param name="SelfCheck"><c>--selfcheck</c>: run the Windows platform diagnostics on a demo window and exit.</param>
/// <param name="PerfIdleSeconds"><c>--perf-idle-seconds N</c> (1–600; implies <c>--perf</c>): after the first frame, stay
/// idle N seconds, log the working set and private bytes again, then exit.</param>
/// <param name="PerfScenario"><c>--perf-scenario week</c> (implies <c>--perf</c>): after the first frame (and the idle
/// sample), drive the Week view and log frame statistics, then exit.</param>
/// <param name="PerfRounds"><c>--perf-rounds N</c> (1–100): rounds of the scenario (0 = its default).</param>
internal sealed record StartupFlags(bool Perf, bool ExitAfterFirstFrame, bool Demo, string? DataFolder, bool Minimized, bool SelfCheck,
    int PerfIdleSeconds = 0, string? PerfScenario = null, int PerfRounds = 0)
{
    /// <summary>The one scenario <c>--perf-scenario</c> knows.</summary>
    public const string WeekScenario = "week";

    private const int MaxIdleSeconds = 600;
    private const int MaxRounds = 100;

    public static StartupFlags None { get; } = new(false, false, false, null, false, false);

    /// <summary>
    /// Flags are case-insensitive; unknown arguments are ignored; <c>--data</c>, <c>--perf-idle-seconds</c>,
    /// <c>--perf-scenario</c> and <c>--perf-rounds</c> take the next argument (an invalid value is ignored).
    /// </summary>
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
                case "--perf-idle-seconds" when i + 1 < args.Count:
                    if (TryCount(args[++i], MaxIdleSeconds, out var seconds))
                    {
                        flags = flags with { Perf = true, PerfIdleSeconds = seconds };
                    }

                    break;
                case "--perf-scenario" when i + 1 < args.Count:
                    if (string.Equals(args[++i], WeekScenario, StringComparison.OrdinalIgnoreCase))
                    {
                        flags = flags with { Perf = true, PerfScenario = WeekScenario };
                    }

                    break;
                case "--perf-rounds" when i + 1 < args.Count:
                    if (TryCount(args[++i], MaxRounds, out var rounds))
                    {
                        flags = flags with { PerfRounds = rounds };
                    }

                    break;
            }
        }

        return flags;
    }

    /// <summary>The app runs on %LOCALAPPDATA%\Doba: the only case in which the Run key is reconciled.</summary>
    public bool UsesDefaultDataLocation => !Demo && !SelfCheck && DataFolder is null;

    /// <summary>The app ends by itself once the perf work is done (first frame, idle sample, scenario).</summary>
    public bool ExitsAfterPerf => ExitAfterFirstFrame || PerfIdleSeconds > 0 || PerfScenario is not null;

    private static bool TryCount(string text, int max, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1 && value <= max;
}

/// <summary>Process-level startup facts: command-line flags, the startup stopwatch and the database preload.</summary>
internal static class AppStartup
{
    /// <summary>Started first thing in <c>Program.Main</c>; every <c>--perf</c> time is measured on it.</summary>
    public static Stopwatch Clock { get; } = new();

    public static StartupFlags Flags { get; private set; } = StartupFlags.None;

    /// <summary>The database being opened and loaded on a pool thread while Avalonia starts (null in demo mode).</summary>
    public static Task<LoadedStore>? Preload { get; private set; }

    /// <summary><c>--perf</c>: log time-to-first-frame (and working set) to stdout.</summary>
    public static bool PerfLogging => Flags.Perf;

    /// <summary><c>--exit-after-first-frame</c>: quit once the first frame is on screen (CI timing runs).</summary>
    public static bool ExitAfterFirstFrame => Flags.ExitAfterFirstFrame;

    /// <summary><c>--demo</c>: DemoData and the fixed demo clock, nothing read from or written to disk.</summary>
    public static bool Demo => Flags.Demo;

    /// <summary><c>--data &lt;folder&gt;</c>: use this data folder instead of %LOCALAPPDATA%\Doba.</summary>
    public static string? DataFolder => Flags.DataFolder;

    /// <summary>Called first in <c>Program.Main</c>: starts the clock, parses the flags and starts the database preload.</summary>
    public static void Begin(string[] args)
    {
        Clock.Start();
        Flags = StartupFlags.Parse(args);
        if (Flags.Perf)
        {
            PerfProbe.Enable();
        }

        Preload = AppServices.StartPreload(Flags);
    }

    /// <summary>Takes the preloaded database (once; later calls get null and open it themselves).</summary>
    public static Task<LoadedStore>? TakePreload()
    {
        var preload = Preload;
        Preload = null;
        return preload;
    }

    /// <summary>Hooks the main window's first rendered frame for the perf flags (see <see cref="PerfSession"/>).</summary>
    public static void WatchFirstFrame(MainWindow window, IClassicDesktopStyleApplicationLifetime lifetime) =>
        PerfSession.Attach(window, lifetime, Flags);
}
