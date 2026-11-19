using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Doba.App.Shell;

/// <summary>
/// The app sidebar. DataContext: <see cref="SidebarViewModel"/>. Code-behind only moves focus for the inline
/// "Tag name" input (focus it when it opens; Enter / Esc hand focus back to "+", by the navigation method that opened
/// it) and routes its keys.
/// </summary>
public partial class SidebarView : UserControl
{
    private const string FocusVisible = ":focus-visible";

    private SidebarViewModel? _vm;
    private NavigationMethod _focusMethod = NavigationMethod.Pointer;

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
            // "+" pressed from the keyboard shows the focus ring (:focus-visible); a click does not. Focus moves into the
            // input and back to "+" the same way, so a mouse user never sees a keyboard focus ring appear.
            _focusMethod = AddTagButton.Classes.Contains(FocusVisible) ? NavigationMethod.Tab : NavigationMethod.Pointer;

            // The row becomes visible in this layout pass; focus it once it can take focus.
            Dispatcher.UIThread.Post(() =>
            {
                if (_vm is { IsAddingTag: true })
                {
                    TagInput.Focus(_focusMethod);
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
            AddTagButton.Focus(_focusMethod);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true; // the input owns Esc while it is open (KeyboardShortcuts skips handled keys)
            _vm.CancelTagCommand.Execute(null);
            AddTagButton.Focus(_focusMethod);
        }
    }

    private void OnTagInputLostFocus(object? sender, FocusChangedEventArgs e) => _vm?.CommitTagCommand.Execute(null);
}
