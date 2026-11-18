using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Doba.App.Screens.Week;

namespace Doba.App.Screens.Year;

/// <summary>The Year screen. DataContext: <see cref="YearViewModel"/>.</summary>
public partial class YearView : UserControl
{
    private readonly ZoomOrigin _origin;

    public YearView()
    {
        InitializeComponent();
        _origin = new ZoomOrigin(this);
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

    /// <summary>Centre of <paramref name="source"/> relative to this view's bounds, as fractions (0..1).</summary>
    internal Point? OriginOf(Control source) => ZoomOrigin.Of(this, source);
}
