using Avalonia;
using Avalonia.Controls;

namespace Doba.App.Dialogs.Popovers;

/// <summary>
/// The mini calendar's grid (CSS <c>grid-template-columns: repeat(7, minmax(0, 1fr))</c>): children fill rows of
/// seven; column edges sit at round(i·W/7), like Chromium snaps the fractional tracks (a UniformGrid rounds each column
/// to the same whole width and loses up to 6 px across the row). Each row is as tall as its tallest child.
/// </summary>
public sealed class WeekColumns : Panel
{
    private const int Columns = 7;

    protected override Size MeasureOverride(Size availableSize)
    {
        var columnWidth = double.IsFinite(availableSize.Width) ? availableSize.Width / Columns : double.PositiveInfinity;
        double width = 0, height = 0, rowHeight = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            width = Math.Max(width, child.DesiredSize.Width * Columns);
            if (i % Columns == Columns - 1 || i == Children.Count - 1)
            {
                height += rowHeight;
                rowHeight = 0;
            }
        }

        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0, rowHeight = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var column = i % Columns;
            if (column == 0)
            {
                rowHeight = 0;
                for (var j = i; j < Math.Min(i + Columns, Children.Count); j++)
                {
                    rowHeight = Math.Max(rowHeight, Children[j].DesiredSize.Height);
                }
            }

            var left = Math.Round(column * finalSize.Width / Columns);
            var right = Math.Round((column + 1) * finalSize.Width / Columns);
            Children[i].Arrange(new Rect(left, y, right - left, rowHeight));
            if (column == Columns - 1)
            {
                y += rowHeight;
            }
        }

        return finalSize;
    }
}
