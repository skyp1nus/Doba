using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Doba.App.Screens.Day;

/// <summary>
/// A multi-day task covering the shown day: a 38 px tinted bar with a display-only checkbox, the title and
/// "Day k of n". Clicking anywhere toggles the task (prototype behaviour). DataContext:
/// <see cref="ViewModels.TaskRowViewModel"/>; state changes animate in place (strike/colour, dim opacity).
/// </summary>
public partial class DaySpanRow : UserControl
{
    public DaySpanRow() => InitializeComponent();

    /// <summary>The bar button (tests).</summary>
    internal Button Bar => PART_Bar;

    /// <summary>"Day k of n" (tests).</summary>
    internal TextBlock MetaText => PART_Meta;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        DayEntrance.TryPlay(this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (IsLoaded)
        {
            DayEntrance.TryPlay(this);
        }
    }
}
