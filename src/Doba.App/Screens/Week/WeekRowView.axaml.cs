using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Doba.App.Screens.Week;

/// <summary>One of the two week rows. DataContext: <see cref="WeekRowViewModel"/>.</summary>
public partial class WeekRowView : UserControl
{
    public WeekRowView() => InitializeComponent();

    /// <summary>Day header or "+N more": zoom into that day from the clicked element's centre.</summary>
    private void OnOpenDay(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: WeekCellViewModel cell } source && this.FindAncestorOfType<WeekView>() is { } view)
        {
            view.OpenDay(cell, source);
            e.Handled = true;
        }
    }
}
