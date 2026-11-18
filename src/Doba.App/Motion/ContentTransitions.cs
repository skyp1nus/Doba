using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Doba.App.Shell;
using Doba.Core.Navigation;

namespace Doba.App.Motion;

/// <summary>
/// The motion spec's navigation transitions ("Навігаційні переходи"; prototype script 1178–1261) around the shell's
/// content swap. The default <see cref="ShellComposition.Transitions"/>.
/// </summary>
/// <remarks>
/// <para>Only the content area (the screen host), the page title (h1) and, on Settings in/out, the sidebar that appears
/// move. Everything is an <see cref="Animator"/> composition animation of opacity / translation / scale relative to the
/// laid-out state, so nothing here touches layout and the screens' visual trees are never rebuilt (the screen host keeps
/// every view it has created; the swap only flips visibility).</para>
/// <list type="table">
/// <item><term>ZoomIn / ZoomOut</term><description>swap, then opacity 0→1, scale 0.86→1 / 1.10→1, 460 ms ease-out, origin =
/// the click point (fractions of the content area) or 50 % 40 %. Blur is not drawn (DECISIONS: optional and costly).</description></item>
/// <item><term>Next / Prev</term><description>swap, then opacity 0→1, translateX ±32→0, 340 ms ease-out.</description></item>
/// <item><term>SectionDown / SectionUp</term><description>exit opacity 1→0, translateY ∓10 px, 130 ms ease-in; when it has
/// finished: swap, reset the exit, then opacity 0→1, translateY ±18→0, 380 ms ease-out.</description></item>
/// <item><term>SettingsIn / SettingsOut</term><description>content like ZoomIn / ZoomOut around 50 % 30 %; the settings
/// sidebar enters from +16 px / the app sidebar from −16 px with a fade, 340 ms ease-out.</description></item>
/// <item><term>Title</term><description>after every swap the h1 plays opacity 0→1, translateY 6→0, 360 ms ease-out.</description></item>
/// <item><term>Launch</term><description>content opacity 0→1, translateY 12→0, 520 ms ease-out.</description></item>
/// </list>
/// <para>A new <see cref="Run"/> or <see cref="Launch"/> interrupts the running one: a swap still waiting for its exit is
/// invoked at once and every target is reset to rest before the new step starts, so swaps are never lost or reordered and
/// nothing is left invisible. With OS animations off every duration is zero and each step ends at once in the same final
/// state.</para>
/// </remarks>
public sealed class ContentTransitions : IContentTransitions
{
    private readonly ConditionalWeakTable<Control, Session> _sessions = new();

    /// <summary>Zoom origin without a click point: 50 % 40 % of the content area.</summary>
    public static RelativePoint DefaultZoomOrigin { get; } = new(Motion.ZoomOriginX, Motion.ZoomOriginY, RelativeUnit.Relative);

    /// <summary>Zoom origin for entering and leaving Settings: 50 % 30 %.</summary>
    public static RelativePoint SettingsZoomOrigin { get; } = new(Motion.ZoomOriginX, Motion.SettingsOriginY, RelativeUnit.Relative);

    /// <inheritdoc />
    public void Run(TransitionKind kind, Point? origin, Action swap, ContentTransitionTargets targets)
    {
        ArgumentNullException.ThrowIfNull(swap);
        ArgumentNullException.ThrowIfNull(targets);
        Dispatcher.UIThread.VerifyAccess();
        SessionFor(targets).Run(kind, origin, swap);
    }

    /// <inheritdoc />
    public void Launch(ContentTransitionTargets targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        Dispatcher.UIThread.VerifyAccess();
        SessionFor(targets).Launch();
    }

    /// <summary>True while a section exit has not yet swapped the content of <paramref name="targets"/>.</summary>
    public bool IsSwapPending(ContentTransitionTargets targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return _sessions.TryGetValue(targets.Content, out var session) && session.IsSwapPending;
    }

    private Session SessionFor(ContentTransitionTargets targets)
    {
        var session = _sessions.GetValue(targets.Content, static _ => new Session());
        session.Targets = targets;
        return session;
    }

    /// <summary>The transition state of one window's content area.</summary>
    private sealed class Session
    {
        private Action? _pendingSwap;
        private int _generation;

        public ContentTransitionTargets Targets { get; set; } = null!;

        public bool IsSwapPending => _pendingSwap is not null;

        public void Run(TransitionKind kind, Point? origin, Action swap)
        {
            Interrupt();
            if (!Motion.SystemAnimationsEnabled)
            {
                swap(); // every duration is zero: the same end state, without creating any animation
                return;
            }

            if (kind is TransitionKind.SectionDown or TransitionKind.SectionUp)
            {
                Exit(kind, swap);
                return;
            }

            var generation = _generation;
            swap();
            if (kind != TransitionKind.None && generation == _generation)
            {
                Enter(kind, origin);
            }
        }

        public void Launch()
        {
            Interrupt();
            if (!Motion.SystemAnimationsEnabled)
            {
                return;
            }

            Animator.Animate(Targets.Content, MotionState.Offset(0, Motion.LaunchY, 0), MotionState.Rest, Motion.Launch,
                Motion.EaseOut);
        }

        /// <summary>Ends whatever runs: the waiting swap happens now, every target shows its resting state.</summary>
        private void Interrupt()
        {
            _generation++;
            var swap = _pendingSwap;
            _pendingSwap = null;
            // Cancelling restores the resting state. A target with nothing running is already at rest: a finished
            // section exit is always followed at once by its reset and entrance.
            var t = Targets;
            Animator.Running(t.Content)?.Cancel();
            Animator.Running(t.Title)?.Cancel();
            if (t.AppSidebar is { } app)
            {
                Animator.Running(app)?.Cancel();
            }

            if (t.SettingsSidebar is { } settings)
            {
                Animator.Running(settings)?.Cancel();
            }

            swap?.Invoke();
        }

        /// <summary>Section exit: the old screen leaves (opposite to where the new one comes from), then the swap.</summary>
        private void Exit(TransitionKind kind, Action swap)
        {
            var dir = Direction(kind);
            var generation = _generation;
            _pendingSwap = swap;
            // With animations off the callback runs inside Animate, before it returns: the guard is the generation.
            Animator.Animate(Targets.Content, MotionState.Rest, MotionState.Offset(0, -Motion.SectionExitY * dir, 0),
                Motion.Exit, Motion.EaseIn, null, _ => AfterExit(kind, generation));
        }

        private void AfterExit(TransitionKind kind, int generation)
        {
            if (generation != _generation || _pendingSwap is not { } swap)
            {
                return; // interrupted: Interrupt already swapped and reset
            }

            _pendingSwap = null;
            swap();
            if (generation != _generation)
            {
                return; // the swap itself started another step
            }

            // The finished exit holds opacity 0 on the render thread: reset it before the entrance (spec).
            Animator.Reset(Targets.Content);
            Enter(kind, null);
        }

        private void Enter(TransitionKind kind, Point? origin)
        {
            var t = Targets;
            switch (kind)
            {
                case TransitionKind.ZoomIn:
                    Zoom(t.Content, Motion.ZoomInFrom, Origin(origin));
                    break;
                case TransitionKind.ZoomOut:
                    Zoom(t.Content, Motion.ZoomOutFrom, Origin(origin));
                    break;
                case TransitionKind.Next or TransitionKind.Prev:
                    var x = kind == TransitionKind.Next ? Motion.SlideX : -Motion.SlideX;
                    Animator.Animate(t.Content, MotionState.Offset(x, 0, 0), MotionState.Rest, Motion.Slide, Motion.EaseOut);
                    break;
                case TransitionKind.SectionDown or TransitionKind.SectionUp:
                    Animator.Animate(t.Content, MotionState.Offset(0, Motion.SectionEnterY * Direction(kind), 0), MotionState.Rest,
                        Motion.Section, Motion.EaseOut);
                    break;
                case TransitionKind.SettingsIn:
                    Zoom(t.Content, Motion.ZoomInFrom, SettingsZoomOrigin);
                    SlideSidebar(t.SettingsSidebar, Motion.SettingsSidebarX);
                    break;
                case TransitionKind.SettingsOut:
                    Zoom(t.Content, Motion.ZoomOutFrom, SettingsZoomOrigin);
                    SlideSidebar(t.AppSidebar, -Motion.SettingsSidebarX);
                    break;
                default:
                    return;
            }

            Animator.Animate(t.Title, MotionState.Offset(0, Motion.TitleY, 0), MotionState.Rest, Motion.Title, Motion.EaseOut);
        }

        private static void Zoom(Control content, double scale, RelativePoint origin) =>
            Animator.Animate(content, MotionState.Zoomed(scale, 0), MotionState.Rest, Motion.Zoom, Motion.EaseOut, origin);

        private static void SlideSidebar(Control? sidebar, double x)
        {
            if (sidebar is not null)
            {
                Animator.Animate(sidebar, MotionState.Offset(x, 0, 0), MotionState.Rest, Motion.Slide, Motion.EaseOut);
            }
        }

        private static RelativePoint Origin(Point? origin) =>
            origin is { } p ? new RelativePoint(p.X, p.Y, RelativeUnit.Relative) : DefaultZoomOrigin;

        /// <summary>+1: the new section has a larger index and comes from below; −1: it comes from above.</summary>
        private static int Direction(TransitionKind kind) => kind == TransitionKind.SectionDown ? 1 : -1;
    }
}
