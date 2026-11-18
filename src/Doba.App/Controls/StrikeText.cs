using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Controls;

/// <summary>
/// A <see cref="TextBlock"/> for task titles whose strikethrough is always present and only changes colour
/// (motion spec: "the strike is never added or removed; its colour animates transparent → Text.Muted", 300 ms `ease`),
/// so completing a task never re-lays out the text. Set <see cref="Struck"/> (usually bound to <c>Done</c>) and
/// <see cref="StrikeColor"/> (<c>{DynamicResource Color.Text.Muted}</c>). The stroke is a per-control mutable brush
/// whose colour transition runs on the compositor; the text colour itself comes from the inherited foreground.
/// Styled as a plain TextBlock (classes such as <c>meta-sm</c> apply).
/// </summary>
/// <remarks>
/// The line is drawn the way Chromium paints <c>text-decoration: line-through</c>, not with Avalonia's
/// <see cref="TextDecoration"/> (which uses the font's fractional strikeout thickness and position and smears the line
/// over two half-lit pixel rows): one device pixel thick, on a whole pixel row. With the baseline B on the pixel grid,
/// the row is [B − n, B − n + 1) with n = ⌈strikeout position + ½⌉ device pixels — n = 5 for the 11.5–13.5 px titles,
/// as measured in the reference renders (Month, Week, Today, Day, schedule blocks).
/// </remarks>
public class StrikeText : TextBlock
{
    public static readonly StyledProperty<bool> StruckProperty =
        AvaloniaProperty.Register<StrikeText, bool>(nameof(Struck));

    public static readonly StyledProperty<Color> StrikeColorProperty =
        AvaloniaProperty.Register<StrikeText, Color>(nameof(StrikeColor));

    private readonly SolidColorBrush _stroke = new();
    private double _strikePosition = double.NaN;

    public StrikeText() => UpdateStroke();

    /// <summary>Show the strikethrough (fades in); false fades it out.</summary>
    public bool Struck
    {
        get => GetValue(StruckProperty);
        set => SetValue(StruckProperty, value);
    }

    /// <summary>Colour of the visible strike (Color.Text.Muted in the design).</summary>
    public Color StrikeColor
    {
        get => GetValue(StrikeColorProperty);
        set => SetValue(StrikeColorProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <summary>The colour the strike currently rests at (the transition target); for tests.</summary>
    internal Color StrokeColor => _stroke.Color;

    /// <summary>
    /// Top of the strike row, in the same coordinates as <paramref name="baseline"/>: the baseline snapped to the
    /// device pixel grid, minus ⌈<paramref name="strikePosition"/> + ½⌉ device pixels.
    /// </summary>
    public static double StrikeTop(double baseline, double strikePosition, double renderScaling)
    {
        var scale = renderScaling > 0 ? renderScaling : 1;
        return (Math.Round(baseline * scale) - Math.Ceiling((strikePosition * scale) + 0.5)) / scale;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Transitions start once the control is shown, so a card that appears already done shows its strike at once.
        if (_stroke.Transitions is null && M.Ms(M.Strike) > TimeSpan.Zero)
        {
            _stroke.Transitions =
            [
                new ColorTransition { Property = SolidColorBrush.ColorProperty, Duration = M.Ms(M.Strike), Easing = M.EaseStd },
            ];
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StruckProperty || change.Property == StrikeColorProperty)
        {
            UpdateStroke();
        }
        else if (change.Property == FontSizeProperty || change.Property == FontFamilyProperty
                 || change.Property == FontWeightProperty || change.Property == FontStyleProperty)
        {
            _strikePosition = double.NaN;
        }
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        base.RenderTextLayout(context, origin);

        // Always drawn (transparent when not struck), so the colour can fade without a re-render.
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var position = StrikePosition();
        var y = origin.Y;
        foreach (var line in TextLayout.TextLines)
        {
            if (line.Width > 0)
            {
                var top = StrikeTop(y + line.Baseline, position, scale);
                context.FillRectangle(_stroke, new Rect(origin.X + line.Start, top, line.Width, 1 / scale));
            }

            y += line.Height;
        }
    }

    /// <summary>The font's strikeout position above the baseline, in px.</summary>
    private double StrikePosition()
    {
        if (double.IsNaN(_strikePosition))
        {
            var metrics = new Typeface(FontFamily, FontStyle, FontWeight).GlyphTypeface.Metrics;
            _strikePosition = metrics.DesignEmHeight > 0
                ? Math.Abs(metrics.StrikethroughPosition) * FontSize / metrics.DesignEmHeight
                : FontSize * 0.3;
        }

        return _strikePosition;
    }

    private void UpdateStroke()
    {
        var c = StrikeColor;

        // "transparent" is the strike colour at alpha 0, so the fade does not pass through black (CSS interpolates
        // premultiplied colours).
        _stroke.Color = Struck ? c : new Color(0, c.R, c.G, c.B);
    }
}
