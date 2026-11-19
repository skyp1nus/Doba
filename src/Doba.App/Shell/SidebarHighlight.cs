using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Doba.App.Motion;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Shell;

/// <summary>
/// The app sidebar's selection pill (motion spec "Індикатори"): one rectangle under Inbox / Today / Calendar that
/// moves by <see cref="M.NavStep"/> (32 px row + 1 px gap) per <see cref="Index"/>, 380 ms ease-out.
/// </summary>
/// <remarks>
/// Its resting position is a <see cref="TranslateTransform"/> (Y = Index × NavStep), never Margin/Top. A change after
/// the first layout plays a composition FLIP (<see cref="Animator.SlideFrom"/>) from where the pill is drawn now, so
/// rapid clicks continue from the current position instead of jumping (same technique as SegmentedSwitch).
/// </remarks>
public class SidebarHighlight : Border
{
    public static readonly StyledProperty<int> IndexProperty =
        AvaloniaProperty.Register<SidebarHighlight, int>(nameof(Index));

    private readonly TranslateTransform _rest = new();

    public SidebarHighlight()
    {
        RenderTransform = _rest;
        IsHitTestVisible = false;
    }

    protected override Type StyleKeyOverride => typeof(Border);

    /// <summary>Row index: Inbox 0, Today 1, Calendar 2.</summary>
    public int Index
    {
        get => GetValue(IndexProperty);
        set => SetValue(IndexProperty, value);
    }

    /// <summary>The resting offset in px (Index × NavStep); what the pill shows once any slide has finished.</summary>
    public double RestY => _rest.Y;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IndexProperty)
        {
            MoveTo(Index * M.NavStep, animate: IsLoaded && IsEffectivelyVisible);
        }
    }

    private void MoveTo(double y, bool animate)
    {
        var oldY = _rest.Y;
        if (Math.Abs(oldY - y) < 0.01)
        {
            return;
        }

        _rest.Y = y;
        if (animate)
        {
            var current = Animator.Running(this)?.Current ?? MotionState.Rest;
            Animator.SlideFrom(this, 0, oldY + current.Y - y, M.Indicator, fade: false);
        }
        else
        {
            Animator.Reset(this);
        }
    }
}
