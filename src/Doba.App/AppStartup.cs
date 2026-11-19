using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia.Controls.ApplicationLifetimes;
using Doba.App.Diagnostics;
using Doba.App.Platform;
using Doba.App.Services;
using Doba.Storage;

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

    /// <summary>One line naming every flag (printed with a parse error).</summary>
    public const string Usage =
        "usage: Doba [--minimized] [--demo | --data <folder> | --selfcheck] [--perf] [--exit-after-first-frame] " +
        "[--perf-idle-seconds 1-600] [--perf-scenario week] [--perf-rounds 1-100]";

    /// <summary>
    /// Like <see cref="TryParse"/>, but throws <see cref="ArgumentException"/> with the error text for a command line
    /// that is not valid.
    /// </summary>
    public static StartupFlags Parse(IReadOnlyList<string> args) =>
        TryParse(args, out var flags, out var error) ? flags : throw new ArgumentException(error, nameof(args));

    /// <summary>
    /// Flags are case-insensitive. The parse is strict, because a launch whose flags were not understood would run on
    /// the real data folder and the real Run key (real launches pass nothing, or <c>--minimized</c> from the Run key):
    /// any unknown argument (<c>--dmeo</c>, <c>/demo</c>, <c>--data=x</c>, a stray word), a value flag with no value,
    /// an empty <c>--data</c> folder, a value that is itself a flag, and an out-of-range number are errors.
    /// <c>--data</c>, <c>--perf-idle-seconds</c>, <c>--perf-scenario</c> and <c>--perf-rounds</c> take the next argument.
    /// </summary>
    /// <returns>False with <paramref name="error"/> set (and <paramref name="flags"/> = <see cref="None"/>) when the
    /// command line is not valid.</returns>
    public static bool TryParse(IReadOnlyList<string> args, out StartupFlags flags, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        flags = None;
        error = null;
        var parsed = None;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--perf": parsed = parsed with { Perf = true }; break;
                case "--exit-after-first-frame": parsed = parsed with { ExitAfterFirstFrame = true }; break;
                case "--demo": parsed = parsed with { Demo = true }; break;
                case AutostartCommand.MinimizedFlag: parsed = parsed with { Minimized = true }; break;
                case "--selfcheck": parsed = parsed with { SelfCheck = true }; break;
                case "--data":
                    if (!TryValue(args, ref i, out var folder, out error))
                    {
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(folder))
                    {
                        error = "--data needs a folder (it was empty)";
                        return false;
                    }

                    parsed = parsed with { DataFolder = folder };
                    break;
                case "--perf-idle-seconds":
                    if (!TryValue(args, ref i, out var idle, out error))
                    {
                        return false;
                    }

                    if (!TryCount(idle, MaxIdleSeconds, out var seconds))
                    {
                        error = $"--perf-idle-seconds needs a whole number from 1 to {MaxIdleSeconds} (got '{idle}')";
                        return false;
                    }

                    parsed = parsed with { Perf = true, PerfIdleSeconds = seconds };
                    break;
                case "--perf-scenario":
                    if (!TryValue(args, ref i, out var scenario, out error))
                    {
                        return false;
                    }

                    if (!string.Equals(scenario, WeekScenario, StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"--perf-scenario knows only '{WeekScenario}' (got '{scenario}')";
                        return false;
                    }

                    parsed = parsed with { Perf = true, PerfScenario = WeekScenario };
                    break;
                case "--perf-rounds":
                    if (!TryValue(args, ref i, out var count, out error))
                    {
                        return false;
                    }

                    if (!TryCount(count, MaxRounds, out var rounds))
                    {
                        error = $"--perf-rounds needs a whole number from 1 to {MaxRounds} (got '{count}')";
                        return false;
                    }

                    parsed = parsed with { PerfRounds = rounds };
                    break;
                default:
                    error = $"unknown argument '{arg}'";
                    return false;
            }
        }

        flags = parsed;
        return true;
    }

    /// <summary>The app runs on %LOCALAPPDATA%\Doba: the only case in which the Run key is reconciled.</summary>
    public bool UsesDefaultDataLocation => !Demo && !SelfCheck && DataFolder is null;

    /// <summary>The app ends by itself once the perf work is done (first frame, idle sample, scenario).</summary>
    public bool ExitsAfterPerf => ExitAfterFirstFrame || PerfIdleSeconds > 0 || PerfScenario is not null;

    /// <summary>The data folder this launch opens (the default %LOCALAPPDATA%\Doba unless <c>--data</c>); null for
    /// <c>--demo</c> and <c>--selfcheck</c>, which read nothing from disk.</summary>
    public string? DataFolderInUse => Demo || SelfCheck ? null : DataFolder ?? DataLocation.Default().Folder;

    private static bool TryCount(string text, int max, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1 && value <= max;

    /// <summary>The value after the flag at <paramref name="i"/>: missing, or itself a flag (<c>--data --demo</c>), is an
    /// error, so a flag is never swallowed as a value.</summary>
    private static bool TryValue(IReadOnlyList<string> args, ref int i, [NotNullWhen(true)] out string? value,
        [NotNullWhen(false)] out string? error)
    {
        var flag = args[i];
        if (i + 1 >= args.Count)
        {
            value = null;
            error = flag + " needs a value";
            return false;
        }

        value = args[++i];
        if (value.StartsWith("--", StringComparison.Ordinal))
        {
            error = $"{flag} needs a value (got the flag '{value}')";
            value = null;
            return false;
        }

        error = null;
        return true;
    }
}

/// <summary>The data folder of a normal launch could not be opened or read (locked, unreadable, from a newer Doba,
/// corrupt). <c>Program.Main</c> shows it and exits with <see cref="AppStartup.ExitDataUnavailable"/>; nothing in the
/// folder is changed and no fresh database is created in its place.</summary>
public sealed class StartupFailedException : Exception
{
    public StartupFailedException()
    {
    }

    public StartupFailedException(string message)
        : base(message)
    {
    }

    public StartupFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The data folder that could not be opened.</summary>
    public string? Folder { get; private init; }

    /// <summary>Wraps the open/load failure of <paramref name="folder"/>; the message is <see cref="Describe"/>'s.</summary>
    public static StartupFailedException For(string folder, Exception error) =>
        new(Describe(folder, error), error) { Folder = folder };

    /// <summary>What the owner is told: what failed, where, and the cause in the storage layer's words.</summary>
    public static string Describe(string folder, Exception error)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(error);
        return "Doba could not open its data, so it did not start." + Environment.NewLine + Environment.NewLine +
            "Folder: " + folder + Environment.NewLine +
            "Reason: " + error.Message + " (" + error.GetType().Name + ")" + Environment.NewLine + Environment.NewLine +
            "Nothing in the folder was changed. Daily backups are in its Backups folder.";
    }
}

/// <summary>Process-level startup facts: command-line flags, the startup stopwatch, the single-instance claim and the
/// database preload.</summary>
internal static class AppStartup
{
    /// <summary>Exit code for a command line that is not valid (nothing was opened).</summary>
    public const int ExitBadArguments = 2;

    /// <summary>Exit code when the data folder could not be opened or read (<see cref="StartupFailedException"/>).</summary>
    public const int ExitDataUnavailable = 3;

    /// <summary>Started first thing in <c>Program.Main</c>; every <c>--perf</c> time is measured on it.</summary>
    public static Stopwatch Clock { get; } = new();

    public static StartupFlags Flags { get; private set; } = StartupFlags.None;

    /// <summary>This process's claim on its data folder (null for <c>--demo</c> / <c>--selfcheck</c>).</summary>
    public static SingleInstance? Instance { get; private set; }

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

    /// <summary>
    /// Called from <c>Program.Main</c> once the flags are valid and this process owns its data folder
    /// (<paramref name="instance"/>; null for demo launches): keeps the flags and starts the database preload.
    /// </summary>
    public static void Begin(StartupFlags flags, SingleInstance? instance)
    {
        ArgumentNullException.ThrowIfNull(flags);
        Flags = flags;
        Instance = instance;
        if (flags.Perf)
        {
            PerfProbe.Enable();
        }

        Preload = AppServices.StartPreload(flags);
    }

    /// <summary>
    /// The services of a normal launch: <see cref="AppServices.FromStartup"/>, with an open or load failure of the data
    /// folder turned into a <see cref="StartupFailedException"/> that names the folder (shown by <c>Program.Main</c>).
    /// </summary>
    public static AppServices OpenServices(StartupFlags flags, Task<LoadedStore>? preload,
        Func<PlatformPlan, PlatformServices>? platforms = null)
    {
        ArgumentNullException.ThrowIfNull(flags);
        try
        {
            return AppServices.FromStartup(flags, preload, platforms);
        }
        catch (Exception ex) when (flags.DataFolderInUse is { } folder && ex is not StartupFailedException)
        {
            throw StartupFailedException.For(folder, ex);
        }
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
