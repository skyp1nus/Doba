using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Doba.App.Dialogs;
using Doba.App.Dialogs.Search;

namespace Doba.App.Shell;

/// <summary>Open / close animations of the overlay layer. <see cref="InstantOverlayTransitions"/> until the Motion
/// package registers its own in <see cref="OverlayHost.OverlayTransitions"/>.</summary>
public interface IOverlayTransitions
{
    /// <summary>Plays the entrance of <paramref name="shade"/> and <paramref name="panel"/> (the overlay view's host).</summary>
    void Open(Control shade, Control panel);

    /// <summary>Plays the exit, then calls <paramref name="done"/> exactly once (also when cancelled or with animations
    /// off); the host then removes the overlay and calls <c>MainViewModel.CloseOverlay</c>.</summary>
    void Close(Control shade, Control panel, Action done);
}

/// <summary>No animation: open does nothing, close finishes at once.</summary>
public sealed class InstantOverlayTransitions : IOverlayTransitions
{
    public void Open(Control shade, Control panel)
    {
    }

    public void Close(Control shade, Control panel, Action done)
    {
        ArgumentNullException.ThrowIfNull(done);
        done();
    }
}

/// <summary>
/// Hosts <see cref="MainViewModel.Overlay"/> inside the window: maps the overlay view-model to its view, moves focus into
/// it (<see cref="IOverlayView.FocusInitial"/>), routes Esc (<see cref="OverlayViewModel.OnEscape"/> first, otherwise
/// close) and returns focus when it closes. The close path is always: request → <see cref="OverlayTransitions"/>.Close →
/// <see cref="MainViewModel.CloseOverlay"/> (which calls <see cref="OverlayViewModel.OnClosed"/>).
/// </summary>
public partial class OverlayHost : UserControl
{
    private MainViewModel? _vm;
    private OverlayViewModel? _shown;
    private IInputElement? _returnFocus;
    private bool _closing;

    public OverlayHost()
    {
        InitializeComponent();
    }

    /// <summary>The overlay animations (instant by default; replaced by the Motion package at startup).</summary>
    public static IOverlayTransitions OverlayTransitions { get; set; } = new InstantOverlayTransitions();

    /// <summary>Focused after close when the element that had focus before the overlay opened is gone, hidden or
    /// disabled (the header's New task button).</summary>
    public Control? FallbackFocus { get; set; }

    /// <summary>The view of the open overlay (null when closed).</summary>
    public Control? OverlayView => PART_Panel.Children.Count > 0 ? PART_Panel.Children[0] : null;

    /// <summary>The shade covering the window.</summary>
    internal Control Shade => PART_Shade;

    /// <summary>The panel that holds the overlay view.</summary>
    internal Control OverlayPanel => PART_Panel;

    /// <summary>Esc while an overlay is open: the overlay handles it, or the host closes it. False when no overlay is open.</summary>
    public bool HandleEscape()
    {
        if (_vm?.Overlay is not { } overlay)
        {
            return false;
        }

        if (!overlay.OnEscape())
        {
            RequestClose();
        }

        return true;
    }

    /// <summary>Closes the open overlay through the exit animation.</summary>
    public void RequestClose()
    {
        if (_vm?.Overlay is null || _closing)
        {
            return;
        }

        _closing = true;
        var vm = _vm;
        var overlay = vm.Overlay;
        vm.MarkOverlayClosing();
        OverlayTransitions.Close(PART_Shade, PART_Panel, () =>
        {
            _closing = false;
            if (ReferenceEquals(vm.Overlay, overlay))
            {
                vm.CloseOverlay();
            }
        });
    }

    /// <summary>The view for an overlay view-model (switch, no reflection).</summary>
    internal static Control CreateView(OverlayViewModel overlay) => overlay switch
    {
        TaskDialogViewModel => new TaskDialogView(),
        SearchPaletteViewModel => new SearchPaletteView(),
        _ => Unknown(overlay),
    };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (this.TryFindResource("Size.Dialog.Top", out var top) && top is double y)
        {
            PART_Panel.Margin = new Thickness(0, y, 0, 0);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as MainViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        Show(_vm?.Overlay);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Overlay))
        {
            Show(_vm?.Overlay);
        }
    }

    private void Show(OverlayViewModel? overlay)
    {
        if (ReferenceEquals(overlay, _shown))
        {
            return;
        }

        var wasOpen = _shown is not null;
        if (_shown is not null)
        {
            _shown.CloseRequested -= OnCloseRequested;
            PART_Panel.Children.Clear();
            _shown = null;
        }

        if (overlay is null)
        {
            IsVisible = false;
            _closing = false;
            RestoreFocus();
            return;
        }

        if (!wasOpen)
        {
            _returnFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        }

        var view = CreateView(overlay);
        view.DataContext = overlay;
        PART_Panel.Children.Add(view);
        _shown = overlay;
        overlay.CloseRequested += OnCloseRequested;
        IsVisible = true;
        OverlayTransitions.Open(PART_Shade, PART_Panel);

        // Once laid out (focus is refused before the first arrange): take focus away from the page underneath, then let
        // the overlay put it on its first input.
        Dispatcher.UIThread.Post(() => FocusShown(overlay, view), DispatcherPriority.Loaded);
    }

    private void FocusShown(OverlayViewModel overlay, Control view)
    {
        if (!ReferenceEquals(_shown, overlay))
        {
            return;
        }

        PART_Panel.Focus();
        (view as IOverlayView)?.FocusInitial();
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _vm?.Overlay))
        {
            RequestClose();
        }
    }

    private void RestoreFocus()
    {
        var target = _returnFocus as Control;
        _returnFocus = null;
        if (target is not null && CanFocus(target))
        {
            target.Focus();
            return;
        }

        if (FallbackFocus is { } fallback && CanFocus(fallback))
        {
            fallback.Focus();
        }
    }

    private bool CanFocus(Control c) =>
        c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled
        && TopLevel.GetTopLevel(c) is { } top && ReferenceEquals(top, TopLevel.GetTopLevel(this));

    private static Border Unknown(OverlayViewModel overlay)
    {
        Trace.WriteLine($"Doba: no overlay view for {overlay.GetType().Name}; showing an empty panel.");
        return new Border();
    }
}
