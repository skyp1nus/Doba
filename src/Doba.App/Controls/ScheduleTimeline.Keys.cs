using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Doba.Core.Editing;

namespace Doba.App.Controls;

/// <summary>
/// The schedule's keyboard (motion spec «Клавіатура», the prototype's <c>sbKey</c>), on a focused block's open button:
/// <list type="bullet">
/// <item>Enter opens the task (the button's own click).</item>
/// <item>Space marks it done or not done, once per press (auto-repeat does nothing; the key-up is swallowed so the
/// button does not click).</item>
/// <item>↑ / ↓ move it to the previous / next 15-minute line (Alt: 5), Shift+↑ / ↓ move its end instead, Alt+Shift both.
/// Every step that changes something is one store write (auto-repeat included); the block keeps its container and the
/// focus (or, folded under a "+N", the focus goes to that "+N"), the time chip shows the new start (Shift: the new end)
/// for <see cref="Motion.MotionTimes.ChipHold"/>, and the live region says "Moved / Resized Title, a to b". The arrows are
/// taken at the 07:00 / 23:00 edges too, so the scroller never scrolls instead.</item>
/// </list>
/// ← / → are left alone (Day: the previous / next day).
/// </summary>
public partial class ScheduleTimeline
{
    /// <summary>Space went down on a block (its key-up is swallowed, so the open button does not click).</summary>
    private bool _spaceDown;

    /// <summary>
    /// Space and the arrows on a focused block; Enter or Space on a "+N" remembers that its list is opened from the
    /// keyboard. Tunnel on the grid, so the open button never sees Space and the scroller never sees ↑ / ↓.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        var button = source as Button ?? source.FindAncestorOfType<Button>();
        if (button is { Name: "PART_More" } && e.Key is Key.Enter or Key.Space)
        {
            _openByKey = true;
            return;
        }

        if (button is not { Name: "PART_Open", DataContext: ScheduleBlockViewModel block })
        {
            return;
        }

        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            if (!_spaceDown)
            {
                _spaceDown = true;
                block.Row.ToggleCommand.Execute(null);
            }

            return;
        }

        // Ctrl / Win + arrow is not a schedule key: it passes on.
        if (e.Key is Key.Up or Key.Down && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0)
        {
            e.Handled = true;
            StepByKey(block, e.Key == Key.Down, e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Alt));
        }
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceDown)
        {
            _spaceDown = false;
            e.Handled = true;
        }
    }

    /// <summary>
    /// One arrow-key step of <paramref name="block"/>: <see cref="ScheduleViewModel.Step"/> writes it (or nothing at an
    /// edge); the block's view-model and container are updated in place, so the focus stays — unless the block is now
    /// folded under a "+N", which then takes the focus.
    /// </summary>
    private void StepByKey(ScheduleBlockViewModel block, bool down, bool shift, bool fine)
    {
        if (_vm is not { } vm)
        {
            return;
        }

        // FLIP: the geometry before the write comes from the view-models (the re-projection after the write is
        // synchronous, so the new geometry is in place when Step returns).
        var before = vm.Blocks.ToDictionary(b => b.Id, b => new Rect(b.X, b.Top, b.Width, b.Height));
        if (vm.Step(block, down, shift, fine) is not { } mode)
        {
            return;
        }

        if (!vm.Blocks.Contains(block) && vm.OverflowOf(block.Id) is { } overflow)
        {
            UpdateLayout();
            MoreOf(overflow)?.Focus(NavigationMethod.Tab);
        }

        OnKeyStepped(block, mode, before);
    }

    /// <summary>
    /// Hook for the step animations (motion spec: the block slides from its old place in <c>Dur.Snap</c>, its neighbours
    /// in <c>Dur.Land</c>): called after a key step was written and re-projected, with every block's geometry before the
    /// write. <paramref name="block"/> may be folded under a "+N" now (no container).
    /// </summary>
    partial void OnKeyStepped(ScheduleBlockViewModel block, ScheduleDragMode mode, IReadOnlyDictionary<long, Rect> before);
}
