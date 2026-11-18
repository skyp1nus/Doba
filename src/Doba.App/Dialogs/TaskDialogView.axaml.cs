using Avalonia.Controls;
using Doba.App.Shell;

namespace Doba.App.Dialogs;

/// <summary>The task dialog. DataContext: <see cref="TaskDialogViewModel"/>.</summary>
public partial class TaskDialogView : UserControl, IOverlayView
{
    public TaskDialogView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    public void FocusInitial()
    {
    }
}
