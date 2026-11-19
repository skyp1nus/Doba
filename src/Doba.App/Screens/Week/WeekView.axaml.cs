using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Doba.App.Screens.Week;

/// <summary>
/// The Week screen. DataContext: <see cref="WeekViewModel"/>. Each measure pass reports the rows' size to
/// <see cref="WeekViewModel.ReportLayout"/> before the rows are measured (both rows get
/// <see cref="WeekRowsPanel.RowHeight"/>), so the first projection already fits the real space and the cards are in
/// place for the first frame (no second pass mid-transition). <see cref="WeekViewModel.ReportLayout"/> ignores
/// unchanged sizes, and card changes never change the row sizes, so this cannot loop. After layout the rows' actual
/// bounds are reported as well (a no-op unless something sized them differently).
/// </summary>
public partial class WeekView : UserControl
{
    private readonly ZoomOrigin _origin;

    public WeekView()
    {
        InitializeComponent();
        _origin = new ZoomOrigin(this);
    }

    /// <summary>Lane count → height of the lane rows under the day header (36 px each).</summary>
    public static IValueConverter LanesHeight { get; } =
        new FuncValueConverter<int, double>(n => n * WeekLayout.LaneHeight);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LayoutUpdated += OnLayoutUpdated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (DataContext is WeekViewModel vm && !double.IsInfinity(availableSize.Height) && !double.IsInfinity(availableSize.Width))
        {
            var inner = availableSize.Deflate(Padding);
            var row = WeekRowsPanel.RowHeight(inner.Height, RowsPanel.RuleHeight());
            vm.ReportLayout(row, row, inner.Width);
        }

        return base.MeasureOverride(availableSize);
    }

    /// <summary>Zoom into <paramref name="cell"/>'s day from <paramref name="source"/>'s centre (keyboard: no origin).</summary>
    internal void OpenDay(WeekCellViewModel cell, Control source)
    {
        if (DataContext is WeekViewModel vm)
        {
            vm.OpenDay(cell.Date, _origin.For(source));
        }
    }

    /// <summary>Centre of <paramref name="source"/> relative to this view's bounds, as fractions (0..1).</summary>
    internal Point? OriginOf(Control source) => ZoomOrigin.Of(this, source);

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (DataContext is WeekViewModel vm)
        {
            var r1 = Row1View.Bounds.Size;
            var r2 = Row2View.Bounds.Size;
            vm.ReportLayout(r1.Height, r2.Height, r1.Width);
        }
    }
}
