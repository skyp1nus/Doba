using Avalonia;
using Avalonia.Controls;

namespace Doba.App.Controls;

/// <summary>
/// The inside of a schedule block: the title (child 0, a trimming <see cref="TextBlock"/>) and the time range (child 1),
/// laid out like the prototype's flex box. Two-line block (<see cref="IsCompact"/> false): the range under the title.
/// Compact block: the range on the title's line, <see cref="Gap"/> after it; the title keeps its natural width while it
/// fits and otherwise shrinks to all the room left (CSS <c>min-width: 0</c> + ellipsis next to a <c>flex-shrink: 0</c>
/// range), so a trimmed title pushes the range flush against the right padding instead of leaving the trimmed text's
/// slack after it, which a left-aligned <c>*,Auto</c> grid does. The range's top is <see cref="RangeOffset"/> below the
/// title's (see <see cref="ScheduleBlockViewModel.RangeOffsetFor"/>).
/// </summary>
public sealed class ScheduleBlockContent : Panel
{
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<ScheduleBlockContent, bool>(nameof(IsCompact));

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<ScheduleBlockContent, double>(nameof(Gap));

    public static readonly StyledProperty<double> RangeOffsetProperty =
        AvaloniaProperty.Register<ScheduleBlockContent, double>(nameof(RangeOffset));

    private double _titleWidth;

    static ScheduleBlockContent() =>
        AffectsMeasure<ScheduleBlockContent>(IsCompactProperty, GapProperty, RangeOffsetProperty);

    /// <summary>Title and range on one line.</summary>
    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>Space between the title and the range on a compact block's line.</summary>
    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Top of the range relative to the top of the title.</summary>
    public double RangeOffset
    {
        get => GetValue(RangeOffsetProperty);
        set => SetValue(RangeOffsetProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children is not [var title, var range])
        {
            return base.MeasureOverride(availableSize);
        }

        if (!IsCompact)
        {
            title.Measure(availableSize);
            range.Measure(availableSize);
            _titleWidth = title.DesiredSize.Width;
            return new Size(
                Math.Max(title.DesiredSize.Width, range.DesiredSize.Width),
                Math.Max(title.DesiredSize.Height, RangeOffset + range.DesiredSize.Height));
        }

        range.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var room = Math.Max(0, availableSize.Width - range.DesiredSize.Width - Gap);
        title.Measure(new Size(room, availableSize.Height));
        _titleWidth = IsTrimmed(title) ? room : title.DesiredSize.Width;
        return new Size(
            _titleWidth + Gap + range.DesiredSize.Width,
            Math.Max(title.DesiredSize.Height, RangeOffset + range.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children is not [var title, var range])
        {
            return base.ArrangeOverride(finalSize);
        }

        var titleWidth = Math.Min(_titleWidth, finalSize.Width);
        title.Arrange(new Rect(0, 0, titleWidth, title.DesiredSize.Height));
        var rangeLeft = IsCompact ? titleWidth + Gap : 0;
        range.Arrange(new Rect(rangeLeft, RangeOffset, range.DesiredSize.Width, range.DesiredSize.Height));
        return finalSize;
    }

    private static bool IsTrimmed(Control title)
    {
        if (title is TextBlock { TextLayout: { } layout })
        {
            foreach (var line in layout.TextLines)
            {
                if (line.HasCollapsed)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
