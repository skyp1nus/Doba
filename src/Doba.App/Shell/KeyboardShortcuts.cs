using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Doba.App.Shell;

/// <summary>
/// Window-level keys: N (New task; ignored while a text box has focus, while an overlay is open and in Settings, where
/// the New task button is hidden), Ctrl+K (search palette) and Esc (routed to the open overlay). Listens to bubbling
/// KeyDown, so focused controls handle their keys first. A later package extends this file (G I / G T, 1–4, arrows).
/// </summary>
public sealed class KeyboardShortcuts
{
    private readonly MainViewModel _vm;
    private readonly OverlayHost _overlays;

    public KeyboardShortcuts(InputElement root, MainViewModel vm, OverlayHost overlays)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(overlays);
        _vm = vm;
        _overlays = overlays;
        root.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>Applies one key press; true when it was a shortcut.</summary>
    public bool Handle(Key key, KeyModifiers modifiers, object? focused)
    {
        if (key == Key.Escape && modifiers == KeyModifiers.None)
        {
            return _overlays.HandleEscape();
        }

        if (_vm.Overlay is not null)
        {
            return false;
        }

        if (key == Key.K && modifiers == KeyModifiers.Control)
        {
            _vm.OpenSearch();
            return true;
        }

        if (key == Key.N && modifiers == KeyModifiers.None && focused is not TextBox && _vm.Header.ShowNewTask)
        {
            _vm.NewTask();
            return true;
        }

        return false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var focused = TopLevel.GetTopLevel(sender as Visual)?.FocusManager?.GetFocusedElement() ?? e.Source;
        e.Handled = Handle(e.Key, e.KeyModifiers, focused);
    }
}
