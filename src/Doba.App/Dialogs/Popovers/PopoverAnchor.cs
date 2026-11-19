using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Doba.App.Motion;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Dialogs.Popovers;

/// <summary>
/// A pill of the task dialog together with its popover (the prototype's <c>&lt;div style="position: relative"&gt;</c>
/// around each pill): the first child is the pill and gives the anchor its size; while <see cref="IsOpen"/> the popover
/// (created once by <see cref="PopoverFactory"/>, then reused) is added as a second child and drawn under the pill —
/// <see cref="Offset"/> (36 px) below the pill's top, left-aligned with it — without taking part in the layout of the
/// pill row. It is an in-window element, never a Popup: it sits in the tree right after its pill (so Tab goes pill →
/// popover → next pill), above the rest of the dialog (the open anchor gets a higher ZIndex, the dialog draws the pill
/// row above the footer) and is not clipped by the dialog panel.
/// </summary>
/// <remarks>
/// <para><b>Small windows.</b> The popover is kept inside the window with an <see cref="Edge"/> margin: when it would
/// cross the right or bottom edge it is moved left / up by the overflow (it may then cover its pill); when it is larger
/// than the window it is pinned to the top / left margin. The shift is computed during arrange and re-checked after
/// each layout pass while open (ancestors may move after the anchor is arranged, e.g. on a window resize).</para>
/// <para><b>Motion.</b> Opening plays the popover entrance (opacity 0→1, translateY −4→0, scale 0.98→1 around its
/// top-left corner, <see cref="M.Pop"/>, ease-out) on the composition visual; closing removes it at once. Removal
/// detaches the popover from the tree, which flushes a time wheel's pending commit (quick "Done").</para>
/// <para><b>Focus.</b> When the popover closes while it holds keyboard focus (Esc, a picked value), focus returns to the
/// pill, with a visible ring only if the popover had keyboard (focus-visible) focus.</para>
/// </remarks>
public sealed class PopoverAnchor : Panel
{
    /// <summary>Defines the <see cref="IsOpen"/> property.</summary>
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<PopoverAnchor, bool>(nameof(IsOpen));

    /// <summary>Defines the <see cref="Offset"/> property.</summary>
    public static readonly StyledProperty<double> OffsetProperty =
        AvaloniaProperty.Register<PopoverAnchor, double>(nameof(Offset));

    /// <summary>Defines the <see cref="Edge"/> property.</summary>
    public static readonly StyledProperty<double> EdgeProperty =
        AvaloniaProperty.Register<PopoverAnchor, double>(nameof(Edge));

    private const double Epsilon = 0.5;

    private Vector _shift;
    private bool _listening;
    private double _measuredMaxHeight = double.PositiveInfinity;

    static PopoverAnchor()
    {
        AffectsArrange<PopoverAnchor>(OffsetProperty, EdgeProperty);
    }

    /// <summary>Shows the popover (bound to the dialog's IsDateOpen / IsTimeOpen / …).</summary>
    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>Distance from the pill's top to the popover's top (Space.Dialog.PopoverTop, 36).</summary>
    public double Offset
    {
        get => GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    /// <summary>Minimum distance between the popover and the window edges (Space.Dialog.PopoverEdge, 8).</summary>
    public double Edge
    {
        get => GetValue(EdgeProperty);
        set => SetValue(EdgeProperty, value);
    }

    /// <summary>Creates the popover the first time it opens (the dialog sets it; popovers are built only when used).</summary>
    public Func<Control>? PopoverFactory { get; set; }

    /// <summary>The popover once created (null before the first open); in the tree only while open.</summary>
    public Control? Popover { get; private set; }

    /// <summary>True while the popover is in the tree.</summary>
    public bool IsShowing => Popover is not null && Children.Contains(Popover);

    /// <summary>How far the popover was moved from its natural place (under the pill) to stay inside the window.</summary>
    public Vector Shift => _shift;

    /// <summary>The pill (first child).</summary>
    private Control? Pill => Children.Count > 0 && !ReferenceEquals(Children[0], Popover) ? Children[0] : null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty)
        {
            if (change.GetNewValue<bool>())
            {
                Open();
            }
            else
            {
                Close();
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsOpen && !IsShowing)
        {
            Open();
        }

        if (IsShowing)
        {
            Listen(true);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Listen(false);
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = default(Size);
        foreach (var child in Children)
        {
            if (ReferenceEquals(child, Popover))
            {
                // No taller than the window inside its edges: a long list (the Tag popover) scrolls instead of running
                // off the bottom. The fixed-size popovers are far below this at the smallest window.
                _measuredMaxHeight = MaxPopoverHeight();
                child.Measure(new Size(double.PositiveInfinity, _measuredMaxHeight));
                continue;
            }

            child.Measure(availableSize);
            size = new Size(Math.Max(size.Width, child.DesiredSize.Width), Math.Max(size.Height, child.DesiredSize.Height));
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            if (ReferenceEquals(child, Popover))
            {
                if (ComputeShift(child.DesiredSize) is { } shift)
                {
                    _shift = shift;
                }

                child.Arrange(new Rect(new Point(_shift.X, Offset + _shift.Y), child.DesiredSize));
                continue;
            }

            child.Arrange(new Rect(finalSize));
        }

        return finalSize;
    }

    private void Open()
    {
        if (!IsAttachedToVisualTree())
        {
            return; // opened on attach
        }

        Popover ??= PopoverFactory?.Invoke();
        if (Popover is not { } popover || Children.Contains(popover))
        {
            return;
        }

        _shift = default;
        Children.Add(popover);
        ZIndex = 1;
        Listen(true);
        Animator.Animate(popover, new MotionState(0, 0, M.PopY, M.PopScale, M.PopScale), MotionState.Rest, M.Pop, M.EaseOut,
            new RelativePoint(0, 0, RelativeUnit.Relative));
    }

    private void Close()
    {
        Listen(false);
        ZIndex = 0;
        if (Popover is not { } popover || !Children.Contains(popover))
        {
            return;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var hadFocus = focused is Visual v && (ReferenceEquals(v, popover) || popover.IsVisualAncestorOf(v));
        var keyboard = hadFocus && focused is StyledElement s && s.Classes.Contains(":focus-visible");
        Animator.Running(popover)?.Cancel();
        Children.Remove(popover); // closes instantly (motion spec)
        if (hadFocus && Pill is { } pill)
        {
            pill.Focus(keyboard ? NavigationMethod.Tab : NavigationMethod.Pointer);
        }
    }

    private bool IsAttachedToVisualTree() => TopLevel.GetTopLevel(this) is not null;

    private void Listen(bool on)
    {
        if (on == _listening)
        {
            return;
        }

        _listening = on;
        if (on)
        {
            LayoutUpdated += OnLayoutUpdated;
        }
        else
        {
            LayoutUpdated -= OnLayoutUpdated;
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (Popover is not { } popover || !IsShowing)
        {
            return;
        }

        var maxHeight = MaxPopoverHeight();
        if (maxHeight != _measuredMaxHeight)
        {
            InvalidateMeasure(); // the window was resized: the popover's height limit changed
            return;
        }

        if (ComputeShift(popover.DesiredSize) is not { } shift)
        {
            return;
        }

        if (Math.Abs(shift.X - _shift.X) > Epsilon || Math.Abs(shift.Y - _shift.Y) > Epsilon)
        {
            _shift = shift;
            InvalidateArrange();
        }
    }

    /// <summary>The tallest a popover may be: the window's height less <see cref="Edge"/> above and below (infinite when
    /// not in a window).</summary>
    private double MaxPopoverHeight() =>
        TopLevel.GetTopLevel(this) is { } top ? Math.Max(0, top.ClientSize.Height - (2 * Edge)) : double.PositiveInfinity;

    /// <summary>The move that keeps a popover of <paramref name="size"/> inside the window, or null when not in a window.</summary>
    private Vector? ComputeShift(Size size)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || this.TranslatePoint(default, top) is not { } origin)
        {
            return null;
        }

        var bounds = top.ClientSize;
        var edge = Edge;
        var x = origin.X;
        var y = origin.Y + Offset;
        var left = Math.Max(edge, Math.Min(x, bounds.Width - edge - size.Width));
        var top2 = Math.Max(edge, Math.Min(y, bounds.Height - edge - size.Height));
        return new Vector(Math.Round(left - x), Math.Round(top2 - y));
    }
}
