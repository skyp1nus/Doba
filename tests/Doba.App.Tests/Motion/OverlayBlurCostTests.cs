using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Doba.App.Controls;
using Doba.App.Shell;
using Doba.Core.Navigation;

namespace Doba.App.Tests.MotionChecks;

/// <summary>
/// The cost of the overlay backdrop blur, measured on the headless Skia CPU raster (no GPU; the real app draws with
/// Skia on the GPU through ANGLE, so these are an upper bound): frame times while the task dialog opens and closes over
/// the Week view, with the blur and without it, and whether anything is drawn while the dialog is open and nothing
/// changes. Writes <c>artifacts/overlay-blur-cost.txt</c>. Only the idle check is asserted; timings are reported.
/// </summary>
public class OverlayBlurCostTests
{
    private static readonly TimeSpan s_window = TimeSpan.FromMilliseconds(400);

    [AvaloniaFact]
    public void Measure_frames_with_and_without_the_backdrop_blur()
    {
        var report = new StringBuilder();
        try
        {
            var sharp = Measure(blur: false, report);
            var blurred = Measure(blur: true, report);

            // Idle: with the overlay open and nothing changing, a render tick draws nothing.
            Assert.True(blurred.Idle < 2, report.ToString());
            Assert.True(sharp.Idle < 2, report.ToString());
        }
        finally
        {
            var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "overlay-blur-cost.txt"), report.ToString());
        }
    }

    private static (double Idle, double Open, double Close) Measure(bool blur, StringBuilder report)
    {
        var label = blur ? "blur" : "no blur";
        using var shell = new AnimatedShell(AppView.Week, blur: blur);
        Assert.Equal(blur, shell.Overlays.FullBlurRadius > 0);
        var cards = shell.OnScreen<TaskCard>().Count();
        Frame(shell);

        shell.Vm.NewTask();
        var open = Frames(shell);
        shell.Settle();
        var idle = Enumerable.Range(0, 10).Select(_ => Tick()).ToList();

        shell.Overlays.RequestClose();
        var close = Frames(shell);
        shell.Settle();

        report.AppendLine(CultureInfo.InvariantCulture,
            $"{label}: Week view, {cards} cards, 1600x1000 px. open: {open.Count} frames in {s_window.TotalMilliseconds} ms, " +
            $"median {Median(open):F1} ms, max {open.Max():F1} ms [{Join(open)}]; close: {close.Count} frames, " +
            $"median {Median(close):F1} ms, max {close.Max():F1} ms [{Join(close)}]; idle ticks with the dialog open: " +
            $"max {idle.Max():F2} ms");
        return (idle.Max(), Median(open), Median(close));
    }

    /// <summary>Frames at up to 60 fps for <see cref="s_window"/> (the entrance or exit and a little after).</summary>
    private static List<double> Frames(AnimatedShell shell)
    {
        var frames = new List<double>();
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < s_window)
        {
            var started = clock.Elapsed;
            frames.Add(Frame(shell));
            var rest = TimeSpan.FromMilliseconds(16) - (clock.Elapsed - started);
            if (rest > TimeSpan.Zero)
            {
                Thread.Sleep(rest);
            }
        }

        return frames;
    }

    /// <summary>One frame: queued jobs, layout and a render tick (the frame is drawn in the tick).</summary>
    private static double Frame(AnimatedShell shell)
    {
        var watch = Stopwatch.StartNew();
        Dispatcher.UIThread.RunJobs();
        shell.Window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return watch.Elapsed.TotalMilliseconds;
    }

    private static double Tick()
    {
        var watch = Stopwatch.StartNew();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return watch.Elapsed.TotalMilliseconds;
    }

    private static string Join(IEnumerable<double> values) =>
        string.Join(", ", values.Select(v => v.ToString("F1", CultureInfo.InvariantCulture)));

    private static double Median(List<double> values) => values.Count == 0 ? 0 : values.Order().ElementAt(values.Count / 2);
}
