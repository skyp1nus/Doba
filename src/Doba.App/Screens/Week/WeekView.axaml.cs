using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Doba.App.Screens.Week;

/// <summary>
/// The Week screen. DataContext: <see cref="WeekViewModel"/>. After every layout pass it reports the two rows' content
/// sizes to <see cref="WeekViewModel.ReportLayout"/> (which ignores unchanged sizes, so the card changes it causes
/// cannot loop: card lists never change the row sizes).
/// </summary>
public partial class WeekView : UserControl
{
    public WeekView() => InitializeComponent();

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

    /// <summary>Zoom into <paramref name="cell"/>'s day from <paramref name="source"/>'s centre.</summary>
    internal void OpenDay(WeekCellViewModel cell, Control source)
    {
        if (DataContext is WeekViewModel vm)
        {
            vm.OpenDay(cell.Date, OriginOf(source));
        }
    }

    /// <summary>Centre of <paramref name="source"/> relative to this view's bounds, as fractions (0..1).</summary>
    internal Point? OriginOf(Control source)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0
            || source.TranslatePoint(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2), this) is not { } centre)
        {
            return null;
        }

        return new Point(Math.Clamp(centre.X / size.Width, 0, 1), Math.Clamp(centre.Y / size.Height, 0, 1));
    }

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
