using Avalonia;
using Avalonia.Controls;

namespace Doba.App.Screens.Week;

/// <summary>
/// Equal-track grid for the calendar screens (week columns, month days, the year's 4 × 3 months): the prototype's
/// <c>grid-template-columns: repeat(n, minmax(0, 1fr))</c>. Children fill the cells row by row.
/// <para>
/// Track edges follow Chromium, not <see cref="Avalonia.Controls.Primitives.UniformGrid"/>: CSS keeps the fractional
/// track positions and snaps each edge to the pixel grid, so every edge is at most half a pixel from its exact
/// position. A uniform grid rounds every track to the same width instead, and its error adds up across the row
/// (1315 / 4 = 328.75 gives 590, 919, 1248 where the reference has 590, 919, 1247). Column edges are
/// <c>round(i × W / n)</c>. Row edges are <c>floor(j × H / m)</c>: in the reference renders the content area starts
/// a fraction of a pixel above ours (its header line boxes are fractional), which turns Chromium's rounding of the
/// row edges into a floor relative to our whole-pixel origin (year rows at 422 / 701 for 836 / 3 = 278.67).
/// </para>
/// </summary>
public sealed class TrackGrid : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<TrackGrid, int>(nameof(Columns), 1);

    public static readonly StyledProperty<int> RowsProperty =
        AvaloniaProperty.Register<TrackGrid, int>(nameof(Rows), 1);

    static TrackGrid()
    {
        AffectsMeasure<TrackGrid>(ColumnsProperty, RowsProperty);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>Left edge of column <paramref name="i"/> (0..<paramref name="n"/>) in a row <paramref name="width"/> wide.</summary>
    public static double ColumnEdge(int i, int n, double width) =>
        Math.Round(i * width / Math.Max(1, n), MidpointRounding.AwayFromZero);

    /// <summary>Top edge of row <paramref name="j"/> (0..<paramref name="m"/>) in a grid <paramref name="height"/> high.</summary>
    public static double RowEdge(int j, int m, double height) => Math.Floor(j * height / Math.Max(1, m));

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = Math.Max(1, Columns);
        var rows = Math.Max(1, Rows);
        var cell = new Size(availableSize.Width / columns, availableSize.Height / rows);
        var maxW = 0.0;
        var maxH = 0.0;
        foreach (var child in Children)
        {
            child.Measure(cell);
            maxW = Math.Max(maxW, child.DesiredSize.Width);
            maxH = Math.Max(maxH, child.DesiredSize.Height);
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? maxW * columns : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? maxH * rows : Math.Min(availableSize.Height, maxH * rows));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Math.Max(1, Columns);
        var rows = Math.Max(1, Rows);
        var children = Children;
        for (var k = 0; k < children.Count; k++)
        {
            var c = k % columns;
            var r = k / columns;
            if (r >= rows)
            {
                children[k].Arrange(default);
                continue;
            }

            var x0 = ColumnEdge(c, columns, finalSize.Width);
            var x1 = ColumnEdge(c + 1, columns, finalSize.Width);
            var y0 = RowEdge(r, rows, finalSize.Height);
            var y1 = r == rows - 1 ? finalSize.Height : RowEdge(r + 1, rows, finalSize.Height);
            children[k].Arrange(new Rect(x0, y0, x1 - x0, y1 - y0));
        }

        return finalSize;
    }
}
