using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Doba.App.Screens.Week;

/// <summary>
/// Zoom origin for the calendar screens' header / cell / "+N more" buttons (motion spec "Точка зуму"): the clicked
/// element's centre as fractions of the content area, or null when the button was activated without a click
/// (Enter / Space), so the transition uses its default 50% 40%. Remembers the element under the last pointer press
/// (tunnel, so it sees presses the buttons handle) and forgets it on any key press.
/// </summary>
internal sealed class ZoomOrigin
{
    private readonly Control _host;
    private Visual? _pressed;

    public ZoomOrigin(Control host)
    {
        _host = host;
        host.AddHandler(InputElement.PointerPressedEvent, (_, e) => _pressed = e.Source as Visual, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.KeyDownEvent, (_, _) => _pressed = null, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>The origin for a button that was just activated; null unless a pointer press on it caused the click.</summary>
    public Point? For(Control source)
    {
        var pressed = _pressed;
        _pressed = null;
        return pressed is not null && (ReferenceEquals(pressed, source) || source.IsVisualAncestorOf(pressed)) ? Of(_host, source) : null;
    }

    /// <summary>Centre of <paramref name="source"/> relative to <paramref name="host"/>'s bounds, as fractions (0..1).</summary>
    public static Point? Of(Control host, Control source)
    {
        var size = host.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0
            || source.TranslatePoint(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2), host) is not { } centre)
        {
            return null;
        }

        return new Point(Math.Clamp(centre.X / size.Width, 0, 1), Math.Clamp(centre.Y / size.Height, 0, 1));
    }
}
