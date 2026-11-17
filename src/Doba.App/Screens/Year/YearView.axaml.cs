using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Doba.App.Screens.Year;

/// <summary>The Year screen. DataContext: <see cref="YearViewModel"/>.</summary>
public partial class YearView : UserControl
{
    public YearView()
    {
        InitializeComponent();
    }

    /// <summary>Month header or "+N more": zoom into that month from the clicked element's centre.</summary>
    private void OnOpenMonth(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: YearMonthViewModel month } source)
        {
            month.Open(OriginOf(source));
            e.Handled = true;
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
}
