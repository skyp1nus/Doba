using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Doba.App.Motion;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Controls;

/// <summary>
/// The 2 px progress line (day headers, months, Today summary): Brush.Bg.Track rail, radius 2, accent fill.
/// <see cref="Value"/> is 0..1. Changes animate the fill's <c>ScaleX</c> (origin left), never its width: the resting scale is a
/// render transform and the movement is a composition FLIP (<see cref="Animator.ScaleXFrom"/>, 500 ms ease-out).
/// </summary>
public class ProgressLine : TemplatedControl
{
    /// <summary>Smallest resting scale while animating towards 0 (a zero scale cannot be inverted for the FLIP).</summary>
    private const double MinScale = 1e-4;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ProgressLine, double>(nameof(Value), coerce: (_, v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1));

    private readonly ScaleTransform _scale = new(0, 1);
    private Control? _fill;

    /// <summary>Completed fraction, 0..1.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _fill = e.NameScope.Find<Control>("PART_Fill");
        if (_fill is not null)
        {
            _fill.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
            _fill.RenderTransform = _scale;
            _scale.ScaleX = Value;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ValueProperty || _fill is not { } fill)
        {
            return;
        }

        var target = change.GetNewValue<double>();
        if (!IsLoaded)
        {
            _scale.ScaleX = target;
            return;
        }

        var shown = _scale.ScaleX * (Animator.Running(fill)?.Current.ScaleX ?? 1);
        var rest = Math.Max(target, MinScale);
        _scale.ScaleX = rest;
        Animator.ScaleXFrom(fill, shown / rest, M.Progress, handle =>
        {
            if (!handle.WasCancelled && target < MinScale)
            {
                _scale.ScaleX = 0;
            }
        });
    }
}
