using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Doba.Core.Editing;

namespace Doba.App.Controls;

/// <summary>Where a schedule drag is (<see cref="ScheduleDrag"/>).</summary>
internal enum ScheduleDragState
{
    /// <summary>Nothing pressed.</summary>
    Idle,

    /// <summary>A block (or its grip) is pressed, the pointer has not travelled past the threshold yet: a release now is
    /// an ordinary click (the open button opens the task).</summary>
    Pending,

    /// <summary>The block is being moved: the copy follows the pointer from slot to slot.</summary>
    Moving,

    /// <summary>The block's bottom edge is being dragged: the block grows or shrinks in place.</summary>
    Resizing,
}

/// <summary>
/// The schedule's pointer drag (the prototype's <c>sbDown</c> / <c>sbMove</c> / <c>sbCompute</c> / <c>sbAuto</c> /
/// <c>sbUp</c> / <c>sbEnd</c>), attached to the timeline's grid.
/// <list type="bullet">
/// <item>A left-button or pen press on a drawn block arms it (<see cref="ScheduleDragState.Pending"/>); on its grip it
/// arms a resize. Presses on the checkbox, on a "+N" or its list, with another button, or by touch are ignored (touch taps
/// still click). The press is watched with <c>handledEventsToo</c> and never marked handled, so the block's open button
/// still gets it.</item>
/// <item>Past the threshold (the larger of |dx| and |dy| ≥ 4 DIP, 2 for a resize) the grid captures the pointer — the
/// open button loses its press, so no click and no editor after a drag — the cursor turns SizeAll (SizeNorthSouth for a
/// resize), and <see cref="ScheduleViewModel.BeginDrag"/> shows it.</item>
/// <item>Every move works out the slot with Core's <see cref="ScheduleEdit.Drag"/> from the pointer's travel plus the
/// scroller's scroll since the press, Alt from the event's modifiers (and on Alt down / up, through a window-level
/// tunnel key handler installed for the drag); near the scroller's top or bottom edge it autoscrolls once per frame
/// (<see cref="ScheduleEdit.AutoScrollSpeed"/>), working the slot out again after each scroll.</item>
/// <item>The left button's release drops it: ONE store write when the slot changed (<see cref="ScheduleViewModel.EndDrag"/>).
/// Esc, a lost capture (Alt+Tab, another element taking the pointer), another day, the screen being left and a store
/// change of the dragged task cancel it: nothing is written, "Move cancelled".</item>
/// </list>
/// While a drag runs, ↑ / ↓ / Space / Enter are swallowed (only Esc means something then).
/// </summary>
internal sealed class ScheduleDrag
{
    private static Cursor? s_moveCursor;
    private static Cursor? s_resizeCursor;

    private readonly ScheduleTimeline _timeline;
    private readonly Panel _grid;
    private readonly ScrollViewer _scroller;

    private ScheduleViewModel? _vm;
    private IPointer? _pointer;
    private ScheduleBlockViewModel? _block;
    private ScheduleDragMode _mode;

    /// <summary>The press, in scroller coordinates (they do not move when the grid scrolls).</summary>
    private Point _press;

    /// <summary>The scroller's offset at the press.</summary>
    private double _scroll0;

    /// <summary>The pointer's latest y in scroller coordinates (autoscroll keeps using it while the pointer rests).</summary>
    private double _lastY;

    /// <summary>Alt is held: snap to 5 minutes.</summary>
    private bool _alt;

    private Cursor? _savedCursor;
    private TopLevel? _keys;
    private bool _frameRequested;

    /// <summary>Bumped when a drag ends, so a frame requested by an earlier drag does nothing.</summary>
    private int _generation;

    public ScheduleDrag(ScheduleTimeline timeline, Panel grid, ScrollViewer scroller)
    {
        _timeline = timeline;
        _grid = grid;
        _scroller = scroller;
        grid.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        grid.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        grid.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        grid.PointerCaptureLost += OnCaptureLost;
    }

    /// <summary>The cursor while a block moves.</summary>
    internal static Cursor MoveCursor => s_moveCursor ??= new Cursor(StandardCursorType.SizeAll);

    /// <summary>The cursor while a block's end is dragged (also the grip's own cursor).</summary>
    internal static Cursor ResizeCursor => s_resizeCursor ??= new Cursor(StandardCursorType.SizeNorthSouth);

    public ScheduleDragState State { get; private set; }

    /// <summary>A block is being moved or resized.</summary>
    public bool IsActive => State is ScheduleDragState.Moving or ScheduleDragState.Resizing;

    /// <summary>
    /// The frame source for autoscroll: called with a one-shot per-frame callback. Null (the app) =
    /// <see cref="TopLevel.RequestAnimationFrame"/>; tests set their own and drive <see cref="Tick"/>.
    /// </summary>
    internal Action<Action<TimeSpan>>? RequestFrame { get; set; }

    /// <summary>The timeline shows another view-model: any drag is cancelled.</summary>
    public void Attach(ScheduleViewModel? vm)
    {
        if (ReferenceEquals(vm, _vm))
        {
            return;
        }

        Cancel();
        if (_vm is not null)
        {
            _vm.DragInvalidated -= OnDragInvalidated;
        }

        _vm = vm;
        if (_vm is not null)
        {
            _vm.DragInvalidated += OnDragInvalidated;
        }
    }

    /// <summary>Cancels a running drag (nothing written, "Move cancelled") or forgets an armed press.</summary>
    public void Cancel() => End(commit: false);

    /// <summary>
    /// One autoscroll frame (the prototype's <c>sbAuto</c> tick): while a drag runs over a scroller that can scroll and
    /// the pointer is in the 40 px zone at its top or bottom, scroll by the zone's speed (2–14 px, within the extent), work
    /// the slot out again, and ask for the next frame. Returns whether the scroller moved.
    /// </summary>
    internal bool Tick()
    {
        if (!IsActive || !CanAutoScroll())
        {
            return false;
        }

        var v = Speed();
        if (v == 0)
        {
            return false; // the loop ends until the pointer moves into a zone again
        }

        var before = _scroller.Offset.Y;
        var max = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        _scroller.Offset = new Vector(_scroller.Offset.X, Math.Max(0, Math.Min(max, before + v)));
        var moved = _scroller.Offset.Y != before;
        if (moved)
        {
            Compute();
        }

        EnsureFrame();
        return moved;
    }

    // ---- Pointer -------------------------------------------------------------------------------------------------------

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is not { } vm || State is not (ScheduleDragState.Idle or ScheduleDragState.Pending))
        {
            return;
        }

        // Left button or pen tip only; touch keeps its tap (click / checkbox).
        var point = e.GetCurrentPoint(_grid);
        if (e.Pointer.Type == PointerType.Touch || point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        if (BlockAt(e.Source as Visual) is not ({ } block, var grip) || !vm.Blocks.Contains(block))
        {
            return;
        }

        State = ScheduleDragState.Pending;
        _pointer = e.Pointer;
        _block = block;
        _mode = grip ? ScheduleDragMode.Resize : ScheduleDragMode.Move;
        _press = e.GetPosition(_scroller);
        _lastY = _press.Y;
        _scroll0 = _scroller.Offset.Y;
        _alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (State == ScheduleDragState.Idle || !ReferenceEquals(e.Pointer, _pointer))
        {
            return;
        }

        var p = e.GetPosition(_scroller);
        _lastY = p.Y;
        _alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (State == ScheduleDragState.Pending)
        {
            // The press was lost without a release reaching us (e.g. the window lost the mouse): forget it.
            if (!e.GetCurrentPoint(_grid).Properties.IsLeftButtonPressed)
            {
                End(commit: false);
                return;
            }

            if (!ScheduleEdit.PastThreshold(_mode, p.X - _press.X, p.Y - _press.Y))
            {
                return;
            }

            Activate(e.Pointer);
            if (!IsActive)
            {
                return;
            }
        }

        if (IsActive)
        {
            Compute();
            EnsureFrame();
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (State == ScheduleDragState.Idle || !ReferenceEquals(e.Pointer, _pointer) || e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        // Pending: an ordinary click (the open button has handled it already).
        End(commit: IsActive);
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (IsActive && ReferenceEquals(e.Source, _grid) && ReferenceEquals(e.Pointer, _pointer))
        {
            End(commit: false);
        }
    }

    private void OnDragInvalidated(object? sender, EventArgs e) => End(commit: false);

    /// <summary>The drawn block under <paramref name="source"/> and whether the press is on its grip; null on its
    /// checkbox, outside a block, or on a "+N" / its list.</summary>
    private (ScheduleBlockViewModel Block, bool Grip)? BlockAt(Visual? source)
    {
        var grip = false;
        for (var v = source; v is not null && !ReferenceEquals(v, _grid); v = v.GetVisualParent())
        {
            switch (v)
            {
                case Control { Name: "PART_Check" }:
                    return null;
                case Control { Name: "PART_Grip" }:
                    grip = true;
                    break;
                case Panel { Name: "PART_Block", DataContext: ScheduleBlockViewModel block }:
                    return (block, grip);
            }
        }

        return null;
    }

    /// <summary>Past the threshold: the grid takes the pointer (the open button loses its press), the cursor changes,
    /// window-level Esc / Alt handling goes in, and the drag shows its first slot.</summary>
    private void Activate(IPointer pointer)
    {
        if (_vm is not { } vm || _block is not { } block || !vm.Blocks.Contains(block))
        {
            End(commit: false);
            return;
        }

        State = _mode == ScheduleDragMode.Move ? ScheduleDragState.Moving : ScheduleDragState.Resizing;
        _savedCursor = _grid.Cursor;
        _grid.Cursor = _mode == ScheduleDragMode.Move ? MoveCursor : ResizeCursor;
        pointer.Capture(_grid);
        _keys = TopLevel.GetTopLevel(_grid);
        if (_keys is not null)
        {
            _keys.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            _keys.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        }

        vm.BeginDrag(block, _mode);
        _timeline.RaiseDragStarted(block, _mode);
    }

    /// <summary>Works the slot out from the pointer's travel and the scroll since the press (the prototype's
    /// <c>sbCompute</c>) and shows it when it changed.</summary>
    private void Compute()
    {
        if (!IsActive || _vm is not { Dragged: { } block } vm)
        {
            return;
        }

        var travel = _lastY - _press.Y + (_scroller.Offset.Y - _scroll0);
        var origin = vm.DragOrigin;
        var slot = ScheduleEdit.Drag(_mode, origin.Start, origin.Duration, travel, _alt);
        var from = vm.DragSlot;
        if (vm.PreviewDrag(slot))
        {
            _timeline.RaiseDragStepped(block, _mode, from, slot);
        }
    }

    // ---- Keys during a drag (window-level tunnel) ---------------------------------------------------------------------

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsActive)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                End(commit: false);
                break;
            case Key.LeftAlt or Key.RightAlt:
                e.Handled = true;
                SetAlt(true);
                break;
            case Key.Up or Key.Down or Key.Space or Key.Enter:
                e.Handled = true; // the block's keys wait for the drop
                break;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (IsActive && e.Key is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
            SetAlt(false);
        }
    }

    /// <summary>Alt went down or up while the pointer rests: snap again at once.</summary>
    private void SetAlt(bool alt)
    {
        if (_alt != alt)
        {
            _alt = alt;
            Compute();
        }
    }

    // ---- Autoscroll ----------------------------------------------------------------------------------------------------

    private bool CanAutoScroll() => _scroller.Extent.Height > _scroller.Viewport.Height + 1;

    private int Speed() => ScheduleEdit.AutoScrollSpeed(_lastY, 0, _scroller.Bounds.Height);

    private void EnsureFrame()
    {
        if (_frameRequested || !IsActive || !CanAutoScroll() || Speed() == 0)
        {
            return;
        }

        var generation = _generation;
        void OnFrame(TimeSpan _)
        {
            if (generation != _generation)
            {
                return;
            }

            _frameRequested = false;
            Tick();
        }

        if (RequestFrame is { } request)
        {
            _frameRequested = true;
            request(OnFrame);
        }
        else if (TopLevel.GetTopLevel(_grid) is { } top)
        {
            _frameRequested = true;
            top.RequestAnimationFrame(OnFrame);
        }
    }

    // ---- End -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Ends the drag: the window-level keys, the frame loop, the cursor and the capture go; then the view-model writes
    /// (<paramref name="commit"/>, a changed slot) or cancels. An armed press that never became a drag just ends.
    /// </summary>
    private void End(bool commit)
    {
        if (State == ScheduleDragState.Idle)
        {
            return;
        }

        var wasActive = IsActive;
        var vm = _vm;
        var block = _block;
        var mode = _mode;
        var pointer = _pointer;
        State = ScheduleDragState.Idle;
        _generation++;
        _frameRequested = false;
        _pointer = null;
        _block = null;
        if (_keys is not null)
        {
            _keys.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            _keys.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
            _keys = null;
        }

        if (!wasActive)
        {
            return;
        }

        _grid.Cursor = _savedCursor;
        _savedCursor = null;
        if (pointer is not null && ReferenceEquals(pointer.Captured, _grid))
        {
            pointer.Capture(null);
        }

        if (vm is null || block is null || !ReferenceEquals(vm.Dragged, block))
        {
            return;
        }

        // FLIP for the motion stage: every block's geometry before the write (the re-projection is synchronous), the
        // landing one from where it is seen now — the copy (move) or the grown block (resize).
        var before = vm.Blocks.ToDictionary(b => b.Id, b => new Rect(b.X, b.Top, b.Width, b.Height));
        var from = mode == ScheduleDragMode.Move
            ? new Rect(block.X, vm.Copy.Y, vm.Copy.Width, vm.Copy.Height)
            : new Rect(block.X, block.Top, block.Width, block.Height);
        var written = vm.EndDrag(commit);
        _timeline.RaiseDragEnded(block, mode, commit, written, from, before);
    }
}

/// <summary>The timeline's side of the drag: the controller, the copy's translation and the hooks the motion
/// (ScheduleTimeline.Motion.cs) implements. A landing never blocks input: a new press can start the next drag at once
/// (the landing then ends where it is going).</summary>
public partial class ScheduleTimeline
{
    private readonly TranslateTransform _copyShift = new();
    private ScheduleCopyViewModel? _copy;

    /// <summary>The pointer drag controller (tests: <see cref="ScheduleDrag.Tick"/>, <see cref="ScheduleDrag.RequestFrame"/>).</summary>
    internal ScheduleDrag Drag { get; private set; } = null!;

    /// <summary>The dragged copy's root (tests).</summary>
    internal Panel CopyPanel => PART_Copy;

    /// <summary>The time chip (tests).</summary>
    internal Control ChipPanel => PART_Chip;

    private void InitDrag()
    {
        Drag = new ScheduleDrag(this, PART_Grid, PART_Scroll);
        PART_Copy.RenderTransform = _copyShift;
    }

    /// <summary>Hands the new view-model to the drag (cancelling one in progress) and follows its copy's y.</summary>
    private void AttachDrag(ScheduleViewModel? vm)
    {
        Drag.Attach(vm);
        if (_copy is not null)
        {
            _copy.PropertyChanged -= OnCopyPropertyChanged;
        }

        _copy = vm?.Copy;
        if (_copy is not null)
        {
            _copy.PropertyChanged += OnCopyPropertyChanged;
        }

        _copyShift.Y = _copy?.Y ?? 0;
    }

    /// <summary>The copy steps between slots by translation only (its layout box stays at the grid's top).</summary>
    private void OnCopyPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScheduleCopyViewModel.Y) && _copy is not null)
        {
            _copyShift.Y = _copy.Y;
        }
    }

    internal void RaiseDragStarted(ScheduleBlockViewModel block, ScheduleDragMode mode) => OnDragStarted(block, mode);

    internal void RaiseDragStepped(ScheduleBlockViewModel block, ScheduleDragMode mode, ScheduleSlot from, ScheduleSlot to) =>
        OnDragStepped(block, mode, from, to);

    internal void RaiseDragEnded(ScheduleBlockViewModel block, ScheduleDragMode mode, bool committed, bool written, Rect from,
        IReadOnlyDictionary<long, Rect> before) =>
        OnDragEnded(block, mode, committed, written, from, before);

    /// <summary>Hook for the lift (motion spec: the copy scales 1 → 1.02 and its shadow fades in over <c>Dur.Lift</c>; the
    /// origin's content fades out in 130 ms). Called once the drag shows its first slot.</summary>
    partial void OnDragStarted(ScheduleBlockViewModel block, ScheduleDragMode mode);

    /// <summary>Hook for a step between slots (the copy and the chip glide <c>Dur.Snap</c>; a resize jumps). Called after
    /// the view-model shows <paramref name="to"/>.</summary>
    partial void OnDragStepped(ScheduleBlockViewModel block, ScheduleDragMode mode, ScheduleSlot from, ScheduleSlot to);

    /// <summary>
    /// Hook for the landing (motion spec: from <paramref name="from"/> into the slot, scale 1.02 → 1 and the shadow out
    /// over <c>Dur.Land</c>; Esc flies back the same way; the neighbours FLIP from <paramref name="before"/>). Called after
    /// the drop was written (<paramref name="written"/>) and re-projected, or after a cancel; the block may now be folded
    /// under a "+N" (no container).
    /// </summary>
    partial void OnDragEnded(ScheduleBlockViewModel block, ScheduleDragMode mode, bool committed, bool written, Rect from,
        IReadOnlyDictionary<long, Rect> before);
}
