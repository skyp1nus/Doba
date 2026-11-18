using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Doba.App.Shell;

namespace Doba.App.Dialogs.Search;

/// <summary>
/// The search palette. DataContext: <see cref="SearchPaletteViewModel"/>. Keys are taken in the tunnel phase so the
/// query box never sees them: ↑ / ↓ move the selection (and keep it in view), Enter opens the selected task. Esc is
/// left to the shell (<see cref="SearchPaletteViewModel"/> has no inner popover, so the overlay closes). Everything
/// else, including letters such as N, goes to the query box; the shell ignores its shortcuts while an overlay is open.
/// </summary>
public partial class SearchPaletteView : UserControl, IOverlayView
{
    private SearchPaletteViewModel? _vm;
    private bool _scrollPending;

    public SearchPaletteView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The query box.</summary>
    internal TextBox QueryBox => PART_Query;

    /// <summary>The hit list.</summary>
    internal ItemsControl HitList => PART_Hits;

    /// <summary>The list's scroll viewer (at most eight rows tall).</summary>
    internal ScrollViewer HitScroll => PART_Scroll;

    /// <summary>The "No tasks found" line.</summary>
    internal TextBlock NoResults => PART_NoResults;

    /// <summary>The footer with the key hint.</summary>
    internal Border Footer => PART_Footer;

    /// <inheritdoc />
    public void FocusInitial() => PART_Query.Focus();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Attach(DataContext as SearchPaletteViewModel);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Attach(null);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Attach(DataContext as SearchPaletteViewModel);
    }

    private void Attach(SearchPaletteViewModel? vm)
    {
        if (ReferenceEquals(vm, _vm))
        {
            return;
        }

        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = vm;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                _vm.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Down:
                _vm.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Enter:
                _vm.OpenSelected();
                e.Handled = true;
                break;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchPaletteViewModel.SelectedIndex) && !_scrollPending)
        {
            // After the next layout pass, so rows inserted by the same query change have their containers.
            _scrollPending = true;
            Dispatcher.UIThread.Post(BringSelectedIntoView, DispatcherPriority.Loaded);
        }
    }

    private void BringSelectedIntoView()
    {
        _scrollPending = false;
        if (_vm is { SelectedIndex: >= 0 and var index } && index < _vm.Hits.Count)
        {
            if (index == 0)
            {
                // Include the list's top padding, as a fresh query should show the list from its top.
                PART_Scroll.Offset = default;
                return;
            }

            PART_Hits.ScrollIntoView(index);
        }
    }
}
