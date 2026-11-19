using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Threading;
using Doba.App.Platform;

namespace Doba.App.Services;

/// <summary>What one release gave back (printed by <c>--perf-scenario tray</c>; read by tests).</summary>
/// <param name="GraphicsStepRan">The render thread ran the graphics step (false when the window was never opened or
/// the render thread did not answer in time).</param>
/// <param name="GraphicsCacheBefore">Bytes in Skia's GPU resource cache before it was emptied (0 without a GPU context).</param>
/// <param name="GraphicsCacheAfter">Bytes left in it: resources still in use (the window's own surfaces).</param>
/// <param name="DriverTrimmed">The graphics driver was asked to drop its internal buffers.</param>
/// <param name="GcPauseMs">How long the collection paused the managed threads.</param>
/// <param name="TotalMs">The whole release, first step to last.</param>
public sealed record HiddenMemoryReport(bool GraphicsStepRan, long GraphicsCacheBefore, long GraphicsCacheAfter, bool DriverTrimmed,
    double GcPauseMs, double TotalMs);

/// <summary>
/// Gives back the memory a window hidden in the tray does not need. Hiding a window frees nothing by itself (Avalonia
/// only stops rendering it), and Doba spends most of its time hidden, so once the window has stayed hidden for
/// <see cref="DelayMs"/> this runs once:
/// <list type="number">
/// <item>empties Skia's GPU resource cache (glyph atlases, cached textures, the blur's scratch surfaces) and asks the
/// graphics driver to drop its internal buffers (<see cref="IProcessMemory.TrimGraphicsDevice"/>);</item>
/// <item>collects the managed heap in the mode that also returns the freed regions to the OS, and lets finalizers
/// release the native objects behind what was collected;</item>
/// <item>returns the free blocks cached in the native heaps (<see cref="IProcessMemory.TrimNativeHeaps"/>).</item>
/// </list>
/// Nothing the user can see is released: every view, its scroll position and every view-model stay as they are, so the
/// window comes back exactly as it was hidden. What was emptied is rebuilt on demand by the first frames after the show
/// (glyphs and textures are uploaded again). The working set is never trimmed by a system call: only memory that is
/// really free goes back.
/// </summary>
/// <remarks>
/// The GPU cache belongs to the render thread and is reachable only while something is being drawn, and a hidden window
/// draws nothing. So a 1×1 composition visual that draws nothing is attached to the window while it is visible
/// (<see cref="Attach"/>; a visual attached after the hide would not reach the render thread before the next show), and
/// the release renders that one visual off screen (<see cref="Compositor.CreateCompositionVisualSnapshot"/>), which runs
/// its handler on the render thread with the GPU context current.
/// <para>All members are for the UI thread. A release that is under way when the window is shown again
/// (<see cref="Cancel"/>) stops before its next step.</para>
/// </remarks>
public sealed class HiddenMemoryRelease : IDisposable
{
    /// <summary>How long the window stays hidden before the release: closing the window and opening it again shortly
    /// after (a quick add, a second thought) costs nothing.</summary>
    public const int DelayMs = 30_000;

    /// <summary>How long the release waits for the render thread before going on without the graphics step.</summary>
    private const int GraphicsTimeoutMs = 5_000;

    private readonly TopLevel _window;
    private readonly IProcessMemory _memory;
    private readonly Action<string> _log;
    private readonly GraphicsPurge _purge;
    private Action _collect = Collect;
    private CompositionCustomVisual? _visual;
    private IDisposable? _timer;
    private int _run;
    private bool _disposed;

    /// <param name="window">The window whose hidden time this serves.</param>
    /// <param name="memory">The platform's part (<see cref="PlatformServices.Memory"/>).</param>
    /// <param name="log">Receives a line when a release fails (it never throws to the caller).</param>
    public HiddenMemoryRelease(TopLevel window, IProcessMemory memory, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(log);
        _window = window;
        _memory = memory;
        _log = log;
        _purge = new GraphicsPurge(memory);
    }

    /// <summary>The wait between <see cref="Schedule"/> and the release (tests shorten it).</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(DelayMs);

    /// <summary>A release is waiting for its delay.</summary>
    public bool IsScheduled => _timer is not null;

    /// <summary>The last release that ran to its end; null before the first.</summary>
    public HiddenMemoryReport? Last { get; private set; }

    /// <summary>The release started last (completed when none has started). Never faults.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>The collection step, run on a pool thread (tests replace it to hold a release in the middle).</summary>
    internal Action Collector
    {
        get => _collect;
        set => _collect = value ?? Collect;
    }

    /// <summary>
    /// Attaches the visual the graphics step renders. Call once the window is open (it must be attached while the window
    /// still renders); later calls do nothing.
    /// </summary>
    public void Attach()
    {
        if (_disposed || _visual is not null || ElementComposition.GetElementVisual(_window) is not { } root)
        {
            return;
        }

        _visual = root.Compositor.CreateCustomVisual(_purge);
        _visual.Size = new Vector(1, 1);
        ElementComposition.SetElementChildVisual(_window, _visual);
    }

    /// <summary>The window was hidden: release after <see cref="Delay"/> unless <see cref="Cancel"/> comes first.</summary>
    public void Schedule()
    {
        Cancel();
        if (_disposed)
        {
            return;
        }

        var run = _run;
        _timer = DispatcherTimer.RunOnce(() =>
        {
            _timer = null;
            // Every show cancels the release; a window that is visible all the same is never released under the user.
            if (!_window.IsVisible)
            {
                Completion = RunAsync(run);
            }
        }, Delay, DispatcherPriority.Background);
    }

    /// <summary>The window is shown again (or the app is quitting): no release, and one under way stops.</summary>
    public void Cancel()
    {
        _run++;
        // A graphics step that the render thread has not started yet must not run inside the first frames of the show.
        _purge.Disarm();
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose()
    {
        Cancel();
        _disposed = true;
    }

    private async Task RunAsync(int run)
    {
        try
        {
            var started = Stopwatch.GetTimestamp();
            var graphics = await PurgeGraphicsAsync().ConfigureAwait(true);
            if (run != _run)
            {
                return;
            }

            var pause = GC.GetTotalPauseDuration();
            await Task.Run(_collect).ConfigureAwait(true);
            pause = GC.GetTotalPauseDuration() - pause;
            if (run != _run)
            {
                return;
            }

            _memory.TrimNativeHeaps();
            Last = new HiddenMemoryReport(graphics.Ran, graphics.Before, graphics.After, graphics.DriverTrimmed,
                pause.TotalMilliseconds, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
#pragma warning disable CA1031 // Giving memory back is optional: a failure is logged and the app goes on as it was.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log("hidden memory release failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// A full blocking compacting collection in the aggressive mode, which decommits the regions it frees instead of
    /// keeping them for the next allocations, then the finalizers: Skia's native objects (text blobs, paths, images) are
    /// freed by the finalizers of their managed wrappers.
    /// </summary>
    private static void Collect()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
    }

    private async Task<GraphicsResult> PurgeGraphicsAsync()
    {
        if (_visual is not { } visual)
        {
            // Never opened (started hidden in the tray): nothing was drawn, so there is nothing to purge.
            return GraphicsResult.None;
        }

        _purge.Arm();
        var snapshot = visual.Compositor.CreateCompositionVisualSnapshot(visual, 1);
        try
        {
            using var bitmap = await snapshot.WaitAsync(TimeSpan.FromMilliseconds(GraphicsTimeoutMs)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            // The render thread did not get to it; whenever it does, its 1×1 bitmap is not left to a finalizer.
            _ = snapshot.ContinueWith(static done => DisposeSnapshot(done), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
#pragma warning disable CA1031 // The graphics step is optional (a lost context, no root): the collection and the heap step do not depend on it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log("hidden memory release: graphics step failed: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            _purge.Disarm();
        }

        return _purge.TakeResult();
    }

    private static void DisposeSnapshot(Task<Bitmap> done)
    {
        if (done.IsCompletedSuccessfully)
        {
            done.Result.Dispose();
        }
        else
        {
            // Observed, so it is not reported as an unobserved task exception.
            _ = done.Exception;
        }
    }

    private sealed record GraphicsResult(bool Ran, long Before, long After, bool DriverTrimmed)
    {
        /// <summary>The render thread never ran the step.</summary>
        public static GraphicsResult None { get; } = new(false, 0, 0, false);

        /// <summary>It ran, and there was no GPU cache to empty.</summary>
        public static GraphicsResult NoGpu { get; } = new(true, 0, 0, false);
    }

    /// <summary>Draws nothing. While armed, the next time it is rendered (on the render thread) it empties the GPU
    /// cache of the context it is rendered with, once.</summary>
    private sealed class GraphicsPurge(IProcessMemory memory) : CompositionCustomVisualHandler
    {
        private int _armed;
        private volatile GraphicsResult? _result;

        public void Arm()
        {
            _result = null;
            Interlocked.Exchange(ref _armed, 1);
        }

        public void Disarm() => Interlocked.Exchange(ref _armed, 0);

        public GraphicsResult TakeResult() => _result ?? GraphicsResult.None;

        public override void OnRender(ImmediateDrawingContext drawingContext)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 0)
            {
                return;
            }

            // No lease or no GRContext: software rendering (or a headless test), nothing is cached on a GPU.
            _result = GraphicsResult.NoGpu;
            if (drawingContext.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
            {
                return;
            }

            using var lease = feature.Lease();
            if (lease.GrContext is not { } gpu)
            {
                return;
            }

            gpu.GetResourceCacheUsage(out _, out var before);
            gpu.Flush();
            gpu.PurgeResources();
            gpu.GetResourceCacheUsage(out _, out var after);
            var trimmed = false;
            using (var platform = lease.TryLeasePlatformGraphicsApi())
            {
                if (platform is not null)
                {
                    trimmed = memory.TrimGraphicsDevice(platform.Context);
                }
            }

            _result = new GraphicsResult(true, before, after, trimmed);
        }
    }
}
