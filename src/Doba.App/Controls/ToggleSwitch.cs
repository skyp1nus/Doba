using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Doba.App.Motion;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Controls;

/// <summary>
/// The Settings switch: a 36×20 track (Brush.Bg.Control → accent, 200 ms) with a 16 px knob
/// (Brush.Knob.Off → Brush.Knob.On) that travels <see cref="M.ToggleTravel"/> px in 250 ms ease-out.
/// </summary>
/// <remarks>
/// Not Avalonia's ToggleSwitch: that one moves its knob with <c>Canvas.Left</c> (layout). Here the knob's resting
/// position is a render transform set by the theme (<c>:checked</c>), and the movement is a composition FLIP animation
/// (<see cref="Animator.SlideFrom"/>) started from wherever the knob is on screen, so rapid toggles stay continuous.
/// </remarks>
public class ToggleSwitch : ToggleButton
{
    private Control? _knob;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _knob = e.NameScope.Find<Control>("PART_Knob");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCheckedProperty && _knob is { } knob && IsLoaded)
        {
            // The :checked style has already moved the knob's resting position; animate from where it was drawn.
            var wasOn = change.GetOldValue<bool?>() == true;
            var oldRest = wasOn ? M.ToggleTravel : 0;
            var newRest = IsChecked == true ? M.ToggleTravel : 0;
            var shown = oldRest + (Animator.Running(knob)?.Current.X ?? 0);
            Animator.SlideFrom(knob, shown - newRest, 0, M.Toggle, fade: false);
        }
    }
}
