using Avalonia.Controls;

namespace Doba.App.Dialogs.Popovers;

/// <summary>The Date popover (mini calendar, quick options, "Lasts"). DataContext: <see cref="TaskDialogViewModel"/>.</summary>
public partial class DatePopover : UserControl
{
    public DatePopover()
    {
        InitializeComponent();
    }
}
