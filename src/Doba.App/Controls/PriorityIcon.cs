using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Doba.Core.Model;

namespace Doba.App.Controls;

/// <summary>
/// The priority glyph (13×13): three rising bars lit by rank (Low = 1, Medium = 2, High = 3; None = all off), or the
/// ASAP badge (a <see cref="AsapBrush"/> rounded square with a bold "!"). Colours come from the control theme
/// (Theme/Controls/TaskCheckBox.axaml): Brush.Priority.On / Brush.Priority.Off / Brush.Warning / Brush.Text.OnPrimary.
/// Not focusable: wrap it in a Button.Ghost for the click-to-cycle button (18×18, radius 4).
/// </summary>
public class PriorityIcon : Control
{
    /// <summary>Bars in the 13×13 viewBox: x, y, w, h (rx 1) — from the prototype's SVG.</summary>
    private static readonly Rect[] s_bars = [new(1, 8, 3, 4), new(5, 5, 3, 7), new(9, 2, 3, 10)];

    public static readonly StyledProperty<Priority> PriorityProperty =
        AvaloniaProperty.Register<PriorityIcon, Priority>(nameof(Priority));

    public static readonly StyledProperty<IBrush?> OnBrushProperty =
        AvaloniaProperty.Register<PriorityIcon, IBrush?>(nameof(OnBrush));

    public static readonly StyledProperty<IBrush?> OffBrushProperty =
        AvaloniaProperty.Register<PriorityIcon, IBrush?>(nameof(OffBrush));

    public static readonly StyledProperty<IBrush?> AsapBrushProperty =
        AvaloniaProperty.Register<PriorityIcon, IBrush?>(nameof(AsapBrush));

    public static readonly StyledProperty<IBrush?> AsapForegroundProperty =
        AvaloniaProperty.Register<PriorityIcon, IBrush?>(nameof(AsapForeground));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<PriorityIcon>();

    private const double Side = 13;
    private const double AsapRadius = 3;
    private const double AsapFontSize = 10;

    private FormattedText? _bang;

    static PriorityIcon()
    {
        AffectsRender<PriorityIcon>(PriorityProperty, OnBrushProperty, OffBrushProperty, AsapBrushProperty,
            AsapForegroundProperty, FontFamilyProperty);
    }

    public Priority Priority
    {
        get => GetValue(PriorityProperty);
        set => SetValue(PriorityProperty, value);
    }

    public IBrush? OnBrush
    {
        get => GetValue(OnBrushProperty);
        set => SetValue(OnBrushProperty, value);
    }

    public IBrush? OffBrush
    {
        get => GetValue(OffBrushProperty);
        set => SetValue(OffBrushProperty, value);
    }

    public IBrush? AsapBrush
    {
        get => GetValue(AsapBrushProperty);
        set => SetValue(AsapBrushProperty, value);
    }

    public IBrush? AsapForeground
    {
        get => GetValue(AsapForegroundProperty);
        set => SetValue(AsapForegroundProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>Number of lit bars for a priority (ASAP draws the badge instead).</summary>
    public static int Rank(Priority priority) => priority switch
    {
        Priority.Low => 1,
        Priority.Medium => 2,
        Priority.High => 3,
        _ => 0,
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FontFamilyProperty || change.Property == AsapForegroundProperty)
        {
            _bang = null;
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(Side, Side);

    public override void Render(DrawingContext context)
    {
        var origin = new Point(Math.Round((Bounds.Width - Side) / 2), Math.Round((Bounds.Height - Side) / 2));
        if (Priority == Priority.Asap)
        {
            context.DrawRectangle(AsapBrush, null, new RoundedRect(new Rect(origin, new Size(Side, Side)), AsapRadius));
            _bang ??= new FormattedText("!", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold), AsapFontSize, AsapForeground);
            context.DrawText(_bang, new Point(
                origin.X + ((Side - _bang.WidthIncludingTrailingWhitespace) / 2),
                origin.Y + ((Side - _bang.Height) / 2)));
            return;
        }

        var rank = Rank(Priority);
        for (var i = 0; i < s_bars.Length; i++)
        {
            var bar = s_bars[i].Translate(new Vector(origin.X, origin.Y));
            context.DrawRectangle(i < rank ? OnBrush : OffBrush, null, new RoundedRect(bar, 1));
        }
    }
}
