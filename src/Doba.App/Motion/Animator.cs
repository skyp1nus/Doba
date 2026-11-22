using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Doba.App.Motion;

/// <summary>
/// A visual state relative to an element's resting state: an opacity multiplier, a translation in px and a scale.
/// <see cref="Rest"/> is "exactly as laid out" (opacity × 1, no offset, scale 1).
/// </summary>
public readonly record struct MotionState(double Opacity, double X, double Y, double ScaleX, double ScaleY)
{
    /// <summary>The element exactly as laid out.</summary>
    public static MotionState Rest { get; } = new(1, 0, 0, 1, 1);

    /// <summary>Invisible, otherwise at rest.</summary>
    public static MotionState Hidden { get; } = new(0, 0, 0, 1, 1);

    /// <summary>Offset by (<paramref name="x"/>, <paramref name="y"/>) px with the given opacity multiplier.</summary>
    public static MotionState Offset(double x, double y, double opacity = 1) => new(opacity, x, y, 1, 1);

    /// <summary>Uniformly scaled with the given opacity multiplier.</summary>
    public static MotionState Zoomed(double scale, double opacity = 1) => new(opacity, 0, 0, scale, scale);

    internal static MotionState Lerp(MotionState a, MotionState b, double t) => new(
        a.Opacity + ((b.Opacity - a.Opacity) * t),
        a.X + ((b.X - a.X) * t),
        a.Y + ((b.Y - a.Y) * t),
        a.ScaleX + ((b.ScaleX - a.ScaleX) * t),
        a.ScaleY + ((b.ScaleY - a.ScaleY) * t));
}

/// <summary>
/// A running (or finished) <see cref="Animator"/> animation. Every handle finishes exactly once — by reaching its end,
/// by <see cref="Complete"/>, by <see cref="Cancel"/>, or by being superseded by a newer animation on the same element —
/// and then raises its completion callback and completes <see cref="Finished"/>.
/// </summary>
public sealed class MotionHandle
{
    private readonly CompositionVisual? _visual;
    private readonly float _restOpacity;
    private readonly long _startedAt;
    private readonly TaskCompletionSource<bool> _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action<MotionHandle>? _onFinished;
    private IDisposable? _timer;
    private bool _superseded;

    internal MotionHandle(Visual target, CompositionVisual? visual, MotionState from, MotionState to, TimeSpan duration,
        Easing easing, RelativePoint origin, Action<MotionHandle>? onFinished)
    {
        Target = target;
        _visual = visual;
        _restOpacity = (float)target.Opacity;
        From = from;
        To = to;
        Duration = duration;
        Easing = easing;
        Origin = origin;
        _onFinished = onFinished;
        _startedAt = Stopwatch.GetTimestamp();
    }

    /// <summary>The element being animated.</summary>
    public Visual Target { get; }

    public MotionState From { get; }

    public MotionState To { get; }

    public TimeSpan Duration { get; }

    public Easing Easing { get; }

    /// <summary>The scale origin relative to the element (the transform origin; centre unless the caller gave one).</summary>
    public RelativePoint Origin { get; }

    /// <summary>True until the animation finishes (naturally, completed, cancelled or superseded).</summary>
    public bool IsAnimating { get; private set; }

    /// <summary>True when the handle finished through <see cref="Cancel"/> or was superseded by a newer animation.</summary>
    public bool WasCancelled { get; private set; }

    /// <summary>Completes with <c>true</c> when the end state was reached (naturally or via <see cref="Complete"/>),
    /// <c>false</c> when cancelled or superseded. Continuations run asynchronously.</summary>
    public Task<bool> Finished => _finished.Task;

    /// <summary>
    /// The UI-thread estimate of the state the render thread is showing now (eased interpolation by elapsed time).
    /// Used to start a follow-up animation from where an interrupted one is (e.g. a sliding indicator).
    /// </summary>
    public MotionState Current
    {
        get
        {
            if (!IsAnimating)
            {
                return WasCancelled ? MotionState.Rest : To;
            }

            var elapsed = Stopwatch.GetElapsedTime(_startedAt);
            var t = Duration <= TimeSpan.Zero ? 1 : Math.Clamp(elapsed / Duration, 0, 1);
            return MotionState.Lerp(From, To, Easing.Ease(t));
        }
    }

    /// <summary>Jumps to the end state (<see cref="To"/>) now and finishes.</summary>
    public void Complete() => Finish(To, cancelled: false);

    /// <summary>
    /// Stops the animation and restores the element's resting state (opacity as laid out, no offset, scale 1) — so a
    /// cancelled fade-out never leaves content invisible — then finishes with <see cref="WasCancelled"/> = true.
    /// </summary>
    public void Cancel() => Finish(MotionState.Rest, cancelled: true);

    internal void Start(Point centerPoint)
    {
        if (_visual is null || Duration <= TimeSpan.Zero)
        {
            // No composition visual yet (element not attached) or animations are off: show the end state at once.
            if (_visual is not null)
            {
                Animator.Snap(_visual, To, _restOpacity);
            }

            IsAnimating = true; // so Finish runs its bookkeeping once
            Finish(snapTo: null, cancelled: false);
            return;
        }

        IsAnimating = true;
        _visual.CenterPoint = new Vector3D(centerPoint.X, centerPoint.Y, 0);
        Animator.Run(_visual, From, To, _restOpacity, Duration, Easing);
        if (!Animator.HoldFinishTimers)
        {
            _timer = DispatcherTimer.RunOnce(() => Finish(snapTo: null, cancelled: false), Duration, DispatcherPriority.Render);
        }
    }

    /// <summary>Finishes because a newer animation took over the same element (its keyframes replace ours).</summary>
    internal void Supersede()
    {
        _superseded = true;
        Finish(snapTo: null, cancelled: true);
    }

    private void Finish(MotionState? snapTo, bool cancelled)
    {
        if (!IsAnimating)
        {
            return;
        }

        IsAnimating = false;
        WasCancelled = cancelled;
        _timer?.Dispose();
        _timer = null;

        if (snapTo is { } state && _visual is { IsDisposed: false })
        {
            Animator.Snap(_visual, state, _restOpacity);
        }

        // What the render thread keeps showing once this handle is done (Retarget starts from it). A superseded handle
        // records nothing: the newer animation on the element owns its state.
        if (_visual is not null && !_superseded)
        {
            Animator.Settle(Target, _visual, cancelled ? MotionState.Rest : To);
        }

        Animator.Unregister(this);
        _finished.TrySetResult(!cancelled);
        var callback = _onFinished;
        _onFinished = null;
        callback?.Invoke(this);
    }
}

/// <summary>
/// Render-thread animation helpers for content transitions (motion-spec "Навігаційні переходи", overlays, new rows) and
/// for the FLIP-style movement inside our controls (segment indicator, toggle knob, progress fill).
/// </summary>
/// <remarks>
/// <para><b>Composition-based (render thread).</b> Every helper here animates the element's composition visual
/// (<see cref="ElementComposition.GetElementVisual"/>) with keyframe animations on <c>Opacity</c>, <c>Translation</c> and
/// <c>Scale</c> (+ <c>CenterPoint</c> for the zoom origin). These run on the compositor clock, so they stay smooth while
/// the UI thread builds the next screen, and they never touch layout. They are explicit — no
/// <c>ImplicitAnimations</c> — so nothing animates by accident.</para>
/// <para><b>Resting state contract.</b> The UI-side properties (<c>Opacity</c>, <c>RenderTransform</c>, layout) are
/// never changed by the Animator; they always describe the element at rest. The animations are offsets from that rest
/// state (<see cref="MotionState"/>), so when an animation ends, is cancelled, or the composition visual is recreated, the
/// element shows its correct laid-out state. Avalonia writes <c>Opacity</c> to the composition visual only when the
/// UI-side value changes and never writes <c>Translation</c>/<c>Scale</c>, so these animations are not overwritten.
/// The one exception is an exit (<see cref="FadeOut"/>, <see cref="SlideTo"/>): when it completes the element stays
/// hidden on the render thread until the caller swaps content, starts an entrance, or calls <see cref="Reset"/>.</para>
/// <para><b>Avalonia Animation/Transitions (UI thread)</b> are used elsewhere only for brush/colour changes (hover
/// backgrounds, checkbox fill, text colour) and small opacity fades inside control templates: composition cannot
/// animate a brush inside drawn content, and those changes are cheap property updates.</para>
/// <para><b>Interruptions.</b> Starting an animation on an element that is already animating supersedes the old handle
/// (its callback runs with <see cref="MotionHandle.WasCancelled"/> = true) and the new keyframes replace the old ones in
/// the same compositor batch. Use <see cref="MotionHandle.Current"/> to start from where the old one was.</para>
/// <para>All durations go through <see cref="Motion.Ms"/>: with OS animations off they are zero and every helper
/// applies its end state immediately and finishes synchronously.</para>
/// <para>Blur (zoom 6→0 px) is intentionally not implemented: the spec marks it optional and it is costly.</para>
/// </remarks>
public static class Animator
{
    private static readonly ConditionalWeakTable<Visual, MotionHandle> s_running = new();

    /// <summary>The state each element was left in by its last finished animation, with the composition visual it was
    /// shown on (a recreated visual shows the element at rest again).</summary>
    private static readonly ConditionalWeakTable<Visual, Settled> s_settled = new();
    private static readonly LinearEasing s_linear = new();

    /// <summary>The 1 ms "snap" used to set a composition property immediately while overriding any running keyframes.</summary>
    private static readonly TimeSpan s_snap = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond);

    /// <summary>
    /// Test hook. While true, a handle started with a duration schedules no finishing timer: it keeps
    /// <see cref="MotionHandle.IsAnimating"/> until <see cref="MotionHandle.Complete"/>, <see cref="MotionHandle.Cancel"/> or
    /// a newer animation on the same element ends it, however long the test takes to pump frames. Headless tests use it to
    /// assert mid-animation state without depending on the wall clock (the finishing timer is a wall-clock
    /// <see cref="DispatcherTimer"/>). The composition animation itself still plays. Never set by the app.
    /// </summary>
    internal static bool HoldFinishTimers { get; set; }

    /// <summary>The animation currently running on <paramref name="target"/>, if any.</summary>
    public static MotionHandle? Running(Visual target) =>
        s_running.TryGetValue(target, out var handle) && handle.IsAnimating ? handle : null;

    /// <summary>True while an Animator animation runs on <paramref name="target"/>.</summary>
    public static bool IsAnimating(Visual target) => Running(target) is not null;

    /// <summary>
    /// The state <paramref name="target"/> is drawn in now, relative to its resting state: the running animation's
    /// <see cref="MotionHandle.Current"/>, else the end state the last animation left on its composition visual (a
    /// finished fade-out stays hidden; a cancelled animation, <see cref="Reset"/> or a recreated visual is at rest).
    /// </summary>
    public static MotionState Shown(Visual target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Running(target)?.Current ?? Left(target);
    }

    /// <summary>Where <paramref name="target"/> is heading: the running animation's <see cref="MotionHandle.To"/>, else
    /// the state it was left in (see <see cref="Shown"/>).</summary>
    public static MotionState Destination(Visual target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Running(target)?.To ?? Left(target);
    }

    /// <summary>
    /// Animates <paramref name="target"/> to <paramref name="to"/>, continuing from where it is drawn now (<see cref="Shown"/>:
    /// mid-way through a running animation, or the end state of the last one) — so a state that flips back and forth
    /// (a block dimming under the dragged copy and back) never jumps. When it is already at or heading to
    /// <paramref name="to"/>, nothing new starts: the running handle is returned, or a finished one.
    /// </summary>
    public static MotionHandle Retarget(Visual target, MotionState to, int durationMs, Easing easing,
        RelativePoint? origin = null, Action<MotionHandle>? onFinished = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Running(target) is { } running && running.To == to)
        {
            return running;
        }

        var from = Shown(target);
        return Animate(target, from, to, from == to ? 0 : durationMs, easing, origin, onFinished);
    }
    /// <summary>
    /// Animates <paramref name="target"/> from <paramref name="from"/> to <paramref name="to"/> (both relative to its
    /// resting state) over <paramref name="durationMs"/> (a <see cref="Motion"/> token).
    /// </summary>
    /// <param name="origin">Scale origin relative to the element (0..1 each axis); default centre.</param>
    /// <param name="onFinished">Called once on the UI thread when the handle finishes (also when cancelled).
    /// Called synchronously before this method returns when there is nothing to animate.</param>
    public static MotionHandle Animate(Visual target, MotionState from, MotionState to, int durationMs, Easing easing,
        RelativePoint? origin = null, Action<MotionHandle>? onFinished = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(easing);
        Dispatcher.UIThread.VerifyAccess();

        if (s_running.TryGetValue(target, out var previous))
        {
            previous.Supersede();
        }

        var visual = ElementComposition.GetElementVisual(target);
        var relative = origin ?? RelativePoint.Center;
        var handle = new MotionHandle(target, visual, from, to, Motion.Ms(durationMs), easing, relative, onFinished);
        s_running.AddOrUpdate(target, handle);
        handle.Start(relative.ToPixels(target.Bounds.Size));
        return handle;
    }

    /// <summary>Entrance: opacity 0→1 (ease-out). Default duration: section entrance (380 ms).</summary>
    public static MotionHandle FadeIn(Visual target, int durationMs = Motion.Section, Action<MotionHandle>? onFinished = null) =>
        Animate(target, MotionState.Hidden, MotionState.Rest, durationMs, Motion.EaseOut, null, onFinished);

    /// <summary>Exit: opacity 1→0 (ease-in). Default duration: screen exit (130 ms). The element stays hidden on the render
    /// thread after it completes (see remarks on <see cref="Animator"/>); <see cref="MotionHandle.Cancel"/> shows it again.</summary>
    public static MotionHandle FadeOut(Visual target, int durationMs = Motion.Exit, Action<MotionHandle>? onFinished = null) =>
        Animate(target, MotionState.Rest, MotionState.Hidden, durationMs, Motion.EaseIn, null, onFinished);

    /// <summary>
    /// Entrance: translate (<paramref name="x"/>, <paramref name="y"/>) → 0, optionally with opacity 0→1 (ease-out).
    /// Period slides: <c>SlideFrom(host, ±Motion.SlideX, 0, Motion.Slide)</c>; sections: <c>SlideFrom(host, 0, 18·dir, Motion.Section)</c>.
    /// </summary>
    public static MotionHandle SlideFrom(Visual target, double x, double y, int durationMs = Motion.Slide, bool fade = true,
        Action<MotionHandle>? onFinished = null) =>
        Animate(target, MotionState.Offset(x, y, fade ? 0 : 1), MotionState.Rest, durationMs, Motion.EaseOut, null, onFinished);

    /// <summary>
    /// Exit: translate 0 → (<paramref name="x"/>, <paramref name="y"/>), optionally with opacity 1→0 (ease-in).
    /// Section exit: <c>SlideTo(host, 0, -Motion.SectionExitY·dir, Motion.Exit)</c>.
    /// </summary>
    public static MotionHandle SlideTo(Visual target, double x, double y, int durationMs = Motion.Exit, bool fade = true,
        Action<MotionHandle>? onFinished = null) =>
        Animate(target, MotionState.Rest, MotionState.Offset(x, y, fade ? 0 : 1), durationMs, Motion.EaseIn, null, onFinished);

    /// <summary>
    /// Entrance: scale <paramref name="scale"/> → 1 around <paramref name="origin"/> (relative to the element), optionally
    /// with opacity 0→1 (ease-out). Zoom in: <c>ZoomFrom(host, Motion.ZoomInFrom, clickOrigin)</c>; zoom out:
    /// <c>ZoomFrom(host, Motion.ZoomOutFrom, new RelativePoint(0.5, 0.4, RelativeUnit.Relative))</c>.
    /// </summary>
    public static MotionHandle ZoomFrom(Visual target, double scale, RelativePoint origin, int durationMs = Motion.Zoom,
        bool fade = true, Action<MotionHandle>? onFinished = null) =>
        Animate(target, MotionState.Zoomed(scale, fade ? 0 : 1), MotionState.Rest, durationMs, Motion.EaseOut, origin, onFinished);

    /// <summary>
    /// FLIP helper for horizontal growth (progress bars): the element has just been given its new resting
    /// <c>ScaleX</c> by its owner; this animates from <paramref name="fromScaleX"/> (old ÷ new) to 1 around the left edge.
    /// </summary>
    public static MotionHandle ScaleXFrom(Visual target, double fromScaleX, int durationMs = Motion.Progress,
        Action<MotionHandle>? onFinished = null) =>
        Animate(target, new MotionState(1, 0, 0, fromScaleX, 1), MotionState.Rest, durationMs, Motion.EaseOut,
            new RelativePoint(0, 0.5, RelativeUnit.Relative), onFinished);

    /// <summary>Cancels any running animation on <paramref name="target"/> and shows it at rest.</summary>
    public static void Reset(Visual target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Running(target) is { } handle)
        {
            handle.Cancel();
            return;
        }

        if (ElementComposition.GetElementVisual(target) is { IsDisposed: false } visual)
        {
            Snap(visual, MotionState.Rest, (float)target.Opacity);
        }

        s_settled.Remove(target);
    }

    internal static void Settle(Visual target, CompositionVisual visual, MotionState state)
    {
        if (state == MotionState.Rest)
        {
            s_settled.Remove(target);
        }
        else
        {
            s_settled.AddOrUpdate(target, new Settled(visual, state));
        }
    }

    /// <summary>The state the last finished animation left on <paramref name="target"/>'s current composition visual.</summary>
    private static MotionState Left(Visual target) =>
        s_settled.TryGetValue(target, out var settled)
        && !settled.Visual.IsDisposed
        && ReferenceEquals(settled.Visual, ElementComposition.GetElementVisual(target))
            ? settled.State
            : MotionState.Rest;

    internal static void Unregister(MotionHandle handle)
    {
        if (s_running.TryGetValue(handle.Target, out var current) && ReferenceEquals(current, handle))
        {
            s_running.Remove(handle.Target);
        }
    }

    /// <summary>Sets the three animated properties to <paramref name="state"/> now, replacing any running keyframes.</summary>
    internal static void Snap(CompositionVisual visual, MotionState state, float restOpacity) =>
        Run(visual, state, state, restOpacity, s_snap, s_linear);

    internal static void Run(CompositionVisual visual, MotionState from, MotionState to, float restOpacity, TimeSpan duration,
        IEasing easing)
    {
        var compositor = visual.Compositor;

        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = duration;
        opacity.InsertKeyFrame(0f, (float)(from.Opacity * restOpacity));
        opacity.InsertKeyFrame(1f, (float)(to.Opacity * restOpacity), easing);
        visual.StartAnimation("Opacity", opacity);

        var translation = compositor.CreateVector3DKeyFrameAnimation();
        translation.Duration = duration;
        translation.InsertKeyFrame(0f, new Vector3D(from.X, from.Y, 0));
        translation.InsertKeyFrame(1f, new Vector3D(to.X, to.Y, 0), easing);
        visual.StartAnimation("Translation", translation);

        var scale = compositor.CreateVector3DKeyFrameAnimation();
        scale.Duration = duration;
        scale.InsertKeyFrame(0f, new Vector3D(from.ScaleX, from.ScaleY, 1));
        scale.InsertKeyFrame(1f, new Vector3D(to.ScaleX, to.ScaleY, 1), easing);
        visual.StartAnimation("Scale", scale);
    }
}

/// <summary>The end state an animation left on a composition visual.</summary>
internal sealed record Settled(CompositionVisual Visual, MotionState State);
