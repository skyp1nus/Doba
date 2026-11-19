using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Doba.App.Services;

namespace Doba.App.Diagnostics;

/// <summary>
/// What <c>--perf</c> and its companions do once the main window exists: time the first frame, optionally sample the
/// idle memory and run a frame scenario, print <c>doba-perf …</c> lines to stdout, and end the app when asked to.
/// Nothing here runs without one of the perf flags.
/// </summary>
/// <remarks>
/// Lines (all fields in ms unless named otherwise; "since Main" = since <c>Program.Main</c> started):
/// <code>
/// doba-perf time-to-first-frame-ms=… working-set-mb=… private-mb=… frame-rendered-ms=… process-to-main-ms=…
///           main-to-shown-ms=… shown-to-rendered-ms=… from-process-start-ms=… data=demo|disk tasks=…
/// doba-perf marks platform=… app-xaml=… framework=… services=… window=… icon=… lifecycle=… shown=… first-frame=… rendered=…
/// doba-perf idle-seconds=… working-set-mb=… private-mb=… managed-heap-mb=… gc-count=… gc-committed-mb=… (as of the last GC; 0 before the first)
/// doba-perf scenario=week …   (see WeekScenario)
/// doba-perf scenario=overlay … (see OverlayScenario; runs after the week scenario, over the Week view)
/// doba-perf scenario=tray …   (see TrayScenario)
/// </code>
/// <c>time-to-first-frame-ms</c> keeps its original meaning (since Main, to the first animation-frame callback after the
/// window opened, i.e. the UI thread is about to commit the first frame). <c>frame-rendered-ms</c> is when the render
/// thread finished rendering the batch that frame committed (<see cref="Avalonia.Rendering.Composition.Transport.CompositionBatch.Rendered"/>):
/// the content is in the swap chain; DWM shows it on its next composition.
/// </remarks>
internal static class PerfSession
{
    /// <summary>How long to wait for the render thread to report the first frame before going on without it.</summary>
    private const int RenderedTimeoutMs = 10_000;

    /// <summary>Hooks <paramref name="window"/>'s first frame when <paramref name="flags"/> ask for any perf output or
    /// for an exit after the first frame. Call before the window is shown.</summary>
    public static void Attach(MainWindow window, IClassicDesktopStyleApplicationLifetime lifetime, StartupFlags flags,
        AppLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(lifecycle);
        if (!flags.Perf && !flags.ExitAfterFirstFrame)
        {
            return;
        }

        var opened = false;
        window.Opened += (_, _) =>
        {
            if (opened)
            {
                return;
            }

            opened = true;
            PerfProbe.Mark("shown");
            window.RequestAnimationFrame(_ => OnFirstFrame(window, lifetime, flags, lifecycle));
        };
    }

    private static void OnFirstFrame(MainWindow window, IClassicDesktopStyleApplicationLifetime lifetime, StartupFlags flags,
        AppLifecycle lifecycle)
    {
        var firstFrameMs = PerfProbe.NowMs;
        PerfProbe.Mark("first-frame");

        // The batch this tick commits carries the first frame; Rendered completes on the render thread (synchronously,
        // see its docs), so the time is taken there and the report goes back to the UI thread.
        var batch = ElementComposition.GetElementVisual(window)?.Compositor.RequestCompositionBatchCommitAsync();
        if (batch is null)
        {
            Continue(double.NaN);
            return;
        }

        // Should the render thread never report the batch (lost device, window gone), go on without the number rather
        // than keep an --exit-after-first-frame run alive.
        _ = batch.Rendered.WaitAsync(TimeSpan.FromMilliseconds(RenderedTimeoutMs)).ContinueWith(
            done =>
            {
                var renderedMs = done.IsCompletedSuccessfully ? PerfProbe.NowMs : double.NaN;
                Dispatcher.UIThread.Post(() => Continue(renderedMs));
            },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        void Continue(double renderedMs) => _ = AfterFirstFrameAsync(window, lifetime, flags, lifecycle, firstFrameMs, renderedMs);
    }

    private static async Task AfterFirstFrameAsync(MainWindow window, IClassicDesktopStyleApplicationLifetime lifetime,
        StartupFlags flags, AppLifecycle lifecycle, double firstFrameMs, double renderedMs)
    {
        if (!double.IsNaN(renderedMs))
        {
            // Measured on the render thread, recorded here (the marks list belongs to the UI thread).
            PerfProbe.MarkAt("rendered", renderedMs);
        }

        if (flags.Perf)
        {
            Console.WriteLine(StartupLine(window, firstFrameMs, renderedMs));
            Console.WriteLine(PerfProbe.FormatMarks());
        }

        try
        {
            if (flags.PerfIdleSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(flags.PerfIdleSeconds)).ConfigureAwait(true);
                Console.WriteLine(IdleLine(flags.PerfIdleSeconds));
            }

            if (string.Equals(flags.PerfScenario, StartupFlags.WeekScenario, StringComparison.Ordinal))
            {
                Console.WriteLine(await WeekScenario.RunAsync(window, flags.PerfRounds).ConfigureAwait(true));
                Console.WriteLine(await OverlayScenario.RunAsync(window, OverlayScenario.DefaultCycles).ConfigureAwait(true));
            }
            else if (string.Equals(flags.PerfScenario, StartupFlags.TrayScenario, StringComparison.Ordinal))
            {
                Console.WriteLine(await TrayScenario.RunAsync(window, lifecycle, flags.PerfHiddenSeconds).ConfigureAwait(true));
            }
        }
#pragma warning disable CA1031 // A failed measurement is reported on stdout; a perf run must still end.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.WriteLine("doba-perf error=" + ex.GetType().Name + " message=" + ex.Message);
        }
        finally
        {
            if (flags.ExitsAfterPerf)
            {
                Console.Out.Flush();
                lifetime.Shutdown();
            }
        }
    }

    /// <summary>The first <c>doba-perf</c> line (format kept from the original; fields only added).</summary>
    internal static string StartupLine(MainWindow window, double firstFrameMs, double renderedMs)
    {
        using var process = Process.GetCurrentProcess();
        var beforeMain = PerfProbe.ProcessToMainMs;
        var shown = PerfProbe.At("shown");
        var app = window.ViewModel?.App;
        return string.Create(CultureInfo.InvariantCulture,
            $"doba-perf time-to-first-frame-ms={firstFrameMs:F0} working-set-mb={Mb(process.WorkingSet64)} private-mb={Mb(process.PrivateMemorySize64)} " +
            $"frame-rendered-ms={PerfProbe.Ms(renderedMs)} process-to-main-ms={PerfProbe.Ms(beforeMain)} " +
            $"main-to-shown-ms={PerfProbe.Ms(shown)} shown-to-rendered-ms={PerfProbe.Ms(renderedMs - shown)} " +
            $"from-process-start-ms={PerfProbe.Ms(beforeMain + renderedMs)} " +
            $"data={(app is null ? "none" : app.IsDemo ? "demo" : "disk")} tasks={app?.Store.Count ?? 0}");
    }

    private static string IdleLine(int seconds)
    {
        using var process = Process.GetCurrentProcess();
        return string.Create(CultureInfo.InvariantCulture,
            $"doba-perf idle-seconds={seconds} working-set-mb={Mb(process.WorkingSet64)} private-mb={Mb(process.PrivateMemorySize64)} " +
            $"managed-heap-mb={Mb(GC.GetTotalMemory(forceFullCollection: false))} gc-count={GC.CollectionCount(0)} " +
            $"gc-committed-mb={Mb(GC.GetGCMemoryInfo().TotalCommittedBytes)}");
    }

    private static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);
}
