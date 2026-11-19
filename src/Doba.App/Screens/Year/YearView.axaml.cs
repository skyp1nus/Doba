using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Doba.App.Screens.Week;

namespace Doba.App.Screens.Year;

/// <summary>The Year screen. DataContext: <see cref="YearViewModel"/>.</summary>
public partial class YearView : UserControl
{
    private readonly ZoomOrigin _origin;

    /// <summary>Height of a shown "+N more" link (measured; until one is shown, a key item's height stands in).</summary>
    private double _moreHeight;

    public YearView()
    {
        InitializeComponent();
        _origin = new ZoomOrigin(this);
    }

    /// <summary>Centre of <paramref name="source"/> relative to this view's bounds, as fractions (0..1).</summary>
    internal Point? OriginOf(Control source) => ZoomOrigin.Of(this, source);

    /// <summary>
    /// After the grid is arranged, every month cell learns how much room it has below its header
    /// (<see cref="YearMonthViewModel.SetRoom"/>), so a short window shows fewer key items and counts the rest in
    /// "+N more" instead of cutting rows at the cell edge. A change re-runs layout once; the header does not depend on the
    /// items, so it settles.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        var itemHeight = this.TryFindResource("Year.ItemHeight", ActualThemeVariant, out var value) && value is double h ? h : 0;
        if (itemHeight <= 0)
        {
            return size;
        }

        var cells = new List<(YearMonthViewModel Month, Border Cell, StackPanel Stack)>(12);
        foreach (var container in MonthGrid.GetRealizedContainers())
        {
            if (container is ContentPresenter { Child: Border { Child: StackPanel stack } cell } && cell.DataContext is YearMonthViewModel month
                && stack.Children.Count == 3)
            {
                cells.Add((month, cell, stack));
                if (stack.Children[2] is { IsVisible: true, Bounds.Height: > 0 } more)
                {
                    _moreHeight = Math.Max(_moreHeight, more.Bounds.Height);
                }
            }
        }

        foreach (var (month, cell, stack) in cells)
        {
            var content = cell.Bounds.Height - cell.Padding.Top - cell.Padding.Bottom - cell.BorderThickness.Top - cell.BorderThickness.Bottom;
            if (content <= 0)
            {
                continue;
            }

            var room = content - stack.Children[0].DesiredSize.Height - stack.Spacing;
            month.SetRoom(new YearRoom(room, itemHeight, stack.Spacing, _moreHeight > 0 ? _moreHeight : itemHeight));
        }

        return size;
    }

    /// <summary>Month header or "+N more": zoom into that month from the clicked element's centre (keyboard: no origin).</summary>
    private void OnOpenMonth(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: YearMonthViewModel month } source)
        {
            month.Open(_origin.For(source));
            e.Handled = true;
        }
    }
}
