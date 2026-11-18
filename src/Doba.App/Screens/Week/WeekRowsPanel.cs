using Avalonia;
using Avalonia.Controls;

namespace Doba.App.Screens.Week;

/// <summary>
/// The Week screen's two rows (design-reference line 506: <c>flex: 1 1 0; box-sizing: border-box</c>, row 1
/// padding-bottom 12, row 2 border-top 1 + padding-top 14): three children — row 1, the rule (its margins carry the
/// 12 / 14 px gaps) and row 2. What is left after the rule is split equally, so both rows get the same height:
/// <see cref="RowHeight"/> = floor((H − rule) / 2); an odd remainder pixel stays empty under row 2.
/// </summary>
public sealed class WeekRowsPanel : Panel
{
    /// <summary>Height of each row for a panel <paramref name="height"/> high with a rule block <paramref name="rule"/> high.</summary>
    public static double RowHeight(double height, double rule) => Math.Max(0, Math.Floor((height - rule) / 2));

    /// <summary>Height of the rule block (child 1) including its margins; measured or taken from its declared size.</summary>
    public double RuleHeight()
    {
        if (Children.Count < 2)
        {
            return 0;
        }

        var rule = Children[1];
        return rule.Margin.Top + rule.Margin.Bottom + (double.IsNaN(rule.Height) ? 0 : rule.Height);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var rule = RuleHeight();
        var row = double.IsInfinity(availableSize.Height) ? double.PositiveInfinity : RowHeight(availableSize.Height, rule);
        var width = 0.0;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(availableSize.Width, i == 1 ? rule : row));
            width = Math.Max(width, Children[i].DesiredSize.Width);
        }

        return new Size(width, double.IsInfinity(row) ? rule : (2 * row) + rule);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rule = RuleHeight();
        var row = RowHeight(finalSize.Height, rule);
        double[] tops = [0, row, row + rule];
        double[] heights = [row, rule, row];
        for (var i = 0; i < Children.Count && i < 3; i++)
        {
            Children[i].Arrange(new Rect(0, tops[i], finalSize.Width, heights[i]));
        }

        return finalSize;
    }
}
