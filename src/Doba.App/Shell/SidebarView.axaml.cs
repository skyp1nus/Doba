using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Doba.App.Shell;

/// <summary>
/// The app sidebar. DataContext: <see cref="SidebarViewModel"/>. Code-behind only moves focus for the inline
/// "Tag name" input (focus it when it opens; Enter / Esc hand focus back to "+") and routes its keys.
/// </summary>
public partial class SidebarView : UserControl
{
    private SidebarViewModel? _vm;

    public SidebarView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as SidebarViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SidebarViewModel.IsAddingTag) && _vm is { IsAddingTag: true })
        {
            // The row becomes visible in this layout pass; focus it once it can take focus.
            Dispatcher.UIThread.Post(() =>
            {
                if (_vm is { IsAddingTag: true })
                {
                    TagInput.Focus();
                }
            }, DispatcherPriority.Loaded);
        }
    }

    private void OnTagInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _vm.CommitTagCommand.Execute(null);
            AddTagButton.Focus(NavigationMethod.Tab);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true; // the input owns Esc while it is open (the shell's Esc handler must not see it)
            _vm.CancelTagCommand.Execute(null);
            AddTagButton.Focus(NavigationMethod.Tab);
        }
    }

    private void OnTagInputLostFocus(object? sender, FocusChangedEventArgs e) => _vm?.CommitTagCommand.Execute(null);
}
