using Avalonia;
using Avalonia.Animation;
using Avalonia.Data;
using Avalonia.VisualTree;

namespace Doba.App.Controls;

/// <summary>
/// Shows a new state at once: while the scope is open, the XAML transitions of a visual and its descendants are
/// switched off (any running one ends at its target) and every <see cref="StrikeText"/> under it snaps its strike colour.
/// Used when a recycled container is re-pointed at another item, which must not fade between the two items' states.
/// </summary>
internal sealed class SnapScope : IDisposable
{
    private readonly List<IDisposable> _restore = [];
    private readonly List<StrikeText> _strikes = [];

    private SnapScope(Visual root)
    {
        foreach (var visual in root.GetSelfAndVisualDescendants())
        {
            if (visual is StrikeText strike)
            {
                strike.Snapping = true;
                _strikes.Add(strike);
            }

            // Animation priority sits above local values and styles, so the visual's own Transitions come back
            // unchanged when it is disposed.
            if (visual.Transitions is { Count: > 0 }
                && visual.SetValue(Animatable.TransitionsProperty, null, BindingPriority.Animation) is { } undo)
            {
                _restore.Add(undo);
            }
        }
    }

    /// <summary>Opens a scope on <paramref name="root"/>; dispose it after the state change.</summary>
    public static SnapScope Open(Visual root) => new(root);

    public void Dispose()
    {
        foreach (var undo in _restore)
        {
            undo.Dispose();
        }

        foreach (var strike in _strikes)
        {
            strike.Snapping = false;
        }

        _restore.Clear();
        _strikes.Clear();
    }
}
