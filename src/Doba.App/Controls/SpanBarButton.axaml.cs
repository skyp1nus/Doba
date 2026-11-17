using Avalonia;
using Avalonia.Controls;
using Doba.App.ViewModels;

namespace Doba.App.Controls;

/// <summary>
/// A multi-day task bar (prototype <c>.hv2</c> span, design-reference lines 523–531): <c>Button.Bar</c> (Accent10 fill +
/// Accent25 ring; done: Bg.SpanDone + Bg.Track; hover brightens) holding a 14 px <see cref="TaskCheckBox"/>
/// (display only), the title (12.5/500 by default — set <c>FontSize</c> to change it — one line with an ellipsis,
/// strike fades in when done) and the meta text (<see cref="TaskRowViewModel.Meta"/>: "3 days", "← 4 days",
/// "Day 2 of 3"; mono 10.5 Text.Tertiary). Clicking anywhere toggles the task, as in the prototype.
/// <para>DataContext: <see cref="TaskRowViewModel"/>. Set <c>Height</c> (week 28, day 38).</para>
/// <para>Shape: <c>CornerRadius</c> (default Radius.Card = 8) applies to the ends that are not clipped;
/// <see cref="ClipStart"/>/<see cref="ClipEnd"/> mean the span continues past this row: that end gets a square corner
/// and no <see cref="Inset"/> margin (week bars: Inset 8).</para>
/// </summary>
public partial class SpanBarButton : UserControl
{
    public static readonly StyledProperty<bool> ClipStartProperty =
        AvaloniaProperty.Register<SpanBarButton, bool>(nameof(ClipStart));

    public static readonly StyledProperty<bool> ClipEndProperty =
        AvaloniaProperty.Register<SpanBarButton, bool>(nameof(ClipEnd));

    public static readonly StyledProperty<double> InsetProperty =
        AvaloniaProperty.Register<SpanBarButton, double>(nameof(Inset));

    public SpanBarButton()
    {
        InitializeComponent();
        UpdateShape();
    }

    /// <summary>The span started before this row: square left corners, no left inset.</summary>
    public bool ClipStart
    {
        get => GetValue(ClipStartProperty);
        set => SetValue(ClipStartProperty, value);
    }

    /// <summary>The span continues after this row: square right corners, no right inset.</summary>
    public bool ClipEnd
    {
        get => GetValue(ClipEndProperty);
        set => SetValue(ClipEndProperty, value);
    }

    /// <summary>Horizontal margin of the bar's unclipped ends inside its slot (week view: 8).</summary>
    public double Inset
    {
        get => GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    /// <summary>The inner button (for tests).</summary>
    internal Button Bar => PART_Bar;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClipStartProperty || change.Property == ClipEndProperty || change.Property == InsetProperty
            || change.Property == CornerRadiusProperty)
        {
            UpdateShape();
        }
    }

    private void UpdateShape()
    {
        if (PART_Bar is null)
        {
            return; // CornerRadius set from XAML before InitializeComponent finished
        }

        var r = CornerRadius;
        var start = ClipStart;
        var end = ClipEnd;
        PART_Bar.CornerRadius = new CornerRadius(start ? 0 : r.TopLeft, end ? 0 : r.TopRight, end ? 0 : r.BottomRight, start ? 0 : r.BottomLeft);
        PART_Bar.Margin = new Thickness(start ? 0 : Inset, 0, end ? 0 : Inset, 0);
    }
}
