using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Doba.App.Motion;

/// <summary>
/// Keeps the XAML transitions of live visuals in step with <see cref="Motion.SystemAnimationsEnabled"/> when the OS
/// "Animation effects" setting changes while the app runs.
/// </summary>
/// <remarks>
/// <para><see cref="Animator"/> animations and code that calls <see cref="Motion.Ms"/> read the setting at call time,
/// and a template applied after the change gets the new <see cref="MotionTimes"/> value through its <c>x:Static</c>. Only
/// transitions that already exist keep the duration they captured. On <see cref="Motion.SystemAnimationsChanged"/> every
/// tracked window's visual tree is walked once (cheap; the setting rarely changes):</para>
/// <list type="bullet">
/// <item>animations turned <b>off</b>: every transition with a non-zero duration is set to zero and its duration is
/// remembered;</item>
/// <item>animations turned <b>on</b>: the remembered durations are put back. A transition created while animations were
/// off was built with zero and has no token to return to; it stays instant until its control is re-templated (any new
/// screen, row or dialog is) or the app restarts.</item>
/// </list>
/// <para>That includes the most common real case: Windows starts with "Animation effects" off, so
/// <see cref="Motion.SystemAnimationsEnabled"/> is false before the window is built and every template transition of the
/// first window is zero. Turning the setting on while the app runs then animates Animator motion (content transitions,
/// indicators, rows) at once, while the XAML hover / colour transitions of controls that already exist stay instant until
/// they are re-templated or the app restarts. Accepted: it only ever errs towards "no animation", which is what the user
/// asked for at start, and re-applying templates of a live window would rebuild visual trees.</para>
/// <para>Popups that live in their own top level (tooltips) are not walked; they are created on demand anyway.</para>
/// </remarks>
public static class MotionSync
{
    private static readonly ConditionalWeakTable<TransitionBase, StrongBox<TimeSpan>> s_zeroed = new();
    private static readonly List<WeakReference<TopLevel>> s_roots = [];

    private static bool s_subscribed;

    /// <summary>Re-times <paramref name="root"/>'s transitions whenever the OS animation setting changes, until it closes.</summary>
    public static void Track(TopLevel root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Dispatcher.UIThread.VerifyAccess();
        if (!s_subscribed)
        {
            s_subscribed = true;
            Motion.SystemAnimationsChanged += OnSystemAnimationsChanged;
        }

        s_roots.Add(new WeakReference<TopLevel>(root));
        if (root is Window window)
        {
            window.Closed += OnClosed;
        }
    }

    /// <summary>Applies the current <see cref="Motion.SystemAnimationsEnabled"/> to every transition under
    /// <paramref name="root"/> (see remarks). Returns how many transitions changed.</summary>
    public static int Retime(Visual root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var enabled = Motion.SystemAnimationsEnabled;
        var changed = 0;
        foreach (var visual in root.GetSelfAndVisualDescendants())
        {
            if (visual.Transitions is not { Count: > 0 } transitions)
            {
                continue;
            }

            foreach (var transition in transitions)
            {
                if (transition is TransitionBase t && Retime(t, enabled))
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    private static bool Retime(TransitionBase transition, bool enabled)
    {
        if (!enabled)
        {
            if (transition.Duration <= TimeSpan.Zero)
            {
                return false;
            }

            s_zeroed.AddOrUpdate(transition, new StrongBox<TimeSpan>(transition.Duration));
            transition.Duration = TimeSpan.Zero;
            return true;
        }

        if (!s_zeroed.TryGetValue(transition, out var original))
        {
            return false;
        }

        s_zeroed.Remove(transition);
        transition.Duration = original.Value;
        return true;
    }

    private static void OnSystemAnimationsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RetimeAll();
        }
        else
        {
            Dispatcher.UIThread.Post(RetimeAll);
        }
    }

    private static void RetimeAll()
    {
        s_roots.RemoveAll(r => !r.TryGetTarget(out _));
        foreach (var reference in s_roots.ToArray())
        {
            if (reference.TryGetTarget(out var root))
            {
                Retime(root);
            }
        }
    }

    private static void OnClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Closed -= OnClosed;
        s_roots.RemoveAll(r => !r.TryGetTarget(out var t) || ReferenceEquals(t, window));
    }
}
