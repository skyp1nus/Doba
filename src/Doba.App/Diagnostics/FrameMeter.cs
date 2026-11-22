using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;

namespace Doba.App.Diagnostics;

/// <summary>
/// Records frame timestamps on both of Avalonia's threads while it runs (<c>--perf-scenario</c> only):
/// <list type="bullet">
/// <item><b>UI thread</b>: a <see cref="TopLevel.RequestAnimationFrame"/> loop. One callback per render tick that the
/// UI thread actually reached; a long gap means the UI thread was busy (layout, re-projection, a view being built) and
/// anything driven from it (input, layout, non-composition animations) stalled.</item>
/// <item><b>Render thread</b>: a 1×1 <see cref="CompositionCustomVisual"/> whose handler re-registers for every
/// animation frame and invalidates itself, so the compositor renders a frame on every tick. Its
/// <see cref="CompositionCustomVisualHandler.OnAnimationFrameUpdate"/> timestamps are the render loop's frames: a long gap
/// means a frame took longer than the refresh interval to render (composition animations, which run here, would
/// stutter).</item>
/// </list>
/// Neither is a GPU present timestamp (DWM composition and the display's vsync come after); they are the closest
/// signals Avalonia 12 exposes. The probe itself makes the compositor render every tick (a 1-pixel dirty rect).
/// All timestamps are <see cref="Now"/> (monotonic, ms).
/// </summary>
internal sealed class FrameMeter
{
    /// <summary>Render frames a meter holds unless told otherwise (about six minutes at 180 Hz).</summary>
    public const int DefaultCapacity = 1 << 16;

    private readonly TopLevel _top;
    private readonly List<double> _ui;
    private readonly List<int> _uiGc;
    private readonly RenderProbe _probe;
    private CompositionCustomVisual? _visual;
    private CompositionVisual? _previousChild;
    private bool _running;

    /// <param name="top">The window whose frames are recorded.</param>
    /// <param name="capacity">Render frames recorded at most (later ones are dropped). The buffers are allocated up
    /// front, so a scenario that samples memory gives the few thousand frames it needs instead of the default.</param>
    public FrameMeter(TopLevel top, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(top);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _top = top;
        _ui = new List<double>(Math.Min(capacity, 4096));
        _uiGc = new List<int>(Math.Min(capacity, 4096));
        _probe = new RenderProbe(capacity);
    }

    /// <summary>The meter's clock: monotonic milliseconds (same base on every thread).</summary>
    public static double Now => Stopwatch.GetElapsedTime(0).TotalMilliseconds;

    /// <summary>UI-thread frame timestamps recorded so far (complete after <see cref="StopAsync"/>).</summary>
    public IReadOnlyList<double> UiFrames => _ui;

    /// <summary>The GC count (<see cref="GC.CollectionCount(int)"/> of gen 0, which every collection raises) read at each
    /// UI frame, parallel to <see cref="UiFrames"/>.</summary>
    public IReadOnlyList<int> UiGcCounts => _uiGc;

    /// <summary>Render-thread frame timestamps (filled by <see cref="StopAsync"/>).</summary>
    public IReadOnlyList<double> RenderFrames { get; private set; } = [];

    /// <summary>The GC count read at each render frame, parallel to <see cref="RenderFrames"/>.</summary>
    public IReadOnlyList<int> RenderGcCounts { get; private set; } = [];

    /// <summary>Starts both recorders (UI thread).</summary>
    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _top.RequestAnimationFrame(OnUiFrame);
        if (ElementComposition.GetElementVisual(_top)?.Compositor is { } compositor)
        {
            _previousChild = ElementComposition.GetElementChildVisual(_top);
            _visual = compositor.CreateCustomVisual(_probe);
            _visual.Size = new Vector(1, 1);
            ElementComposition.SetElementChildVisual(_top, _visual);
            _visual.SendHandlerMessage(RenderProbe.StartMessage);
        }
    }

    /// <summary>Stops both recorders; afterwards <see cref="UiFrames"/> and <see cref="RenderFrames"/> are complete.</summary>
    public async Task StopAsync()
    {
        _running = false;
        if (_visual is { } visual)
        {
            visual.SendHandlerMessage(RenderProbe.StopMessage);
            // Once this batch is applied on the render thread the probe has stopped writing; the task completion
            // orders its writes before the read below.
            await visual.Compositor.RequestCommitAsync().ConfigureAwait(true);
            (RenderFrames, RenderGcCounts) = _probe.Snapshot();
            ElementComposition.SetElementChildVisual(_top, _previousChild);
            _visual = null;
        }
    }

    /// <summary>The timestamps of <paramref name="frames"/> within [<paramref name="from"/>, <paramref name="to"/>).</summary>
    public static List<double> Between(IReadOnlyList<double> frames, double from, double to)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var result = new List<double>();
        foreach (var t in frames)
        {
            if (t >= from && t < to)
            {
                result.Add(t);
            }
        }

        return result;
    }

    private void OnUiFrame(TimeSpan _)
    {
        if (!_running)
        {
            return;
        }

        _ui.Add(Now);
        _uiGc.Add(GC.CollectionCount(0));
        _top.RequestAnimationFrame(OnUiFrame);
    }

    /// <summary>Runs on the render thread; timestamps every animation frame while started.</summary>
    private sealed class RenderProbe(int capacity) : CompositionCustomVisualHandler
    {
        public static readonly object StartMessage = new();
        public static readonly object StopMessage = new();

        private readonly double[] _times = new double[capacity];
        private readonly int[] _gcs = new int[capacity];
        private int _count;
        private bool _on;

        public override void OnMessage(object message)
        {
            if (ReferenceEquals(message, StartMessage))
            {
                _on = true;
                Invalidate();
                RegisterForNextAnimationFrameUpdate();
            }
            else if (ReferenceEquals(message, StopMessage))
            {
                _on = false;
            }
        }

        public override void OnAnimationFrameUpdate()
        {
            if (!_on)
            {
                return;
            }

            if (_count < _times.Length)
            {
                _gcs[_count] = GC.CollectionCount(0);
                _times[_count++] = Now;
            }

            Invalidate();
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext drawingContext)
        {
            // Draws nothing: the invalidation alone makes the compositor render a frame.
        }

        /// <summary>The recorded timestamps and GC counts (read after the stop message was processed).</summary>
        public (double[] Times, int[] GcCounts) Snapshot() =>
            (_times.AsSpan(0, _count).ToArray(), _gcs.AsSpan(0, _count).ToArray());
    }
}
