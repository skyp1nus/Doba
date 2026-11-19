using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using Doba.App.Motion;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Controls;

/// <summary>
/// The page header's Year / Month / Week / Day switch: 62×30 segments, gap 2, and one indicator (Brush.Bg.Selected,
/// radius 7) that slides under the selected segment (translateX, 420 ms ease-out) while the segment text colours change
/// (Brush.Text.Tertiary → Brush.Text.Primary, 250 ms). With <c>SelectedIndex = -1</c> (Inbox, Today, Settings) the
/// indicator fades out (250 ms) where it is; when a level is selected again it is placed under it at once and fades in
/// (the prototype creates a fresh switch when the calendar opens, so nothing slides). Before any level was selected it
/// rests under the first segment.
/// </summary>
/// <remarks>
/// A <see cref="ListBox"/> (keyboard arrows, selection, automation) with its own template
/// (Theme/Controls/Switches.axaml). The indicator's resting position is a render transform; the slide is a
/// composition FLIP animation (<see cref="Animator.SlideFrom"/>) started from where the indicator is drawn, so rapid
/// clicks continue smoothly instead of jumping.
/// </remarks>
public class SegmentedSwitch : ListBox
{
    private Control? _indicator;
    private readonly TranslateTransform _rest = new();
    private bool _placed;

    protected override Type StyleKeyOverride => typeof(SegmentedSwitch);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _indicator = e.NameScope.Find<Control>("PART_Indicator");
        if (_indicator is not null)
        {
            _indicator.RenderTransform = _rest;
        }

        _placed = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedIndexProperty)
        {
            // The indicator slides only between levels. Coming back from "no level" (Inbox / Today / Settings, where
            // the switch is hidden) it is placed under the level at once and only fades in, as the prototype's switch,
            // which is created fresh when the calendar opens.
            UpdateIndicator(animate: IsLoaded && IsEffectivelyVisible && change.GetOldValue<int>() >= 0);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        UpdateIndicator(animate: false);
        return size;
    }

    private void UpdateIndicator(bool animate)
    {
        if (_indicator?.GetVisualParent() is not Visual host)
        {
            return;
        }

        var index = SelectedIndex;
        var container = ContainerFromIndex(Math.Max(0, index));
        if (container is null || container.Bounds.Width <= 0)
        {
            return;
        }

        _indicator.Opacity = index < 0 ? 0 : 1;
        if (index < 0 && _placed)
        {
            return; // fades out where it is
        }

        if (container.TranslatePoint(default, host) is not { } target)
        {
            return;
        }

        if (!AreClose(_indicator.Width, container.Bounds.Width) ||
            !AreClose(_indicator.Height, container.Bounds.Height))
        {
            _indicator.Width = container.Bounds.Width;
            _indicator.Height = container.Bounds.Height;
        }

        var oldX = _rest.X;
        var oldY = _rest.Y;
        if (_placed && AreClose(oldX, target.X) && AreClose(oldY, target.Y))
        {
            return;
        }

        _rest.X = target.X;
        _rest.Y = target.Y;
        if (animate && _placed)
        {
            var current = Animator.Running(_indicator)?.Current ?? MotionState.Rest;
            Animator.SlideFrom(_indicator, oldX + current.X - target.X, oldY + current.Y - target.Y, M.SegIndicator, fade: false);
        }
        else if (Animator.IsAnimating(_indicator))
        {
            Animator.Reset(_indicator);
        }

        _placed = true;
    }

    private static bool AreClose(double a, double b) => Math.Abs(a - b) < 0.01;
}
