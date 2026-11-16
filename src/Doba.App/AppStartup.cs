using System.Diagnostics;

namespace Doba.App;

/// <summary>Process-level startup facts: command-line flags and the time-to-first-frame stopwatch.</summary>
internal static class AppStartup
{
    public static Stopwatch Clock { get; } = new();

    /// <summary>True when started with <c>--perf</c>: the app logs its time-to-first-frame.</summary>
    public static bool PerfLogging { get; private set; }

    public static void Begin(string[] args)
    {
        Clock.Start();
        PerfLogging = args.Contains("--perf", StringComparer.OrdinalIgnoreCase);
    }
}
