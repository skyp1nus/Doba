using System.Diagnostics;
using System.Globalization;
using Avalonia.Rendering.Composition;
using Doba.App.Services;
using Doba.Core.Navigation;

namespace Doba.App.Diagnostics;

/// <summary>
/// <c>--perf-scenario tray</c>: the memory of the three states the app lives in, and what coming back from the tray
/// costs. It samples the memory (<see cref="MemorySample"/>) <see cref="RestMs"/> after the first frame
/// (<c>start</c>), visits every screen twice through the shell's public navigation API and samples again
/// (<c>screens</c>), hides the window to the tray the way closing it does, samples after the hidden time
/// (<c>tray</c>), shows the window again, visits every screen once more and takes the last sample (<c>shown</c>).
/// <para>The price of the hidden time is on the same line: the GCs that ran while hidden and their pause, what the
/// memory release did (<c>release-…</c>; it runs <see cref="HiddenMemoryRelease.DelayMs"/> after the hide), the time
/// from the show call to the first frame (its callback, and the render thread finishing it), and the frames of one
/// walk over the screens before hiding (<c>before-</c>: every view exists) and right after the show (<c>after-</c>),
/// recorded by <see cref="FrameMeter"/>.</para>
/// </summary>
internal static class TrayScenario
{
    /// <summary>Default time hidden in the tray, seconds.</summary>
    public const int DefaultHiddenSeconds = 60;

    /// <summary>The rest before a memory sample (the same 5 s as the idle sample of scripts/perf.ps1).</summary>
    public const int RestMs = 5000;

    /// <summary>How long to wait for the render thread to finish the first frame after the show before going on
    /// without its time.</summary>
    private const int FirstFrameTimeoutMs = 10_000;

    /// <summary>How long to wait for that frame's callback and its render together.</summary>
    private const int FirstFrameGiveUpMs = 2 * FirstFrameTimeoutMs;

    /// <summary>
    /// Render frames one walk can record: a walk lasts about six seconds, so this covers refresh rates far above any
    /// display's. Small on purpose: the meters' buffers are alive while the memory samples are taken, and at this size
    /// both together are under 0.2 MB.
    /// </summary>
    private const int WalkFrames = 8192;

    /// <summary>The wait after each navigation: the longest transition has ended.</summary>
    public static int StepMs => WeekScenario.StepMs;

    /// <summary>Runs the scenario and returns its <c>doba-perf scenario=tray …</c> line.</summary>
    public static async Task<string> RunAsync(MainWindow window, AppLifecycle lifecycle, int hiddenSeconds)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(lifecycle);
        var vm = window.ViewModel ?? throw new InvalidOperationException("The main window has no view-model.");
        hiddenSeconds = hiddenSeconds > 0 ? hiddenSeconds : DefaultHiddenSeconds;
        var today = vm.App.DayClock.Today;
        // Every screen once and the New task dialog opened and closed (as Esc does; nothing is written), ending on
        // Today (the launch screen by default).
        Action[] route =
        [
            () => vm.GoSection(AppView.Inbox),
            () => vm.ZoomTo(AppView.Year, today),
            () => vm.ZoomTo(AppView.Month, today),
            () => vm.ZoomTo(AppView.Week, today),
            () => vm.ZoomTo(AppView.Day, today),
            () => vm.NewTask(),
            window.Overlays.RequestClose,
            vm.OpenSettings,
            vm.BackFromSettings,
            () => vm.GoSection(AppView.Today),
        ];

        await Task.Delay(RestMs).ConfigureAwait(true);
        var start = MemorySample.Take();

        // The first walk builds every view; the second is the one compared with the walk after the show.
        await WalkAsync(route).ConfigureAwait(true);
        var before = new FrameMeter(window, WalkFrames);
        before.Start();
        var beforeFrom = FrameMeter.Now;
        await WalkAsync(route).ConfigureAwait(true);
        var beforeTo = FrameMeter.Now;
        await before.StopAsync().ConfigureAwait(true);
        await Task.Delay(RestMs).ConfigureAwait(true);
        var screens = MemorySample.Take();

        var gcCount = GC.CollectionCount(0);
        var gcPause = GC.GetTotalPauseDuration();
        lifecycle.HideToTray();
        await Task.Delay(TimeSpan.FromSeconds(hiddenSeconds)).ConfigureAwait(true);
        // A release that started just before the hidden time ended is let to finish, so the sample and its report
        // describe the same state (completed already when none has started).
        await lifecycle.HiddenMemory.Completion.ConfigureAwait(true);
        var tray = MemorySample.Take();
        gcCount = GC.CollectionCount(0) - gcCount;
        gcPause = GC.GetTotalPauseDuration() - gcPause;
        var release = ReleaseFields(lifecycle.HiddenMemory.Last);

        var shownAt = Stopwatch.GetTimestamp();
        lifecycle.ShowWindow();
        var showCallMs = Stopwatch.GetElapsedTime(shownAt).TotalMilliseconds;
        var after = new FrameMeter(window, WalkFrames);
        after.Start();
        var afterFrom = FrameMeter.Now;
        var (frameMs, renderedMs) = await FirstFrameAsync(window, shownAt).ConfigureAwait(true);
        await WalkAsync(route).ConfigureAwait(true);
        var afterTo = FrameMeter.Now;
        await after.StopAsync().ConfigureAwait(true);
        await Task.Delay(RestMs).ConfigureAwait(true);
        var shown = MemorySample.Take();

        return string.Create(CultureInfo.InvariantCulture,
            $"doba-perf scenario=tray hidden-seconds={hiddenSeconds} steps={route.Length} step-wait-ms={StepMs} rest-ms={RestMs} " +
            $"{start.Format("start")} {screens.Format("screens")} {tray.Format("tray")} {shown.Format("shown")} " +
            $"hidden-gc-count={gcCount} hidden-gc-pause-ms={gcPause.TotalMilliseconds:F1} {release} " +
            $"show-call-ms={PerfProbe.Ms(showCallMs)} show-to-frame-ms={PerfProbe.Ms(frameMs)} show-to-rendered-ms={PerfProbe.Ms(renderedMs)} " +
            $"{Stats(before.UiFrames, beforeFrom, beforeTo).Format("before-ui")} {Stats(before.RenderFrames, beforeFrom, beforeTo).Format("before-render")} " +
            $"{Stats(after.UiFrames, afterFrom, afterTo).Format("after-ui")} {Stats(after.RenderFrames, afterFrom, afterTo).Format("after-render")}");
    }

    /// <summary>What the hidden-time memory release did (<see cref="HiddenMemoryRelease"/>); <c>release=0</c> when
    /// none ran to its end (the hidden time was shorter than its delay, or it failed and logged why).</summary>
    private static string ReleaseFields(HiddenMemoryReport? report) => report is null
        ? "release=0"
        : string.Create(CultureInfo.InvariantCulture,
            $"release=1 release-graphics={(report.GraphicsStepRan ? 1 : 0)} release-gpu-cache-before-mb={report.GraphicsCacheBefore / (1024.0 * 1024.0):F1} " +
            $"release-gpu-cache-after-mb={report.GraphicsCacheAfter / (1024.0 * 1024.0):F1} " +
            $"release-driver-trim={(report.DriverTrimmed ? 1 : 0)} release-gc-pause-ms={report.GcPauseMs:F1} release-ms={report.TotalMs:F1}");

    private static FrameStats Stats(IReadOnlyList<double> frames, double from, double to) =>
        FrameStats.FromTimestamps(FrameMeter.Between(frames, from, to));

    private static async Task WalkAsync(Action[] route)
    {
        foreach (var step in route)
        {
            step();
            await Task.Delay(StepMs).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Milliseconds from <paramref name="since"/> to the first animation-frame callback of <paramref name="window"/>
    /// and to the render thread finishing the batch that frame committed (NaN for what did not arrive in time: the
    /// window is covered or minimised).
    /// </summary>
    private static async Task<(double FrameMs, double RenderedMs)> FirstFrameAsync(MainWindow window, long since)
    {
        var result = new TaskCompletionSource<(double, double)>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.RequestAnimationFrame(frameTime =>
        {
            var frameMs = Stopwatch.GetElapsedTime(since).TotalMilliseconds;
            var batch = ElementComposition.GetElementVisual(window)?.Compositor.RequestCompositionBatchCommitAsync();
            if (batch is null)
            {
                result.TrySetResult((frameMs, double.NaN));
                return;
            }

            // Rendered completes on the render thread, so the time is taken there.
            _ = batch.Rendered.WaitAsync(TimeSpan.FromMilliseconds(FirstFrameTimeoutMs)).ContinueWith(
                done => result.TrySetResult((frameMs, done.IsCompletedSuccessfully ? Stopwatch.GetElapsedTime(since).TotalMilliseconds : double.NaN)),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        });

        try
        {
            return await result.Task.WaitAsync(TimeSpan.FromMilliseconds(FirstFrameGiveUpMs)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            // No animation frame at all: the window never became visible to the compositor.
            return (double.NaN, double.NaN);
        }
    }
}
