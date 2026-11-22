using Avalonia;
using Avalonia.Controls;

namespace Doba.App.Controls;

/// <summary>
/// The inside of a schedule block, laid out like the prototype's flex boxes: the 12 px checkbox (child 0), the title
/// (child 1, a trimming <see cref="TextBlock"/>) <see cref="Gap"/> after it and centred on its line, and the time
/// (child 2, collapsed when the block's size leaves no room for it).
/// <para>
/// Tall block (<see cref="IsTall"/>): the time under the title, <see cref="MetaOffset"/> below the title's top and
/// <see cref="MetaIndent"/> in (under the title). Short block: the time on the title's line, <see cref="Gap"/> after it;
/// the title keeps its natural width while it fits and otherwise shrinks to all the room left (CSS
/// <c>min-width: 0</c> + ellipsis next to a <c>flex-shrink: 0</c> time), so a trimmed title pushes the time flush
/// against the right padding. The time's top is <see cref="MetaOffset"/> below the title's (see
/// <see cref="ScheduleBlockViewModel.MetaOffsetFor"/>).
/// </para>
/// </summary>
public sealed class ScheduleBlockContent : Panel
{
    public static readonly StyledProperty<bool> IsTallProperty =
        AvaloniaProperty.Register<ScheduleBlockContent, bool>(nameof(IsTall));

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<ScheduleBlockContent, double>(nameof(Gap));

    public static readonly StyledProperty<double> MetaOffsetProperty =
        AvaloniaProperty.Register<ScheduleBlockContent, double>(nameof(MetaOffset));

    public static readonly StyledProperty<double> MetaIndentProperty =
        AvaloniaProperty.Register<ScheduleBlockContent, double>(nameof(MetaIndent));

    private double _titleWidth;

    static ScheduleBlockContent() =>
        AffectsMeasure<ScheduleBlockContent>(IsTallProperty, GapProperty, MetaOffsetProperty, MetaIndentProperty);

    /// <summary>Title and time stacked (block at least 44 px tall).</summary>
    public bool IsTall
    {
        get => GetValue(IsTallProperty);
        set => SetValue(IsTallProperty, value);
    }

    /// <summary>Space after the checkbox, and between the title and the time on a short block's line.</summary>
    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Top of the time relative to the top of the title.</summary>
    public double MetaOffset
    {
        get => GetValue(MetaOffsetProperty);
        set => SetValue(MetaOffsetProperty, value);
    }

    /// <summary>Left of the time on a tall block (CSS <c>padding-left: 20px</c>: under the title).</summary>
    public double MetaIndent
    {
        get => GetValue(MetaIndentProperty);
        set => SetValue(MetaIndentProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children is not [var box, var title, var meta])
        {
            return base.MeasureOverride(availableSize);
        }

        box.Measure(availableSize);
        var lead = box.DesiredSize.Width + Gap;
        var hasMeta = meta.IsVisible;
        meta.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        if (IsTall || !hasMeta)
        {
            var room = Math.Max(0, availableSize.Width - lead);
            title.Measure(new Size(room, availableSize.Height));
            _titleWidth = IsTrimmed(title) ? room : title.DesiredSize.Width;
            var metaBottom = hasMeta ? MetaOffset + meta.DesiredSize.Height : 0;
            return new Size(
                Math.Max(lead + _titleWidth, hasMeta ? MetaIndent + meta.DesiredSize.Width : 0),
                Math.Max(title.DesiredSize.Height, metaBottom));
        }

        var titleRoom = Math.Max(0, availableSize.Width - lead - Gap - meta.DesiredSize.Width);
        title.Measure(new Size(titleRoom, availableSize.Height));
        _titleWidth = IsTrimmed(title) ? titleRoom : title.DesiredSize.Width;
        return new Size(
            lead + _titleWidth + Gap + meta.DesiredSize.Width,
            Math.Max(title.DesiredSize.Height, MetaOffset + meta.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children is not [var box, var title, var meta])
        {
            return base.ArrangeOverride(finalSize);
        }

        // The checkbox is centred on the title's line (CSS align-items: center in the title row).
        var line = title.DesiredSize.Height;
        box.Arrange(new Rect(0, (line - box.DesiredSize.Height) / 2, box.DesiredSize.Width, box.DesiredSize.Height));
        var lead = box.DesiredSize.Width + Gap;
        var titleWidth = Math.Max(0, Math.Min(_titleWidth, finalSize.Width - lead));
        title.Arrange(new Rect(lead, 0, titleWidth, line));
        var metaLeft = IsTall ? MetaIndent : lead + titleWidth + Gap;
        meta.Arrange(new Rect(metaLeft, MetaOffset, meta.DesiredSize.Width, meta.DesiredSize.Height));
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
