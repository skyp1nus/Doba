using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Doba.App.Screens.Week;

/// <summary>
/// Lays out the multi-day bars of a week row: four equal columns (edges as in <see cref="TrackGrid"/>), lanes of <see cref="LaneHeight"/> starting at
/// <see cref="Top"/> (below the day headers) — the prototype's <c>grid-column: c / span n; grid-row: lane + 2</c>.
/// Children carry <see cref="ColumnProperty"/>, <see cref="ColumnSpanProperty"/> and <see cref="LaneProperty"/>.
/// </summary>
public sealed class WeekSpanPanel : Panel
{
    public static readonly AttachedProperty<int> ColumnProperty =
        AvaloniaProperty.RegisterAttached<WeekSpanPanel, Control, int>("Column");

    public static readonly AttachedProperty<int> ColumnSpanProperty =
        AvaloniaProperty.RegisterAttached<WeekSpanPanel, Control, int>("ColumnSpan", 1);

    public static readonly AttachedProperty<int> LaneProperty =
        AvaloniaProperty.RegisterAttached<WeekSpanPanel, Control, int>("Lane");

    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<WeekSpanPanel, int>(nameof(Columns), 4);

    public static readonly StyledProperty<double> TopProperty =
        AvaloniaProperty.Register<WeekSpanPanel, double>(nameof(Top), WeekLayout.HeaderHeight);

    public static readonly StyledProperty<double> LaneHeightProperty =
        AvaloniaProperty.Register<WeekSpanPanel, double>(nameof(LaneHeight), WeekLayout.LaneHeight);

    static WeekSpanPanel()
    {
        AffectsParentMeasure<WeekSpanPanel>(ColumnProperty, ColumnSpanProperty, LaneProperty);
        AffectsParentArrange<WeekSpanPanel>(ColumnProperty, ColumnSpanProperty, LaneProperty);
        AffectsMeasure<WeekSpanPanel>(ColumnsProperty, TopProperty, LaneHeightProperty);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>Y of the first lane.</summary>
    public double Top
    {
        get => GetValue(TopProperty);
        set => SetValue(TopProperty, value);
    }

    public double LaneHeight
    {
        get => GetValue(LaneHeightProperty);
        set => SetValue(LaneHeightProperty, value);
    }

    public static int GetColumn(Control control) => control.GetValue(ColumnProperty);

    public static void SetColumn(Control control, int value) => control.SetValue(ColumnProperty, value);

    public static int GetColumnSpan(Control control) => control.GetValue(ColumnSpanProperty);

    public static void SetColumnSpan(Control control, int value) => control.SetValue(ColumnSpanProperty, value);

    public static int GetLane(Control control) => control.GetValue(LaneProperty);

    public static void SetLane(Control control, int value) => control.SetValue(LaneProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var column = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width / Columns;
        var bottom = 0.0;
        foreach (var child in Children)
        {
            var slot = new Size(column * Span(child), LaneHeight);
            child.Measure(slot);
            bottom = Math.Max(bottom, Top + ((GetLane(child) + 1) * LaneHeight));
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Column edges as in TrackGrid, so bars line up with the day columns under them.
        foreach (var child in Children)
        {
            var c = Math.Clamp(GetColumn(child), 0, Columns - 1);
            var x0 = TrackGrid.ColumnEdge(c, Columns, finalSize.Width);
            var x1 = TrackGrid.ColumnEdge(c + Span(child), Columns, finalSize.Width);
            child.Arrange(new Rect(x0, Top + (GetLane(child) * LaneHeight), x1 - x0, LaneHeight));
        }

        return finalSize;
    }

    private int Span(Control child) => Math.Clamp(GetColumnSpan(child), 1, Columns - Math.Clamp(GetColumn(child), 0, Columns - 1));
}
