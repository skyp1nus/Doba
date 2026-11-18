using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Dialogs;
using Doba.App.Dialogs.Search;
using Doba.App.Motion;

namespace Doba.App.Shell;

/// <summary>Open / close animations of the overlay layer (<see cref="OverlayHost.OverlayTransitions"/>; default
/// <see cref="AnimatedOverlayTransitions"/>, <see cref="InstantOverlayTransitions"/> for no motion).</summary>
public interface IOverlayTransitions
{
    /// <summary>Plays the entrance of <paramref name="shade"/> and <paramref name="panel"/> (the overlay view's host).</summary>
    void Open(Control shade, Control panel);

    /// <summary>Plays the entrance, then calls <paramref name="opened"/> once when it has ended (at once with animations
    /// off): the host moves focus into the overlay only then (motion spec: "then focus goes to the title field").
    /// The default implementation plays <see cref="Open(Control, Control)"/> and reports the end at once.</summary>
    void Open(Control shade, Control panel, Action opened)
    {
        ArgumentNullException.ThrowIfNull(opened);
        Open(shade, panel);
        opened();
    }

    /// <summary>Plays the entrance of an overlay that replaces the open one at once (a search hit opening the task
    /// dialog): the shade is already up, or still on its way up, and stays as it is; only the new panel enters. Calls
    /// <paramref name="opened"/> as <see cref="Open(Control, Control, Action)"/> does. The default implementation plays
    /// the full <see cref="Open(Control, Control, Action)"/>.</summary>
    void Replace(Control shade, Control panel, Action opened) => Open(shade, panel, opened);

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
/// <para>Order on open: the entrance plays (focus parked on the panel so keys never reach the page underneath; text typed
/// meanwhile is kept and handed to the first input), then the overlay focuses its first input. On close: the exit plays,
/// then the overlay is removed, <c>OnClosed</c> runs and focus returns to the element that had it. The content behind is
/// blurred in step with the shade's opacity (in with it, out with it). An overlay that replaces the open one at once
/// (<see cref="MainViewModel.OpenOverlay"/>, e.g. a search hit opening the task dialog) keeps the shade, the blur and the
/// element focus returns to; only its panel enters.</para>
/// </summary>
public partial class OverlayHost : UserControl
{
    private readonly StringBuilder _typedDuringEntrance = new();
    private MainViewModel? _vm;
    private OverlayViewModel? _shown;
    private IInputElement? _returnFocus;
    private bool _closing;
    private bool _focusedShown;
    private bool _replaceTurn;
    private bool _blurFrameRequested;
    private double _blurRadius;
    private Control? _blurred;

    public OverlayHost()
    {
        InitializeComponent();
        PART_Panel.AddHandler(TextInputEvent, OnParkedTextInput);
    }

    /// <summary>The overlay animations (<see cref="AnimatedOverlayTransitions"/>; tests may hold or replace them).</summary>
    public static IOverlayTransitions OverlayTransitions { get; set; } = new AnimatedOverlayTransitions();

    /// <summary>
    /// The prototype's shade blurs what is behind it (<c>backdrop-filter: blur(3px)</c>): while an overlay is open the
    /// window content behind it gets this Gaussian blur. Avalonia's BlurEffect radius r renders σ = 0.57735·(r / 2) + 0.5
    /// (measured: r 8.66 gives σ ≈ 3.1 on the headless raster), so CSS 3 px (σ) is r ≈ 8.66. Null (default): from the
    /// <c>Size.Overlay.BackdropBlur</c> token (CSS px) when the theme defines it, otherwise <see cref="BackdropBlurCss"/>.
    /// Zero turns the blur off.
    /// </summary>
    public static double? BackdropBlurRadius { get; set; }

    /// <summary>The design's backdrop blur, CSS px (σ); used until Tokens.axaml defines <c>Size.Overlay.BackdropBlur</c>.</summary>
    public const double BackdropBlurCss = 3;

    /// <summary>The element blurred behind an open overlay. Null: the sibling drawn just before the host in its parent
    /// panel (the window's title bar, sidebar and main panel; the painted backdrop and chrome tint are smooth already).</summary>
    public Control? BackdropContent { get; set; }

    /// <summary>The element currently blurred (for tests), or null.</summary>
    internal Control? Blurred => _blurred;

    /// <summary>The blur radius drawn now (0: sharp; for tests).</summary>
    internal double BlurRadius => _blurRadius;

    /// <summary>The blur radius with the shade fully shown.</summary>
    internal double FullBlurRadius =>
        BackdropBlurRadius
        ?? CssBlurToRadius(this.TryFindResource("Size.Overlay.BackdropBlur", out var css) && css is double d ? d : BackdropBlurCss);

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
        _typedDuringEntrance.Clear();
        var vm = _vm;
        var overlay = vm.Overlay;
        vm.MarkOverlayClosing();
        OverlayTransitions.Close(PART_Shade, PART_Panel, () =>
        {
            // A late "done" (the overlay was already removed, e.g. by CompletePendingOverlayClose, and maybe replaced)
            // must not touch the overlay shown now: removing an overlay always resets _closing (Show(null)).
            if (ReferenceEquals(vm.Overlay, overlay))
            {
                _closing = false;
                vm.CloseOverlay();
            }
        });
        FollowShade(); // the blur goes out with the shade
    }

    /// <summary>The view for an overlay view-model (switch, no reflection).</summary>
    internal static Control CreateView(OverlayViewModel overlay) => overlay switch
    {
        TaskDialogViewModel => new TaskDialogView(),
        SearchPaletteViewModel => new SearchPaletteView(),
        _ => Unknown(overlay),
    };

    /// <summary>The BlurEffect radius for a CSS blur of <paramref name="css"/> px (σ): the radius r is drawn with
    /// σ = 0.57735·(r / 2) + 0.5 (Skia's radius-to-sigma on half the radius).</summary>
    internal static double CssBlurToRadius(double css) => css <= 0.5 ? 0 : 2 * (css - 0.5) / 0.57735;

    /// <summary>The CSS blur (σ, px) the BlurEffect radius <paramref name="radius"/> draws.</summary>
    internal static double RadiusToCssBlur(double radius) => radius <= 0 ? 0 : (0.57735 * radius / 2) + 0.5;

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
        var removedWithoutExit = wasOpen && !_closing;
        if (_shown is not null)
        {
            _shown.CloseRequested -= OnCloseRequested;
            PART_Panel.Children.Clear();
            _shown = null;
        }

        _typedDuringEntrance.Clear();
        _focusedShown = false;
        if (overlay is null)
        {
            IsVisible = false;
            _closing = false;
            if (removedWithoutExit)
            {
                // Removed without an exit: MainViewModel.OpenOverlay replacing it shows the next overlay before this turn
                // ends, so no frame is drawn in between. Keep the blur and the element focus returns to for it; if
                // nothing follows (the window is closing), put them back right after this turn, before any frame.
                _replaceTurn = true;
                Dispatcher.UIThread.Post(EndReplaceTurn, DispatcherPriority.Send);
                return;
            }

            ApplyBlur(0);
            RestoreFocus();
            return;
        }

        var replacing = _replaceTurn;
        _replaceTurn = false;
        if (!wasOpen && !replacing)
        {
            _returnFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        }

        var view = CreateView(overlay);
        view.DataContext = overlay;
        PART_Panel.Children.Add(view);
        _shown = overlay;
        overlay.CloseRequested += OnCloseRequested;
        IsVisible = true;

        // Lay the new view out now (it would be before the next frame anyway) so the entrance scales around the panel's
        // real centre, not around the size of an empty panel, and so focus can leave the page at once: keys pressed
        // during the entrance never reach the page underneath (text typed then is kept, see OnParkedTextInput). The
        // posted retry covers a host that is not arranged yet. The overlay puts focus on its first input when the
        // entrance has ended (posted after this, so the order holds when the entrance is instant).
        UpdateLayout();
        TakeFocus(overlay);
        Dispatcher.UIThread.Post(() => TakeFocus(overlay), DispatcherPriority.Loaded);

        void Opened() => Dispatcher.UIThread.Post(() => FocusShown(overlay, view), DispatcherPriority.Loaded);
        if (replacing)
        {
            OverlayTransitions.Replace(PART_Shade, PART_Panel, Opened);
        }
        else
        {
            OverlayTransitions.Open(PART_Shade, PART_Panel, Opened);
        }

        FollowShade(); // the blur comes in with the shade; a replacement keeps it where it is
    }

    private void EndReplaceTurn()
    {
        if (!_replaceTurn)
        {
            return;
        }

        _replaceTurn = false;
        if (_shown is null)
        {
            ApplyBlur(0);
            RestoreFocus();
        }
    }

    private bool IsShowing(OverlayViewModel overlay) => ReferenceEquals(_shown, overlay) && !_closing;

    private void TakeFocus(OverlayViewModel overlay)
    {
        if (!IsShowing(overlay) || _focusedShown)
        {
            return;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is null || !PART_Panel.IsVisualAncestorOf(focused))
        {
            PART_Panel.Focus();
        }
    }

    private void FocusShown(OverlayViewModel overlay, Control view)
    {
        if (!IsShowing(overlay))
        {
            return;
        }

        TakeFocus(overlay);
        _focusedShown = true;
        (view as IOverlayView)?.FocusInitial();
        ReplayTypedText();
    }

    /// <summary>Text typed while focus is parked on the panel (during the entrance) is kept for the first input.</summary>
    private void OnParkedTextInput(object? sender, TextInputEventArgs e)
    {
        if (_shown is null || _closing || _focusedShown || !ReferenceEquals(e.Source, PART_Panel) || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        _typedDuringEntrance.Append(e.Text);
        e.Handled = true;
    }

    /// <summary>Hands the text typed during the entrance to the input focused now, as if it had been typed there.</summary>
    private void ReplayTypedText()
    {
        if (_typedDuringEntrance.Length == 0)
        {
            return;
        }

        var text = _typedDuringEntrance.ToString();
        _typedDuringEntrance.Clear();
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Interactive target && !ReferenceEquals(target, PART_Panel))
        {
            target.RaiseEvent(new TextInputEventArgs { RoutedEvent = TextInputEvent, Source = target, Text = text });
        }
    }

    /// <summary>
    /// Blurs the content behind in step with the shade, like the CSS shade whose backdrop-filter fades with its opacity:
    /// while the shade animates, the blur (σ) is the shade's drawn opacity × the full blur, updated once per frame; at
    /// rest it is the full blur while an overlay is open and none once it is closing or gone. Nothing is re-rendered for
    /// the blur while the shade is at rest.
    /// </summary>
    private void FollowShade()
    {
        var shade = Animator.Running(PART_Shade);
        var level = _shown is null ? 0
            : shade is not null ? Math.Clamp(shade.Current.Opacity, 0, 1)
            : _closing ? 0 : 1;
        ApplyBlur(level);

        if (shade is not null && _shown is not null && !_blurFrameRequested && TopLevel.GetTopLevel(this) is { } top)
        {
            _blurFrameRequested = true;
            top.RequestAnimationFrame(_ =>
            {
                _blurFrameRequested = false;
                FollowShade();
            });
        }
    }

    /// <summary>Blurs the content behind the overlay at <paramref name="level"/> (0..1) of the full blur.</summary>
    private void ApplyBlur(double level)
    {
        var full = FullBlurRadius;
        var radius = level <= 0 || full <= 0 ? 0 : CssBlurToRadius(RadiusToCssBlur(full) * Math.Min(level, 1));
        var target = radius > 0 ? BackdropContent ?? PrecedingSibling() : null;
        if (_blurred is not null && !ReferenceEquals(target, _blurred))
        {
            _blurred.Effect = null;
        }

        if (target is not null && (!ReferenceEquals(target, _blurred) || radius != _blurRadius))
        {
            target.Effect = new ImmutableBlurEffect(radius);
        }

        _blurred = target;
        _blurRadius = target is null ? 0 : radius;
    }

    private Control? PrecedingSibling()
    {
        if (Parent is not Panel parent)
        {
            return null;
        }

        var index = parent.Children.IndexOf(this);
        return index > 0 ? parent.Children[index - 1] : null;
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
