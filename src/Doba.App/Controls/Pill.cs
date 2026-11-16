using Avalonia;
using Avalonia.Controls.Primitives;

namespace Doba.App.Controls;

/// <summary>
/// A task-dialog pill (Date / Time / Priority / Tag): 30 px tall, radius 8, padding 0 11, 12.5 px Medium, gap 7
/// between its icon and label (put both in the content). Three looks, 180 ms transitions:
/// <list type="bullet">
/// <item>unset — dashed 1 px Brush.Border.Strong, transparent, Brush.Text.Muted;</item>
/// <item><see cref="HasValue"/> — solid Brush.Border.Strong, Brush.Overlay.Field, Brush.Text.Primary;</item>
/// <item>open (<c>IsChecked</c>, its popover is showing) — solid Brush.Border.Emphasis, Brush.Bg.Selected.</item>
/// </list>
/// Hover adds the .ob overlay (Brush.Overlay.Hover). Template: Theme/Controls/Switches.axaml.
/// </summary>
public class Pill : ToggleButton
{
    public static readonly StyledProperty<bool> HasValueProperty =
        AvaloniaProperty.Register<Pill, bool>(nameof(HasValue));

    /// <summary>True when the pill holds a value (solid look); false shows the dashed "not set" look.</summary>
    public bool HasValue
    {
        get => GetValue(HasValueProperty);
        set => SetValue(HasValueProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HasValueProperty)
        {
            if (change.GetNewValue<bool>())
            {
                PseudoClasses.Add(":set");
            }
            else
            {
                PseudoClasses.Remove(":set");
            }
        }
    }
}
