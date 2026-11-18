using Avalonia;
using Avalonia.Controls;

namespace Doba.App.Controls;

/// <summary>
/// The task completion checkbox: a <see cref="BoxSize"/> px square with a 1.5 px ring (Brush.Border.Check) that fills
/// with the accent and shows a dark check mark when checked (250 ms fill/ring, 200 ms check, `ease`).
/// Sizes used by the design: 16 (lists, cards, day spans; check 10 px), 14 (week bars; check 9 px),
/// 12 (year rows; check 8 px), 10 (month items; no check mark). Template: Theme/Controls/TaskCheckBox.axaml.
/// </summary>
public class TaskCheckBox : CheckBox
{
    /// <summary>The design's ring: <c>border: 1.5px solid</c>.</summary>
    public const double CssRingWidth = 1.5;

    public static readonly StyledProperty<double> BoxSizeProperty =
        AvaloniaProperty.Register<TaskCheckBox, double>(nameof(BoxSize), 16);

    public static readonly DirectProperty<TaskCheckBox, Thickness> RingThicknessProperty =
        AvaloniaProperty.RegisterDirect<TaskCheckBox, Thickness>(nameof(RingThickness), o => o.RingThickness);

    private Thickness _ringThickness = new(RingWidth(1));
    private TopLevel? _topLevel;

    public double BoxSize
    {
        get => GetValue(BoxSizeProperty);
        set => SetValue(BoxSizeProperty, value);
    }

    /// <summary>
    /// The ring as drawn on this screen: <see cref="CssRingWidth"/> snapped down to whole device pixels (at least one),
    /// the way Chromium draws CSS border widths — 1 px at 100 %, 1.33 at 150 %, 1.5 at 200 %.
    /// </summary>
    public Thickness RingThickness
    {
        get => _ringThickness;
        private set => SetAndRaise(RingThicknessProperty, ref _ringThickness, value);
    }

    /// <summary>The ring width in DIPs at <paramref name="renderScaling"/>.</summary>
    public static double RingWidth(double renderScaling) =>
        renderScaling > 0 ? Math.Max(1, Math.Floor(CssRingWidth * renderScaling)) / renderScaling : 1;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged += OnScalingChanged;
        }

        UpdateRing();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged -= OnScalingChanged;
            _topLevel = null;
        }
    }

    private void OnScalingChanged(object? sender, EventArgs e) => UpdateRing();

    private void UpdateRing() => RingThickness = new Thickness(RingWidth(_topLevel?.RenderScaling ?? 1));
}
