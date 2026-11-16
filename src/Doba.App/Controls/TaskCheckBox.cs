using Avalonia;
using Avalonia.Controls;

namespace Doba.App.Controls;

/// <summary>
/// The task completion checkbox: a <see cref="BoxSize"/> px square with a 1.5 px ring (Brush.Border.Check) that fills
/// with the accent and shows a dark check mark when checked (250 ms fill/ring, 200 ms check, `ease`).
/// Sizes used by the design: 16 (lists, cards, day spans; check 10 px), 14 (week bars; check 9 px),
/// 12 (year rows; check 8 px), 10 (month items; no check mark). Template: Theme/Controls/TaskCheckBox.axaml.
/// </summary>
public class TaskCheckBox : CheckBox
{
    public static readonly StyledProperty<double> BoxSizeProperty =
        AvaloniaProperty.Register<TaskCheckBox, double>(nameof(BoxSize), 16);

    public double BoxSize
    {
        get => GetValue(BoxSizeProperty);
        set => SetValue(BoxSizeProperty, value);
    }
}
