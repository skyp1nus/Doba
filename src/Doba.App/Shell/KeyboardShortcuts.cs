using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.Core.Navigation;

namespace Doba.App.Shell;

/// <summary>
/// The window-level keyboard shortcuts (KICKOFF §6, Settings › Shortcuts): N new task · Ctrl+K search · G I Inbox ·
/// G T Today · 1–4 Year / Month / Week / Day · ← / → previous / next period · Esc closes the open popover, dialog or
/// search. Space (complete the focused task) and Enter belong to the focused control.
/// </summary>
/// <remarks>
/// <para><b>Routing.</b> Shortcuts listen to the bubbling KeyDown at the window, so a focused control that uses a key
/// handles it first (text boxes, the time wheel, sliders, the header's view switch and other list boxes, the tag input's
/// Enter / Esc). Arrows are also left alone while focus is inside such a control even when it did not handle the key
/// (the view switch at its first / last segment), so an arrow never does two things.</para>
/// <para><b>When.</b> With an overlay open only Esc is a shortcut (the overlay routes it; the task dialog closes its
/// popover first). With focus in a text box only Ctrl+K is. Letters and digits need no modifier at all; Ctrl+K needs
/// exactly Ctrl. Anything with Alt, Ctrl (other than Ctrl+K), Shift or Win held passes through, so system, AltGr and
/// IME combinations keep working.</para>
/// <para><b>Layouts.</b> Keys are matched on <see cref="KeyEventArgs.Key"/> (the virtual key): Windows keeps the Latin
/// virtual keys on Cyrillic layouts (the Ukrainian "т" key is VK_N), and on Latin layouts it follows the printed letter
/// (AZERTY). When the virtual key is unknown (<see cref="Key.None"/>, <see cref="Key.ImeProcessed"/>) the physical
/// key position is used instead.</para>
/// <para><b>Chords.</b> G arms a chord for <see cref="ChordTimeout"/> (measured on <see cref="ChordClock"/>); I or T
/// completes it. Any other key disarms it and then acts as itself; a lone modifier press does not disarm it.</para>
/// <para><b>Key repeat.</b> Only the arrows repeat (each repeat steps one more period). A held N, G, digit, Ctrl+K or Esc
/// acts once per press; repeated Space key-downs are swallowed outside text boxes, so a held Space completes a focused
/// task once. The character a shortcut key would type (and its auto-repeats) is swallowed, so N never leaves an "n" in
/// the dialog it opens.</para>
/// <para><b>Scrolling.</b> Keyboard focus in a scrolling area brings a margin around the control into view, so Tab never
/// skips the rows of a virtualised list that are not realised yet.</para>
/// <para><b>Focus after navigation.</b> When a navigation hides the focused element (or nothing has focus), focus moves
/// to the content area (the screen host: focusable but not a tab stop, no focus ring), so the next Tab lands on the
/// first control of the new screen and Space does nothing unexpected. A still visible invoker (a sidebar row, the
/// header's ‹ ›) keeps focus.</para>
/// </remarks>
public sealed class KeyboardShortcuts
{
    /// <summary>How long G waits for its second key (not an animation; does not change with OS animations off).</summary>
    public const int ChordTimeoutMilliseconds = 1500;

    /// <summary><see cref="ChordTimeoutMilliseconds"/> as a span.</summary>
    public static readonly TimeSpan ChordTimeout = TimeSpan.FromMilliseconds(ChordTimeoutMilliseconds);

    private readonly MainViewModel _vm;
    private readonly OverlayHost _overlays;
    private readonly InputElement _root;
    private readonly Control? _content;
    private readonly HashSet<Key> _down = [];
    private long? _armedAt;
    private bool _isRepeat; // of the key-down being routed (set by the tunnel handler, read by the bubble handler)
    private Key? _textSuppressedFor; // a held key that fired a shortcut: its characters are not typed anywhere
    private bool _swallowNextText;
    private bool _focusCheckPending;

    public KeyboardShortcuts(InputElement root, MainViewModel vm, OverlayHost overlays, Control? content = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(overlays);
        _root = root;
        _vm = vm;
        _overlays = overlays;
        _content = content;
        root.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        root.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(InputElement.TextInputEvent, OnPreviewTextInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        if (root is WindowBase window)
        {
            window.Deactivated += OnDeactivated;
        }

        if (content is not null)
        {
            // The focus target after a navigation: takes focus from code, never from Tab.
            content.Focusable = true;
            KeyboardNavigation.SetIsTabStop(content, false);
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.Navigated += OnNavigated;
        }
    }

    /// <summary>The clock the G chord's timeout is measured on (the system clock; tests drive their own).</summary>
    public TimeProvider ChordClock { get; set; } = TimeProvider.System;

    /// <summary>True while G waits for its second key.</summary>
    public bool IsChordArmed => _armedAt is { } at && ChordClock.GetElapsedTime(at) <= ChordTimeout;

    /// <summary>Applies one (non-repeated) key press; true when it was a shortcut.</summary>
    public bool Handle(Key key, KeyModifiers modifiers, object? focused) => Handle(key, PhysicalKey.None, modifiers, focused, false);

    /// <summary>Applies one key press; true when it was a shortcut (the key is then marked handled).</summary>
    /// <param name="key">The virtual key.</param>
    /// <param name="physical">The physical key position (used when <paramref name="key"/> is unknown).</param>
    /// <param name="modifiers">Modifiers held.</param>
    /// <param name="focused">The focused element.</param>
    /// <param name="isRepeat">An auto-repeat of a key that is still held.</param>
    public bool Handle(Key key, PhysicalKey physical, KeyModifiers modifiers, object? focused, bool isRepeat)
    {
        key = Effective(key, physical);
        if (IsModifier(key))
        {
            return false; // a lone Shift / Ctrl / Alt / Win press changes nothing (the chord stays armed)
        }

        var chord = IsChordArmed;
        if (!(isRepeat && key == Key.G))
        {
            _armedAt = null; // a held G keeps its first press's chord
        }

        if (key == Key.Escape && modifiers == KeyModifiers.None)
        {
            return isRepeat ? _vm.Overlay is not null : _overlays.HandleEscape();
        }

        if (_vm.Overlay is not null)
        {
            return false;
        }

        if (key == Key.K && modifiers == KeyModifiers.Control)
        {
            if (!isRepeat)
            {
                _vm.OpenSearch();
            }

            return true;
        }

        if (modifiers != KeyModifiers.None || IsInTextInput(focused))
        {
            return false;
        }

        if (chord && key is Key.I or Key.T)
        {
            if (!isRepeat)
            {
                _vm.GoSection(key == Key.I ? AppView.Inbox : AppView.Today);
            }

            return true;
        }

        switch (key)
        {
            case Key.G:
                if (!isRepeat)
                {
                    _armedAt = ChordClock.GetTimestamp();
                }

                return true;

            case Key.N:
                if (!_vm.Header.ShowNewTask)
                {
                    return false; // Settings: no New task button, no N
                }

                if (!isRepeat)
                {
                    _vm.NewTask();
                }

                return true;

            case Key.Left or Key.Right:
                if (!_vm.Nav.IsCalendar || UsesArrows(focused))
                {
                    return false;
                }

                _vm.Shift(key == Key.Left ? -1 : 1);
                return true;
        }

        if (ZoomLevel(key) is { } level)
        {
            if (!isRepeat && _vm.Nav.View != level)
            {
                _vm.SetZoomLevel(level);
            }

            return true;
        }

        return false;
    }

    /// <summary>1–4 (top row or numeric keypad) → Year / Month / Week / Day.</summary>
    private static AppView? ZoomLevel(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => AppView.Year,
        Key.D2 or Key.NumPad2 => AppView.Month,
        Key.D3 or Key.NumPad3 => AppView.Week,
        Key.D4 or Key.NumPad4 => AppView.Day,
        _ => null,
    };

    /// <summary>The virtual key, or the key at the physical position when the layout / IME reports none.</summary>
    private static Key Effective(Key key, PhysicalKey physical)
    {
        if (key is not (Key.None or Key.ImeProcessed) || physical == PhysicalKey.None)
        {
            return key;
        }

        return physical switch
        {
            PhysicalKey.N => Key.N,
            PhysicalKey.K => Key.K,
            PhysicalKey.G => Key.G,
            PhysicalKey.I => Key.I,
            PhysicalKey.T => Key.T,
            PhysicalKey.Digit1 => Key.D1,
            PhysicalKey.Digit2 => Key.D2,
            PhysicalKey.Digit3 => Key.D3,
            PhysicalKey.Digit4 => Key.D4,
            _ => key,
        };
    }

    private static bool IsModifier(Key key) => key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    /// <summary>Focus is in a text box (typing: letters and digits are text, not shortcuts).</summary>
    private static bool IsInTextInput(object? focused) => Ancestors(focused).Any(v => v is TextBox);

    /// <summary>Focus is inside a control that gives the arrow keys a meaning of its own.</summary>
    private static bool UsesArrows(object? focused) =>
        Ancestors(focused).Any(v => v is TextBox or SelectingItemsControl or RangeBase or TimeWheel);

    private static IEnumerable<Visual> Ancestors(object? focused)
    {
        for (var v = focused as Visual; v is not null; v = v.GetVisualParent())
        {
            yield return v;
        }
    }

    private object? Focused(KeyEventArgs e) => TopLevel.GetTopLevel(_root)?.FocusManager?.GetFocusedElement() ?? e.Source;

    /// <summary>
    /// Tracks held keys (auto-repeat detection), disarms the chord, swallows repeated Space outside text boxes, and
    /// takes ← / → when nothing has focus (Avalonia would otherwise spend that press on focusing the first control).
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return; // raised already handled (synthetic): not a key the user holds
        }

        var key = Effective(e.Key, e.PhysicalKey);
        _isRepeat = !_down.Add(key);
        if (_textSuppressedFor != key)
        {
            _textSuppressedFor = null;
        }

        _swallowNextText = _textSuppressedFor is not null; // an auto-repeat of a key that fired a shortcut
        if (!IsModifier(key) && key is not (Key.G or Key.I or Key.T))
        {
            _armedAt = null; // any other key disarms G, also one a focused control handles (Tab, Enter, Space…)
        }

        if (_isRepeat && e.Key == Key.Space && !IsInTextInput(Focused(e)))
        {
            e.Handled = true;
        }
        else if (key is Key.Left or Key.Right && TopLevel.GetTopLevel(_root)?.FocusManager?.GetFocusedElement() is null)
        {
            Apply(e, key);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Handled)
        {
            Apply(e, Effective(e.Key, e.PhysicalKey));
        }
    }

    private void Apply(KeyEventArgs e, Key key)
    {
        e.Handled = Handle(e.Key, e.PhysicalKey, e.KeyModifiers, Focused(e), _isRepeat);
        if (e.Handled && e.KeyModifiers == KeyModifiers.None && key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9
            or >= Key.NumPad0 and <= Key.NumPad9)
        {
            // Windows sends the character after the key-down; by then focus may be in the dialog N just opened.
            _textSuppressedFor = key;
            _swallowNextText = true;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        var key = Effective(e.Key, e.PhysicalKey);
        _down.Remove(key);
        if (_textSuppressedFor == key)
        {
            _textSuppressedFor = null;
            _swallowNextText = false;
        }
    }

    /// <summary>The character of a key press that was a shortcut is not typed (e.g. "n" into the dialog N opened).</summary>
    private void OnPreviewTextInput(object? sender, TextInputEventArgs e)
    {
        if (_swallowNextText)
        {
            _swallowNextText = false;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Keyboard focus inside a scrolling area also brings one more element-height above and below into view. The task
    /// lists are virtualised (only rows in the viewport exist), so without this margin Tab would land on a row at the
    /// viewport's edge, find the next row not realised, and skip ahead to the next group.
    /// </summary>
    private static void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NavigationMethod is not (NavigationMethod.Tab or NavigationMethod.Directional)
            || e.Source is not Control c || c.FindAncestorOfType<ScrollViewer>() is null)
        {
            return;
        }

        var h = c.Bounds.Height;
        c.BringIntoView(new Rect(0, -h, c.Bounds.Width, 3 * h));
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Key-ups are not delivered to an inactive window: forget what was held, and the chord.
        _down.Clear();
        _armedAt = null;
        _textSuppressedFor = null;
        _swallowNextText = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Current))
        {
            ScheduleFocusCheck(); // the swap (a section transition swaps after its exit phase)
        }
    }

    private void OnNavigated(object? sender, NavigatedEventArgs e) => ScheduleFocusCheck(); // e.g. a period shift rebuilds the cards

    /// <summary>Checks focus after the swap's layout pass, when the old screen / rebuilt items are gone.</summary>
    private void ScheduleFocusCheck()
    {
        if (_focusCheckPending)
        {
            return;
        }

        _focusCheckPending = true;
        Dispatcher.UIThread.Post(KeepFocusVisible, DispatcherPriority.Loaded);
    }

    /// <summary>Moves focus to the content area when the focused element was hidden by a navigation (or none has it).</summary>
    private void KeepFocusVisible()
    {
        _focusCheckPending = false;
        if (_content is null || _vm.Overlay is not null || TopLevel.GetTopLevel(_root)?.FocusManager is not { } focus)
        {
            return; // an open overlay owns focus (and returns it when it closes)
        }

        if (focus.GetFocusedElement() is Control c && c.IsEffectivelyVisible && c.IsEffectivelyEnabled
            && ReferenceEquals(TopLevel.GetTopLevel(c), TopLevel.GetTopLevel(_root)))
        {
            return;
        }

        _content.Focus(NavigationMethod.Unspecified);
    }
}
