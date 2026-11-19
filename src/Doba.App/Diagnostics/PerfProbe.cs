using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Doba.App.Diagnostics;

/// <summary>
/// Startup stopwatch marks for <c>--perf</c>. <see cref="Mark"/> costs one branch when perf logging is off, so the
/// marks stay in the startup path for every launch. Times are milliseconds since <c>Program.Main</c>
/// (<see cref="AppStartup.Clock"/>); <see cref="ProcessToMainMs"/> adds what happened before Main (loader, runtime init).
/// </summary>
internal static class PerfProbe
{
    private static readonly List<(string Name, double Ms)> s_marks = [];

    /// <summary>True under <c>--perf</c> (marks are recorded and printed).</summary>
    public static bool Enabled { get; private set; }

    /// <summary>
    /// Process creation → <c>Program.Main</c>, ms: the OS loader, the NativeAOT runtime start and static constructors.
    /// Measured with wall-clock times (<see cref="Process.StartTime"/> against <see cref="DateTime.UtcNow"/>), which is
    /// fine for a diagnostic but not monotonic: NaN when unavailable or negative (clock adjusted meanwhile).
    /// </summary>
    public static double ProcessToMainMs { get; private set; } = double.NaN;

    /// <summary>The recorded marks in order (name, ms since Main).</summary>
    public static IReadOnlyList<(string Name, double Ms)> Marks => s_marks;

    /// <summary>Turns recording on (called once from <see cref="AppStartup.Begin"/> under <c>--perf</c>).</summary>
    public static void Enable()
    {
        Enabled = true;
        ProcessToMainMs = MeasureProcessToMain(AppStartup.Clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>Records <paramref name="name"/> at the current time (no-op unless <see cref="Enabled"/>).</summary>
    public static void Mark(string name)
    {
        if (Enabled)
        {
            s_marks.Add((name, NowMs));
        }
    }

    /// <summary>Records <paramref name="name"/> at a time taken earlier (e.g. on another thread).</summary>
    public static void MarkAt(string name, double ms)
    {
        if (Enabled)
        {
            s_marks.Add((name, ms));
        }
    }

    /// <summary>Milliseconds since Main.</summary>
    public static double NowMs => AppStartup.Clock.Elapsed.TotalMilliseconds;

    /// <summary>The time of the first mark called <paramref name="name"/>, or NaN.</summary>
    public static double At(string name)
    {
        foreach (var (n, ms) in s_marks)
        {
            if (string.Equals(n, name, StringComparison.Ordinal))
            {
                return ms;
            }
        }

        return double.NaN;
    }

    /// <summary>One line: <c>doba-perf marks a=… b=…</c> (ms since Main, in recording order).</summary>
    public static string FormatMarks()
    {
        var sb = new StringBuilder("doba-perf marks");
        foreach (var (name, ms) in s_marks)
        {
            sb.Append(' ').Append(name).Append('=').Append(Ms(ms));
        }

        return sb.ToString();
    }

    /// <summary>Invariant, one decimal; "n/a" for NaN.</summary>
    public static string Ms(double ms) =>
        double.IsNaN(ms) ? "n/a" : ms.ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>For tests: forget everything recorded.</summary>
    internal static void Reset()
    {
        Enabled = false;
        ProcessToMainMs = double.NaN;
        s_marks.Clear();
    }

    private static double MeasureProcessToMain(double sinceMainMs)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var sinceStart = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
            var beforeMain = sinceStart - sinceMainMs;
            return beforeMain >= 0 ? beforeMain : double.NaN;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return double.NaN;
        }
    }
}
