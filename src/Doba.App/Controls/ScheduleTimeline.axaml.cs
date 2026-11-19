using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Motion;
using Doba.Core.Projections;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Controls;

/// <summary>
/// The Today/Day schedule: fixed header, then the grid's own scroller with the hour lines 07–23 (48 px/hour), blocks in
/// overlap columns, "+N" stand-ins with their list, and the now-line. DataContext is a <see cref="ScheduleViewModel"/>.
/// Clicking a block (or Enter) opens the task; its checkbox, or Space on the focused block, marks it done. Width is the
/// caller's (design: <c>Size.Schedule.Width</c> = 380 inside a 24 px padded aside with a Border.Subtle left border);
/// the height is the aside's: the grid scrolls when it does not fit.
/// </summary>
/// <remarks>
/// The "+N" list closes on Esc (focus back on its "+N" when it was inside), a second click on its "+N", a press anywhere
/// else in the schedule, another day, the screen being left (<see cref="CloseList"/>) and a change of its tasks
/// (<see cref="ScheduleViewModel.Update"/>). Opened from the keyboard it focuses its first row.
/// </remarks>
public partial class ScheduleTimeline : UserControl
{
    private readonly ScheduleScroll _scroll;
    private ScheduleViewModel? _vm;

    /// <summary>The "+N" whose list is shown.</summary>
    private ScheduleOverflowViewModel? _shown;

    /// <summary>Enter / Space went down on a "+N": the list it opens takes the keyboard focus.</summary>
    private bool _openByKey;

    public ScheduleTimeline()
    {
        InitializeComponent();
        _scroll = new ScheduleScroll(PART_Scroll, this);
        InitDrag();
        InitMotion();
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        PART_Grid.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        PART_Grid.AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>The grid's scroller (tests; the screens' <c>ScheduleScroller</c>).</summary>
    internal ScrollViewer Scroller => PART_Scroll;

    /// <summary>The grid inside the scroller (tests).</summary>
    internal Panel GridPanel => PART_Grid;

    /// <summary>The now-line row (tests; Today scrolls it into view).</summary>
    internal Control NowLine => PART_Now;

    /// <summary>The blocks list (tests).</summary>
    internal ItemsControl BlockList => PART_Blocks;

    /// <summary>The "+N" list (tests).</summary>
    internal ItemsControl OverflowList => PART_Overflows;

    /// <summary>The "+N" popover (tests).</summary>
    internal Border ListPopover => PART_List;

    /// <summary>The live region (tests).</summary>
    internal TextBlock LiveRegion => PART_Live;

    /// <summary>The earliest block's root, or null when the day has no drawn timed task.</summary>
    internal Control? FirstBlock => Blocks().MinBy(b => b.Bounds.Y);

    /// <summary>The drawn block roots (<c>PART_Block</c>).</summary>
    internal IEnumerable<Panel> Blocks() =>
        PART_Blocks.GetVisualDescendants().OfType<Panel>().Where(p => p.Name == "PART_Block");

    /// <summary>The root of <paramref name="block"/>'s container, if realised.</summary>
    internal Panel? BlockOf(ScheduleBlockViewModel block) =>
        PART_Blocks.ContainerFromItem(block) is ContentPresenter { Child: Panel root } ? root : null;

    /// <summary>The "+N" button of <paramref name="overflow"/>, if realised.</summary>
    internal Button? MoreOf(ScheduleOverflowViewModel overflow) =>
        PART_Overflows.ContainerFromItem(overflow) is ContentPresenter { Child: Button more } ? more : null;

    /// <summary>Day: another day is shown — place the grid again (now-line or earliest block).</summary>
    public void ResetScroll() => _scroll.Reset();

    /// <summary>The screen is left: close the "+N" list and cancel a drag (nothing written).</summary>
    public void CloseList()
    {
        Drag.Cancel();
        _vm?.CloseOverflow();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as ScheduleViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        AttachDrag(_vm);
        AttachMotion(_vm);
        ShowList();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _spaceDown = false;
        _openByKey = false;
        CloseList();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScheduleViewModel.OpenOverflow))
        {
            ShowList();
        }
    }

    /// <summary>Shows, fills and places the list of the open "+N", or hides it (at once, as the prototype unmounts it).
    /// A list that had the focus gives it back to its "+N".</summary>
    private void ShowList()
    {
        var open = _vm?.OpenOverflow;
        if (ReferenceEquals(open, _shown))
        {
            return;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var hadFocus = focused is Visual v && (ReferenceEquals(v, PART_List) || PART_List.IsVisualAncestorOf(v));
        var keyboard = hadFocus && focused is StyledElement s && s.Classes.Contains(":focus-visible");
        var previous = _shown;
        _shown = open;
        Animator.Running(PART_List)?.Cancel();

        if (open is null)
        {
            PART_List.IsVisible = false;
            PART_List.DataContext = null;
            if (hadFocus && previous is not null && MoreOf(previous) is { } more)
            {
                more.Focus(keyboard ? NavigationMethod.Tab : NavigationMethod.Pointer);
            }

            return;
        }

        PART_List.DataContext = open; // its TabIndex is bound to the "+N"'s TabOrder (a re-projection can renumber it)
        PART_List.IsVisible = true;

        // sbPop: opacity 0 → 1, translateY −4 → 0, scale 0.98 → 1 from the corner next to the "+N" (top right below it,
        // bottom right above it). The list has not been measured yet: its corner comes from the design's geometry.
        var height = ScheduleLayout.PopoverHead + (open.Rows.Count * ScheduleLayout.PopoverRow) + ScheduleLayout.PopoverFoot;
        Animator.Animate(PART_List, new MotionState(0, 0, M.PopY, M.PopScale, M.PopScale), MotionState.Rest, M.Pop, M.EaseOut,
            new RelativePoint(ScheduleLayout.PopoverWidth, open.PopoverBelow ? 0 : height, RelativeUnit.Absolute));

        if (_openByKey)
        {
            _openByKey = false;
            Dispatcher.UIThread.Post(FocusFirstRow, DispatcherPriority.Background);
        }
    }

    private void FocusFirstRow()
    {
        if (_shown is not null
            && PART_ListRows.ContainerFromIndex(0) is { } row
            && row.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "PART_ListOpen") is { } open)
        {
            open.Focus(NavigationMethod.Tab);
        }
    }

    /// <summary>A press anywhere in the schedule but on the open list or its "+N" closes the list.</summary>
    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _openByKey = false;
        if (_shown is not { } open || e.Source is not Visual source)
        {
            return;
        }

        if (IsWithin(source, PART_List) || (MoreOf(open) is { } more && IsWithin(source, more)))
        {
            return;
        }

        _vm?.CloseOverflow();
    }

    /// <summary>Esc closes the open list (the window's Esc handling never sees it).</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && _vm?.OpenOverflow is not null)
        {
            _vm.CloseOverflow();
            e.Handled = true;
        }
    }

    private static bool IsWithin(Visual source, Visual scope) =>
        ReferenceEquals(source, scope) || scope.IsVisualAncestorOf(source);
}
