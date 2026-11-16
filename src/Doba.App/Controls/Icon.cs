using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Doba.App.Controls;

/// <summary>
/// A stroked vector icon drawn like the prototype's inline SVGs: <see cref="Data"/> is in a
/// <see cref="ViewBox"/>×<see cref="ViewBox"/> coordinate space (24 by default), scaled to <see cref="Size"/> px, and
/// <see cref="StrokeWidth"/> is in viewBox units (SVG <c>stroke-width</c>), so it scales with the icon.
/// Round caps and joins. The stroke uses the inherited text foreground unless <see cref="Foreground"/> is set.
/// </summary>
public class Icon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(Size), 16);

    public static readonly StyledProperty<double> ViewBoxProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(ViewBox), 24);

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeWidth), 1.6);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Icon, IBrush?>(nameof(Fill));

    private Pen? _pen;

    static Icon()
    {
        AffectsMeasure<Icon>(SizeProperty);
        AffectsRender<Icon>(DataProperty, SizeProperty, ViewBoxProperty, StrokeWidthProperty, ForegroundProperty, FillProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Rendered width and height in px.</summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Side of the square coordinate space of <see cref="Data"/> (24 for the design's icons, 10 for caption glyphs).</summary>
    public double ViewBox
    {
        get => GetValue(ViewBoxProperty);
        set => SetValue(ViewBoxProperty, value);
    }

    /// <summary>Stroke width in viewBox units, like SVG <c>stroke-width</c>.</summary>
    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Optional fill (most design icons are stroke-only).</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForegroundProperty || change.Property == StrokeWidthProperty)
        {
            _pen = null;
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data || Size <= 0 || ViewBox <= 0)
        {
            return;
        }

        _pen ??= Foreground is { } brush && StrokeWidth > 0
            ? new Pen(brush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
            : null;

        var scale = Size / ViewBox;
        var dx = (Bounds.Width - Size) / 2;
        var dy = (Bounds.Height - Size) / 2;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
        {
            context.DrawGeometry(Fill, _pen, data);
        }
    }
}
