using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Doba.App.Dialogs.Popovers;
using Doba.App.Shell;

namespace Doba.App.Dialogs;

/// <summary>
/// The task dialog. DataContext: <see cref="TaskDialogViewModel"/>. Keys: Enter in the title saves (through the
/// view-model's Commit, which closes first and writes in OnClosed); Enter in the notes inserts a newline; Esc goes through
/// the overlay host (open popover first). Focusing the title or the notes closes an open popover. The four popovers are
/// built the first time they open (<see cref="PopoverAnchor.PopoverFactory"/>).
/// </summary>
public partial class TaskDialogView : UserControl, IOverlayView
{
    private TaskDialogViewModel? _vm;

    public TaskDialogView()
    {
        InitializeComponent();
        // The popovers get the dialog as their own DataContext (not inherited), so their bindings and item containers
        // survive being taken out of the tree between opens.
        PART_DateAnchor.PopoverFactory = () => new DatePopover { DataContext = _vm };
        PART_TimeAnchor.PopoverFactory = () => new TimePopover { DataContext = _vm, Dialog = _vm };
        PART_PriorityAnchor.PopoverFactory = () => new PriorityPopover { DataContext = _vm };
        PART_TagAnchor.PopoverFactory = () => new TagPopover { DataContext = _vm };

        PART_Title.AddHandler(KeyDownEvent, OnTitleKeyDown, RoutingStrategies.Tunnel);
        PART_Title.GotFocus += OnTextFocused;
        PART_Notes.GotFocus += OnTextFocused;
    }

    /// <summary>The task name box (focused when the dialog opens).</summary>
    internal TextBox TitleBox => PART_Title;

    /// <summary>The notes box.</summary>
    internal TextBox NotesBox => PART_Notes;

    /// <summary>The Date, Time, Priority and Tag pill anchors, in that order.</summary>
    internal IReadOnlyList<PopoverAnchor> Anchors => [PART_DateAnchor, PART_TimeAnchor, PART_PriorityAnchor, PART_TagAnchor];

    /// <inheritdoc />
    public void FocusInitial() => FocusTitle(NavigationMethod.Unspecified);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.FocusTitleRequested -= OnFocusTitleRequested;
        }

        _vm = DataContext as TaskDialogViewModel;
        if (_vm is not null)
        {
            _vm.FocusTitleRequested += OnFocusTitleRequested;
        }
    }

    private void FocusTitle(NavigationMethod method)
    {
        if (PART_Title.Focus(method))
        {
            PART_Title.CaretIndex = PART_Title.Text?.Length ?? 0;
        }
    }

    private void OnFocusTitleRequested(object? sender, EventArgs e) => FocusTitle(NavigationMethod.Unspecified);

    private void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && e.KeyModifiers == KeyModifiers.None && _vm is not null)
        {
            e.Handled = true;
            _vm.Commit();
        }
    }

    private void OnTextFocused(object? sender, FocusChangedEventArgs e) => _vm?.ClosePopover();
}
