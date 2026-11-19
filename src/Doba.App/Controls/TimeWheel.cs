using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Doba.App.Motion;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Controls;

/// <summary>
/// One column of the Time popover's iPhone-style wheel (hours "00".."23" or minutes "00".."55"): a vertical strip of
/// <see cref="Items"/> in 34 px rows, five visible, the selected row centred under a fixed highlight band, the top and
/// bottom fading out through an opacity mask (docs/design/motion-spec.md, "Барабан часу"). The dialog places two of
/// them side by side with its own ":" between (and, to get the design's single wide band, sets
/// <see cref="ShowsBand"/> = false and draws one <c>TimeWheelBand</c> behind both). Template: Theme/Controls/TimeWheel.axaml.
/// </summary>
/// <remarks>
/// <para><b>Input.</b> Mouse wheel (one notch = one row; fractional touchpad deltas accumulate), pointer drag (the strip
/// follows the pointer, snaps to the nearest row on release), click on a row (smooth scroll to it), and, when focused,
/// Up/Down (one row), PageUp/PageDown (one screen of rows), Home/End. Snap is mandatory and both ends clamp: at rest a row is
/// always exactly centred.</para>
/// <para><b>Commit rule.</b> While the user is scrolling nothing changes: <see cref="SelectedIndex"/> keeps the committed
/// value. When the wheel has been still for <see cref="M.WheelSettle"/> ms after user movement (the smooth scroll has
/// finished and the pointer is up), <see cref="SelectedIndex"/> becomes the centred row and <see cref="Committed"/> is raised
/// once — a burst of notches gives one commit with the final index. A gesture that does not move the wheel (a notch at an
/// end, a click on the centred row) commits nothing. A pending commit is flushed if the wheel leaves the visual tree.</para>
/// <para><b>Motion.</b> The strip moves only through its render transform (resting position) plus a composition FLIP
/// animation (<see cref="Animator"/>, <see cref="M.Slide"/> ease-out) from where it is drawn, so interrupted scrolls
/// continue smoothly and nothing is laid out per frame. The selected number is emphasised (15 → 19 px, weight, muted →
/// primary colour over <see cref="M.WheelText"/>): the font size is set once per change and the visible growth is a
/// composition scale animation of the row (19/15 → 1), so no layout runs per frame. With OS animations off every move is
/// instant.</para>
/// </remarks>
public class TimeWheel : TemplatedControl
{
    /// <summary>Defines the <see cref="Items"/> property.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>> ItemsProperty =
        AvaloniaProperty.Register<TimeWheel, IReadOnlyList<string>>(nameof(Items), []);

    /// <summary>Defines the <see cref="SelectedIndex"/> property (two-way by default, clamped to the items).</summary>
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<TimeWheel, int>(nameof(SelectedIndex), 0, false, BindingMode.TwoWay, coerce: CoerceIndex);

    /// <summary>Defines the <see cref="ItemHeight"/> property.</summary>
    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<TimeWheel, double>(nameof(ItemHeight), 34, validate: h => h > 0 && double.IsFinite(h));

    /// <summary>Defines the <see cref="SelectedFontSize"/> property.</summary>
    public static readonly StyledProperty<double> SelectedFontSizeProperty =
        AvaloniaProperty.Register<TimeWheel, double>(nameof(SelectedFontSize), 19);

    /// <summary>Defines the <see cref="SelectedFontWeight"/> property.</summary>
    public static readonly StyledProperty<FontWeight> SelectedFontWeightProperty =
        AvaloniaProperty.Register<TimeWheel, FontWeight>(nameof(SelectedFontWeight), FontWeight.SemiBold);

    /// <summary>Defines the <see cref="SelectedForeground"/> property.</summary>
    public static readonly StyledProperty<IBrush?> SelectedForegroundProperty =
        AvaloniaProperty.Register<TimeWheel, IBrush?>(nameof(SelectedForeground));

    /// <summary>Defines the <see cref="ShowsBand"/> property.</summary>
    public static readonly StyledProperty<bool> ShowsBandProperty =
        AvaloniaProperty.Register<TimeWheel, bool>(nameof(ShowsBand), true);

    /// <summary>Pointer travel (px) after which a press becomes a drag instead of a click on a row.</summary>
    private const double DragThreshold = 4;

    /// <summary>Distances below this (px) count as "already there".</summary>
    private const double Epsilon = 0.5;

    /// <summary>The prototype's root <c>line-height: 1.4</c>, which the wheel's row buttons inherit.</summary>
    private const double CssLineHeight = 1.4;

    private readonly TranslateTransform _stripTransform = new();
    private readonly List<Border> _rows = [];
    private readonly List<TextBlock> _labels = [];

    private Panel? _viewport;
    private StackPanel? _strip;

    // Scroll state. _offset is the resting scrollTop in px (index · ItemHeight at rest, anything while dragging);
    // _target is the row the wheel rests on or is heading to.
    private double _offset;
    private int _target;
    private double _wheelAccumulator;

    // Pointer gesture.
    private IPointer? _pointer;
    private bool _pressed;
    private bool _dragging;
    private Point _pressPoint;
    private double _pressOffset;

    // Commit rule: _userMoved = the user moved the wheel since the last commit; the settle timer fires WheelSettle ms
    // after the last movement ended. _settleGeneration discards callbacks of timers that were restarted or cancelled.
    private bool _userMoved;
    private ITimer? _settleTimer;
    private DispatcherTimer? _settleDispatcherTimer;
    private int _settleGeneration;

    private bool _settingIndex;
    private int _emphasized = -1;

    static TimeWheel()
    {
        FocusableProperty.OverrideDefaultValue<TimeWheel>(true);
    }

    /// <summary>The labels shown on the wheel ("00".."23" or "00".."55"), top to bottom.</summary>
    public IReadOnlyList<string> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>
    /// The committed index: the row under the band once the wheel has settled. It does not change while the user is
    /// scrolling; it changes after <see cref="M.WheelSettle"/> ms of stillness (then <see cref="Committed"/> is raised), or
    /// at once through <see cref="ScrollToIndex"/> or when set from outside (which positions the wheel instantly).
    /// </summary>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>Row height in px (theme: Size.TimeWheel.Row, 34).</summary>
    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>Font size of the selected number (theme: FontSize.19); the others use <see cref="TemplatedControl.FontSize"/>.</summary>
    public double SelectedFontSize
    {
        get => GetValue(SelectedFontSizeProperty);
        set => SetValue(SelectedFontSizeProperty, value);
    }

    /// <summary>Weight of the selected number (600); the others use <see cref="TemplatedControl.FontWeight"/>.</summary>
    public FontWeight SelectedFontWeight
    {
        get => GetValue(SelectedFontWeightProperty);
        set => SetValue(SelectedFontWeightProperty, value);
    }

    /// <summary>Colour of the selected number (theme: Brush.Text.Primary); the others use <see cref="TemplatedControl.Foreground"/>.</summary>
    public IBrush? SelectedForeground
    {
        get => GetValue(SelectedForegroundProperty);
        set => SetValue(SelectedForegroundProperty, value);
    }

    /// <summary>
    /// True (default): the wheel draws its own selection band. The dialog sets false on both columns and draws one
    /// shared band (Theme <c>TimeWheelBand</c>, margin Space.TimeWheel.Band) behind the pair, as in the design.
    /// </summary>
    public bool ShowsBand
    {
        get => GetValue(ShowsBandProperty);
        set => SetValue(ShowsBandProperty, value);
    }

    /// <summary>
    /// Raised once each time the wheel settles after the user moved it (wheel, drag, click on a row, keys), with the
    /// index now in <see cref="SelectedIndex"/>. It is raised even when the settled index equals the previous one (the
    /// user scrolled away and back), because user movement itself means "Set time" in the dialog. It is never raised by
    /// <see cref="ScrollToIndex"/>, by setting <see cref="SelectedIndex"/>, or by a gesture that did not move the wheel.
    /// </summary>
    public event EventHandler<int>? Committed;

    /// <summary>
    /// The clock behind the settle timer. Null (the app) = a one-shot <see cref="DispatcherTimer"/> on the UI thread, like
    /// the rest of the app's timers; tests set a manual clock to drive the commit rule deterministically.
    /// </summary>
    internal TimeProvider? SettleClock { get; set; }

    /// <summary>The row the wheel rests on or is scrolling to (equals <see cref="SelectedIndex"/> once settled).</summary>
    internal int TargetIndex => _target;

    /// <summary>True while a user movement waits for the settle timer (or for the scroll/drag to end).</summary>
    internal bool CommitPending => _userMoved;

    /// <summary>
    /// Brings <paramref name="index"/> (clamped) under the band — smoothly (Motion tokens) or, with
    /// <paramref name="animate"/> = false, instantly (used when the popover opens). Sets <see cref="SelectedIndex"/> at
    /// once, cancels any pending user commit and never raises <see cref="Committed"/>.
    /// </summary>
    public void ScrollToIndex(int index, bool animate)
    {
        EndGesture();
        _userMoved = false;
        CancelSettle();
        index = Clamp(index);
        SetIndex(index);
        if (animate)
        {
            MoveTo(index, animate: true, user: false);
            UpdateEmphasis(animate: true);
        }
        else
        {
            Place(index);
            UpdateEmphasis(animate: false);
        }
    }

    /// <summary>Raises <see cref="Committed"/>.</summary>
    protected virtual void OnCommitted(int index) => Committed?.Invoke(this, index);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_strip is not null)
        {
            _strip.RenderTransform = null;
            _strip.Children.Clear();
        }

        _viewport = e.NameScope.Find<Panel>("PART_Viewport");
        _strip = e.NameScope.Find<StackPanel>("PART_Items");
        if (_strip is not null)
        {
            _strip.RenderTransform = _stripTransform;
        }

        BuildRows();
        Place(Clamp(SelectedIndex));
        UpdateEmphasis(animate: false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        var property = change.Property;
        if (property == SelectedIndexProperty)
        {
            if (!_settingIndex)
            {
                // Set from outside (binding, dialog): position instantly, drop any half-finished user gesture.
                EndGesture();
                _userMoved = false;
                CancelSettle();
                Place(Clamp(SelectedIndex));
                UpdateEmphasis(animate: false);
            }
        }
        else if (property == ItemsProperty)
        {
            EndGesture();
            _userMoved = false;
            CancelSettle();
            BuildRows();
            CoerceValue(SelectedIndexProperty);
            Place(Clamp(SelectedIndex));
            UpdateEmphasis(animate: false);
        }
        else if (property == ItemHeightProperty)
        {
            foreach (var row in _rows)
            {
                row.Height = ItemHeight;
            }

            Place(_target);
        }
        else if (property == ForegroundProperty || property == FontSizeProperty || property == FontWeightProperty ||
                 property == FontFamilyProperty ||
                 property == SelectedForegroundProperty || property == SelectedFontSizeProperty ||
                 property == SelectedFontWeightProperty)
        {
            RestyleRows();
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        ApplyOffset(); // the centre line depends on the viewport height
        return size;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Popover closed within the settle time (e.g. Done right after a scroll): keep the user's value.
        if (_userMoved)
        {
            if (_dragging)
            {
                _target = Clamp((int)Math.Round(_offset / ItemHeight, MidpointRounding.AwayFromZero));
            }

            EndGesture();
            Commit();
        }

        EndGesture();
        CancelSettle();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Handled || _pressed || Items.Count == 0)
        {
            return;
        }

        e.Handled = true;
        _wheelAccumulator += e.Delta.Y;
        var steps = (int)Math.Truncate(_wheelAccumulator);
        if (steps == 0)
        {
            return;
        }

        _wheelAccumulator -= steps;
        MoveTo(_target - steps, animate: true, user: true); // wheel up (positive) = earlier rows
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || _pressed || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Items.Count == 0)
        {
            return;
        }

        e.Handled = true;
        Focus(NavigationMethod.Pointer);

        // Touching a moving wheel stops it where it is drawn (like iOS); nothing commits while the button is down.
        _offset = VisualOffset();
        if (_strip is not null)
        {
            Animator.Running(_strip)?.Cancel();
        }

        ApplyOffset();
        CancelSettle();

        _pointer = e.Pointer;
        _pointer.Capture(this);
        _pressed = true;
        _dragging = false;
        _pressPoint = e.GetPosition(this);
        _pressOffset = _offset;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pressed || !ReferenceEquals(e.Pointer, _pointer))
        {
            return;
        }

        var dy = e.GetPosition(this).Y - _pressPoint.Y;
        if (!_dragging && Math.Abs(dy) < DragThreshold)
        {
            return;
        }

        e.Handled = true;
        _dragging = true;
        var offset = Math.Clamp(_pressOffset - dy, 0, MaxOffset);
        if (Math.Abs(offset - _pressOffset) > Epsilon)
        {
            _userMoved = true;
        }

        _offset = offset;
        ApplyOffset();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressed || !ReferenceEquals(e.Pointer, _pointer))
        {
            return;
        }

        e.Handled = true;
        var wasDrag = _dragging;
        var rowUnderPointer = _strip is null ? -1 : RowAt(e.GetPosition(_strip).Y);
        EndGesture();

        if (wasDrag || rowUnderPointer < 0)
        {
            Snap();
        }
        else
        {
            MoveTo(rowUnderPointer, animate: true, user: true); // click on a number: smooth scroll to it
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_pressed && ReferenceEquals(e.Pointer, _pointer))
        {
            _pointer = null; // already lost; do not release it again
            EndGesture();
            Snap();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _pressed || Items.Count == 0 || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var page = Math.Max(1, (int)Math.Floor(((_viewport?.Bounds.Height ?? 0) / ItemHeight) + 0.01));
        int? index = e.Key switch
        {
            Key.Up => _target - 1,
            Key.Down => _target + 1,
            Key.PageUp => _target - page,
            Key.PageDown => _target + page,
            Key.Home => 0,
            Key.End => Items.Count - 1,
            _ => null,
        };

        if (index is { } i)
        {
            e.Handled = true;
            MoveTo(i, animate: true, user: true);
        }
    }

    private static int CoerceIndex(AvaloniaObject sender, int value)
    {
        var count = ((TimeWheel)sender).Items.Count;
        return count == 0 ? Math.Max(0, value) : Math.Clamp(value, 0, count - 1);
    }

    private double MaxOffset => Math.Max(0, Items.Count - 1) * ItemHeight;

    private int Clamp(int index) => Items.Count == 0 ? 0 : Math.Clamp(index, 0, Items.Count - 1);

    /// <summary>Index of the row at <paramref name="y"/> (strip coordinates), or -1 outside the rows.</summary>
    private int RowAt(double y)
    {
        var index = (int)Math.Floor(y / ItemHeight);
        return index >= 0 && index < Items.Count ? index : -1;
    }

    /// <summary>The scroll offset the user sees now: the resting offset minus what the running FLIP still adds.</summary>
    private double VisualOffset() =>
        _strip is not null && Animator.Running(_strip) is { } handle ? _offset - handle.Current.Y : _offset;

    /// <summary>Instant positioning: no animation, no commit.</summary>
    private void Place(int index)
    {
        if (_strip is not null)
        {
            Animator.Running(_strip)?.Cancel();
        }

        _target = index;
        _offset = index * ItemHeight;
        _wheelAccumulator = 0;
        ApplyOffset();
    }

    /// <summary>Snap after a drag: to the nearest row.</summary>
    private void Snap() =>
        MoveTo((int)Math.Round(_offset / ItemHeight, MidpointRounding.AwayFromZero), animate: true, user: true);

    /// <summary>
    /// Scrolls to <paramref name="index"/> (clamped). A user move that changes the position marks a pending commit;
    /// the settle timer starts once the smooth scroll has finished (immediately when nothing animates).
    /// </summary>
    private void MoveTo(int index, bool animate, bool user)
    {
        index = Clamp(index);
        if (_strip is not null && index == _target && Animator.IsAnimating(_strip))
        {
            return; // already heading there (e.g. more notches against an end)
        }

        var from = VisualOffset();
        var to = index * ItemHeight;
        var moves = Math.Abs(from - to) > Epsilon;
        _target = index;
        _offset = to;
        ApplyOffset();
        CancelSettle();
        if (user && moves)
        {
            _userMoved = true;
        }

        if (moves && animate && _strip is not null)
        {
            // FLIP: the transform is already at the new resting place; start the composition offset where the strip is drawn.
            Animator.Animate(_strip, MotionState.Offset(0, to - from), MotionState.Rest, M.Slide, M.EaseOut,
                onFinished: OnMoveFinished);
            return;
        }

        if (_strip is not null)
        {
            Animator.Running(_strip)?.Cancel();
        }

        if (_userMoved)
        {
            StartSettle();
        }
    }

    private void OnMoveFinished(MotionHandle handle)
    {
        if (!handle.WasCancelled && !_pressed && _userMoved)
        {
            StartSettle();
        }
    }

    private void ApplyOffset()
    {
        var viewport = _viewport?.Bounds.Height ?? 0;
        _stripTransform.Y = ((viewport - ItemHeight) / 2) - _offset;
    }

    private void StartSettle()
    {
        CancelSettle();
        var generation = _settleGeneration;
        var delay = TimeSpan.FromMilliseconds(M.WheelSettle);
        if (SettleClock is { } clock)
        {
            _settleTimer = clock.CreateTimer(_ => OnSettleElapsed(generation), null, delay, Timeout.InfiniteTimeSpan);
            return;
        }

        if (_settleDispatcherTimer is null)
        {
            _settleDispatcherTimer = new DispatcherTimer();
            _settleDispatcherTimer.Tick += (_, _) =>
            {
                _settleDispatcherTimer?.Stop();
                OnSettleElapsed(_settleGeneration);
            };
        }

        _settleDispatcherTimer.Interval = delay;
        _settleDispatcherTimer.Start();
    }

    private void CancelSettle()
    {
        _settleGeneration++;
        _settleTimer?.Dispose();
        _settleTimer = null;
        _settleDispatcherTimer?.Stop();
    }

    private void OnSettleElapsed(int generation)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnSettleElapsed(generation));
            return;
        }

        if (generation != _settleGeneration || _pressed || (_strip is not null && Animator.IsAnimating(_strip)))
        {
            return; // restarted, cancelled, or the wheel is moving again (a new settle follows)
        }

        _settleTimer?.Dispose();
        _settleTimer = null;
        _settleDispatcherTimer?.Stop();
        Commit();
    }

    private void Commit()
    {
        if (!_userMoved)
        {
            return;
        }

        _userMoved = false;
        var index = Clamp(_target);
        SetIndex(index);
        UpdateEmphasis(animate: true);
        OnCommitted(index);
    }

    private void SetIndex(int index)
    {
        _settingIndex = true;
        try
        {
            SetCurrentValue(SelectedIndexProperty, index);
        }
        finally
        {
            _settingIndex = false;
        }
    }

    private void EndGesture()
    {
        var pointer = _pointer;
        _pointer = null;
        _pressed = false;
        _dragging = false;
        if (pointer is not null && ReferenceEquals(pointer.Captured, this))
        {
            pointer.Capture(null);
        }
    }

    private void BuildRows()
    {
        _rows.Clear();
        _labels.Clear();
        _emphasized = -1;
        if (_strip is null)
        {
            return;
        }

        _strip.Children.Clear();
        foreach (var text in Items)
        {
            var label = new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top, // placed by LabelTop, like the prototype's line box
            };
            var row = new Border { Height = ItemHeight, Child = label };
            _labels.Add(label);
            _rows.Add(row);
            _strip.Children.Add(row);
            ApplyLabelStyle(label, selected: false, animate: false);
        }
    }

    private void RestyleRows()
    {
        for (var i = 0; i < _labels.Count; i++)
        {
            ApplyLabelStyle(_labels[i], i == _emphasized, animate: false);
        }
    }

    /// <summary>Moves the emphasis to <see cref="SelectedIndex"/>.</summary>
    private void UpdateEmphasis(bool animate)
    {
        var index = _labels.Count == 0 ? -1 : Clamp(SelectedIndex);
        if (index == _emphasized)
        {
            return;
        }

        var previous = _emphasized;
        _emphasized = index;
        if (previous >= 0 && previous < _labels.Count)
        {
            Emphasize(previous, selected: false, animate);
        }

        if (index >= 0)
        {
            Emphasize(index, selected: true, animate);
        }
    }

    private void Emphasize(int index, bool selected, bool animate)
    {
        var label = _labels[index];
        var row = _rows[index];
        var oldSize = label.FontSize;
        ApplyLabelStyle(label, selected, animate);
        var newSize = label.FontSize;
        if (animate && newSize > 0 && Math.Abs(oldSize - newSize) > 0.01)
        {
            // The glyphs are laid out at the final size once; the growth/shrink is a render-thread scale of the
            // fixed-size row around its centre (the label is centred in it).
            Animator.Animate(row, MotionState.Zoomed(oldSize / newSize), MotionState.Rest, M.WheelText, M.EaseStd);
        }
        else
        {
            Animator.Running(row)?.Cancel();
        }
    }

    private void ApplyLabelStyle(TextBlock label, bool selected, bool animate)
    {
        // Colour fades over WheelText (`color 0.2s ease`); instant positioning (open, Items change) skips the fade.
        label.Transitions = animate && M.SystemAnimationsEnabled
            ? label.Transitions ?? [new BrushTransition { Property = TextBlock.ForegroundProperty, Duration = M.Ms(M.WheelText), Easing = M.EaseStd }]
            : null;
        label.Foreground = selected ? SelectedForeground ?? Foreground : Foreground;
        label.FontSize = selected ? SelectedFontSize : FontSize;
        label.FontWeight = selected ? SelectedFontWeight : FontWeight;
        label.Margin = new Thickness(0, LabelTop(label.FontSize, label.FontWeight), 0, 0);
    }

    /// <summary>
    /// Top of a number's text box so that its baseline lands where Chromium puts it in the prototype's 34 px row button
    /// (font inherited, so the root line height 1.4): floor((row − line)/2) + floor((line − (a + d))/2) + a, with the
    /// font's ascent a and descent d rounded to whole px. That is 22 for both the 15 px and the 19 px numbers
    /// (dialog-time.png); centring the 19 px text box instead put the selected number a pixel low.
    /// </summary>
    private double LabelTop(double fontSize, FontWeight weight)
    {
        var metrics = new Typeface(FontFamily, FontStyle.Normal, weight).GlyphTypeface.Metrics;
        if (metrics.DesignEmHeight <= 0 || fontSize <= 0)
        {
            return 0;
        }

        var ascent = Math.Abs(metrics.Ascent) * fontSize / metrics.DesignEmHeight;
        var descent = Math.Abs(metrics.Descent) * fontSize / metrics.DesignEmHeight;
        var line = CssLineHeight * fontSize;
        var baseline = Math.Floor((ItemHeight - line) / 2)
                       + Math.Floor((line - (Math.Round(ascent) + Math.Round(descent))) / 2)
                       + Math.Round(ascent);
        return Math.Max(0, baseline - ascent);
    }
}
