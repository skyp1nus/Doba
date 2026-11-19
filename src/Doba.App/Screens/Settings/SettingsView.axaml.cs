using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Doba.App.Screens.Settings;

/// <summary>Settings content for the current tab; see <see cref="SettingsViewModel"/>.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView() => AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ProvidePicker();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ProvidePicker();
    }

    /// <summary>Gives the view-model the system file dialogs and the clipboard of this window, unless something else
    /// (a test) set them.</summary>
    private void ProvidePicker()
    {
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        vm.FilePicker ??= new StorageProviderFilePicker(() => TopLevel.GetTopLevel(this));
        vm.Clipboard ??= new TopLevelClipboard(() => TopLevel.GetTopLevel(this));
    }
}
