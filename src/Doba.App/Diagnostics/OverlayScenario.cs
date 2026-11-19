using System.Globalization;
using Doba.App.Shell;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Diagnostics;

/// <summary>
/// The overlay part of <c>--perf-scenario week</c> (run after <see cref="WeekScenario"/>, over the Week view it leaves
/// open): opens the New task dialog and closes it again (as Esc does; nothing is written), alternating cycles with the
/// backdrop blur on and off (<see cref="OverlayHost.BackdropBlurRadius"/> = 0), while <see cref="FrameMeter"/> records
/// frames. Only frames inside the fades count: from the open call to the end of the entrance
/// (<see cref="AppMotion.DialogIn"/>) and from the close call to the end of the exit (<see cref="AppMotion.DialogExit"/>),
/// each plus one 60 Hz frame. One unmeasured cycle first builds the dialog's view.
/// </summary>
internal static class OverlayScenario
{
    /// <summary>Default cycles per mode (blur on, blur off).</summary>
    public const int DefaultCycles = 8;

    /// <summary>The wait after opening / closing: the fade has ended and a few frames at rest follow.</summary>
    public static int OpenWaitMs => AppMotion.DialogIn + 180;

    public static int CloseWaitMs => AppMotion.DialogExit + 180;

    /// <summary>Runs the cycles and returns the <c>doba-perf scenario=overlay …</c> line.</summary>
    public static async Task<string> RunAsync(MainWindow window, int cycles)
    {
        ArgumentNullException.ThrowIfNull(window);
        var vm = window.ViewModel ?? throw new InvalidOperationException("The main window has no view-model.");
        var host = window.Overlays;
        cycles = cycles > 0 ? cycles : DefaultCycles;
        var configured = OverlayHost.BackdropBlurRadius;
        var slack = FrameStats.Frame60;
        var windows = new[] { new List<(double, double)>(), new List<(double, double)>(), new List<(double, double)>(), new List<(double, double)>() };
        const int BlurOpen = 0, BlurClose = 1, PlainOpen = 2, PlainClose = 3;

        // Warm-up: the first open builds the dialog's view, templates and text layouts.
        await CycleAsync().ConfigureAwait(true);

        var meter = new FrameMeter(window);
        meter.Start();
        var start = FrameMeter.Now;
        try
        {
            for (var i = 0; i < cycles; i++)
            {
                // Alternate the order so neither mode always runs right after the other.
                bool[] order = i % 2 == 0 ? [true, false] : [false, true];
                foreach (var blur in order)
                {
                    OverlayHost.BackdropBlurRadius = blur ? configured : 0;
                    var (open, close) = await CycleAsync().ConfigureAwait(true);
                    windows[blur ? BlurOpen : PlainOpen].Add((open, open + AppMotion.DialogIn + slack));
                    windows[blur ? BlurClose : PlainClose].Add((close, close + AppMotion.DialogExit + slack));
                }
            }
        }
        finally
        {
            OverlayHost.BackdropBlurRadius = configured;
        }

        var end = FrameMeter.Now;
        await meter.StopAsync().ConfigureAwait(true);

        string Stats(int which, string prefix)
        {
            var ui = FrameStats.IntervalsEndingIn(meter.UiFrames, windows[which]);
            var render = FrameStats.IntervalsEndingIn(meter.RenderFrames, windows[which]);
            return string.Concat(FrameStats.FromIntervals(ui, ui.Count).Format(prefix + "-ui"), " ",
                FrameStats.FromIntervals(render, render.Count).Format(prefix + "-render"));
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"doba-perf scenario=overlay cycles={cycles} seconds={(end - start) / 1000:F1} blur-radius={host.FullBlurRadius:F2} " +
            $"{Stats(BlurOpen, "blur-open")} {Stats(BlurClose, "blur-close")} {Stats(PlainOpen, "plain-open")} {Stats(PlainClose, "plain-close")}");

        async Task<(double Open, double Close)> CycleAsync()
        {
            var open = FrameMeter.Now;
            vm.NewTask();
            await Task.Delay(OpenWaitMs).ConfigureAwait(true);
            var close = FrameMeter.Now;
            host.RequestClose();
            await Task.Delay(CloseWaitMs).ConfigureAwait(true);
            if (vm.Overlay is not null)
            {
                throw new InvalidOperationException("The task dialog did not close.");
            }

            return (open, close);
        }
    }
}
