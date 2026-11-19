using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Doba.App.Screens.Week;

namespace Doba.App.Screens.Month;

/// <summary>
/// The Month screen. DataContext: <see cref="MonthViewModel"/>. After every layout pass it reports its own height to
/// <see cref="MonthViewModel.ReportLayout"/> (which ignores unchanged heights; the week rows it sizes never change the
/// view's own height, so this cannot loop). Day cells zoom into the day and week labels into the week, from the
/// clicked element's centre (no origin when activated from the keyboard).
/// </summary>
public partial class MonthView : UserControl
{
    private readonly ZoomOrigin _origin;

    public MonthView()
    {
        InitializeComponent();
        _origin = new ZoomOrigin(this);
    }

    /// <summary>Lane count → height of the lane rows under the day number (24 px each).</summary>
    public static IValueConverter LanesHeight { get; } =
        new FuncValueConverter<int, double>(n => n * MonthLayout.LaneHeight);

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

    /// <summary>Centre of <paramref name="source"/> relative to this view's bounds, as fractions (0..1).</summary>
    internal Point? OriginOf(Control source) => ZoomOrigin.Of(this, source);

    /// <summary>A day cell (or anything over it: number, chips, "+N more"): zoom into that day.</summary>
    private void OnOpenDay(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: MonthDayViewModel day } source && DataContext is MonthViewModel vm)
        {
            vm.OpenDay(day.Date, _origin.For(source));
            e.Handled = true;
        }
    }

    /// <summary>"W46": zoom into that week.</summary>
    private void OnOpenWeek(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: MonthWeekViewModel week } source && DataContext is MonthViewModel vm)
        {
            vm.OpenWeek(week, _origin.For(source));
            e.Handled = true;
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (DataContext is MonthViewModel vm)
        {
            vm.ReportLayout(Bounds.Height);
        }
    }
}
