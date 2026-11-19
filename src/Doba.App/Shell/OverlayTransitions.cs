using Avalonia;
using Avalonia.Controls;
using Doba.App.Motion;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Shell;

/// <summary>
/// The overlay motion of the motion spec ("Оверлеї"), the default <see cref="OverlayHost.OverlayTransitions"/>:
/// <list type="bullet">
/// <item>open — shade opacity 0→1 over <see cref="M.Shade"/> (220 ms) and the panel opacity 0→1, translateY
/// <see cref="M.DialogY"/>→0, scale <see cref="M.DialogScale"/>→1 over <see cref="M.DialogIn"/> (320 ms), both ease-out;
/// the host is told when the panel's entrance has ended so focus goes to the title field only then;</item>
/// <item>close — shade opacity 1→0 and the panel opacity 1→0, translateY 0→<see cref="M.DialogExitY"/>, scale
/// 1→<see cref="M.DialogExitScale"/>, both over <see cref="M.DialogExit"/> (150 ms) ease-in; <c>done</c> runs once, when
/// the panel's exit ends (the host then removes the overlay and the dialog commits its save);</item>
/// <item>replace (an overlay replacing the open one at once, e.g. a search hit opening the task dialog) — the shade stays
/// as it is and only the new panel enters.</item>
/// </list>
/// </summary>
/// <remarks>
/// Composition animations through <see cref="Animator"/> (opacity and transform offsets on the render thread, nothing
/// re-laid out). A new open or close supersedes the running one on the same elements: a superseded or cancelled exit
/// still calls <c>done</c> exactly once (its callback runs with <see cref="MotionHandle.WasCancelled"/>), so the host
/// never waits for a close that will not finish. If the overlay is removed before the exit ends
/// (<see cref="MainViewModel.CompletePendingOverlayClose"/>), the exit keeps running on the hidden layer and its late
/// <c>done</c> is ignored by the host. With OS animations off every animation ends at once and <c>opened</c> /
/// <c>done</c> run synchronously.
/// </remarks>
public sealed class AnimatedOverlayTransitions : IOverlayTransitions
{
    /// <summary>The panel's state when its entrance starts: invisible, <see cref="M.DialogY"/> px lower, scaled
    /// <see cref="M.DialogScale"/>.</summary>
    public static MotionState PanelEnterFrom { get; } = new(0, 0, M.DialogY, M.DialogScale, M.DialogScale);

    /// <summary>The panel's state when its exit ends: invisible, <see cref="M.DialogExitY"/> px lower, scaled
    /// <see cref="M.DialogExitScale"/>.</summary>
    public static MotionState PanelExitTo { get; } = new(0, 0, M.DialogExitY, M.DialogExitScale, M.DialogExitScale);

    /// <inheritdoc />
    public void Open(Control shade, Control panel) => Open(shade, panel, static () => { });

    /// <inheritdoc />
    public void Open(Control shade, Control panel, Action opened)
    {
        ArgumentNullException.ThrowIfNull(shade);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(opened);

        // An overlay opened while the previous one is still leaving starts from where that exit is drawn.
        Animator.Animate(shade, Shown(shade, MotionState.Hidden), MotionState.Rest, M.Shade, M.EaseOut);
        Animator.Animate(panel, Shown(panel, PanelEnterFrom), MotionState.Rest, M.DialogIn, M.EaseOut, RelativePoint.Center,
            _ => opened());
    }

    /// <inheritdoc />
    /// <remarks>The shade keeps what it is doing (at rest, or still entering); only the new panel enters, as in
    /// <see cref="Open(Control, Control, Action)"/>.</remarks>
    public void Replace(Control shade, Control panel, Action opened)
    {
        ArgumentNullException.ThrowIfNull(shade);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(opened);
        Animator.Animate(panel, PanelEnterFrom, MotionState.Rest, M.DialogIn, M.EaseOut, RelativePoint.Center, _ => opened());
    }

    /// <inheritdoc />
    public void Close(Control shade, Control panel, Action done)
    {
        ArgumentNullException.ThrowIfNull(shade);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(done);
        var called = false;

        // Closed during the entrance: the exit starts from where the entrance is drawn, not from rest.
        Animator.Animate(shade, Shown(shade, MotionState.Rest), MotionState.Hidden, M.DialogExit, M.EaseIn);
        Animator.Animate(panel, Shown(panel, MotionState.Rest), PanelExitTo, M.DialogExit, M.EaseIn, RelativePoint.Center, _ =>
        {
            if (!called)
            {
                called = true;
                done();
            }
        });
    }

    /// <summary>Where a running animation currently draws <paramref name="target"/>, or <paramref name="fallback"/>.</summary>
    private static MotionState Shown(Visual target, MotionState fallback) => Animator.Running(target)?.Current ?? fallback;
}
