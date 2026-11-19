using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Doba.App.Controls;

/// <summary>
/// The keyboard focus ring (the design's <c>:focus-visible</c> outline: 1 px Brush.Border.Focus). Used as the app-wide
/// focus adorner (Theme/Controls/Base.axaml sets it as AdornerLayer.DefaultFocusAdorner), so every focusable control gets
/// it — and Avalonia shows focus adorners only for keyboard (Tab / arrow) focus, never on a mouse click.
/// The ring follows the adorned control's corner radius, inset 2 px like the design's <c>outline-offset: -2px</c> (the
/// adorner layer clips to the control's bounds, so an outside ring would be cut off).
/// </summary>
public class FocusRing : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<FocusRing, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<FocusRing, double>(nameof(StrokeThickness), 1);

    /// <summary>Distance between the control's edge and the ring (negative = inside, like CSS outline-offset).</summary>
    public static readonly StyledProperty<double> OffsetProperty =
        AvaloniaProperty.Register<FocusRing, double>(nameof(Offset), -2);

    static FocusRing()
    {
        AffectsRender<FocusRing>(StrokeProperty, StrokeThicknessProperty, OffsetProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<FocusRing>(false);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double Offset
    {
        get => GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Stroke is null || StrokeThickness <= 0)
        {
            return;
        }

        var adorned = AdornerLayer.GetAdornedElement(this);
        var radius = adorned switch
        {
            TemplatedControl t => t.CornerRadius,
            Border b => b.CornerRadius,
            _ => default,
        };

        // Centre the stroke on a rect Offset px outside (negative: inside) the bounds; half the thickness keeps it crisp.
        var inflate = Offset + (StrokeThickness / 2);
        var rect = new Rect(Bounds.Size).Inflate(inflate);
        var r = new CornerRadius(
            Grow(radius.TopLeft, inflate), Grow(radius.TopRight, inflate),
            Grow(radius.BottomRight, inflate), Grow(radius.BottomLeft, inflate));
        context.DrawRectangle(null, new Pen(Stroke, StrokeThickness), new RoundedRect(rect, r));
    }

    private static double Grow(double radius, double by) => radius > 0 ? Math.Max(0, radius + by) : 0;
}
