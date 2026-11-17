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
public class StrikeText : TextBlock
{
    public static readonly StyledProperty<bool> StruckProperty =
        AvaloniaProperty.Register<StrikeText, bool>(nameof(Struck));

    public static readonly StyledProperty<Color> StrikeColorProperty =
        AvaloniaProperty.Register<StrikeText, Color>(nameof(StrikeColor));

    private readonly SolidColorBrush _stroke = new();

    public StrikeText()
    {
        TextDecorations =
        [
            new TextDecoration { Location = TextDecorationLocation.Strikethrough, Stroke = _stroke },
        ];
        UpdateStroke();
    }

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
    }

    private void UpdateStroke()
    {
        var c = StrikeColor;

        // "transparent" is the strike colour at alpha 0, so the fade does not pass through black (CSS interpolates
        // premultiplied colours).
        _stroke.Color = Struck ? c : new Color(0, c.R, c.G, c.B);
    }
}
