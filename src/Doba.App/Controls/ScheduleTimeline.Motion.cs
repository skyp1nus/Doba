using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Doba.App.Motion;
using Doba.Core.Editing;
using Doba.Core.Projections;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Controls;

/// <summary>
/// The schedule's motion (motion spec «Розклад» › «Рух», the prototype's <c>.sb*</c> transitions, <c>sbLift</c> /
/// <c>sbFade</c> and <c>sbFlip</c>). Everything here is composition opacity, translation and scale through
/// <see cref="Animator"/>; widths, heights and positions jump to their new values at once. Each layer has one job, because
/// an Animator animation owns a visual's opacity, translation and scale together:
/// <list type="table">
/// <item><term>block item container</term><description>the FLIP translation after a drop, an Esc or a key step (the
/// dragged block: <see cref="M.Land"/> / <see cref="M.Snap"/>, neighbours <see cref="M.Land"/>) and the landing scale
/// 1.02 → 1;</description></item>
/// <item><term>PART_Body</term><description>dim to 0.55 under the dragged copy and back (<see cref="M.Fade"/>,
/// continued from where it is: <see cref="Animator.Retarget"/>);</description></item>
/// <item><term>PART_ContentBox</term><description>the origin's content fading out (<see cref="M.Fade"/>; it comes back at
/// once, as the prototype's <c>cop2T: none</c>);</description></item>
/// <item><term>PART_Shadow</term><description>the resize shadow in and out (<see cref="M.Hover"/>, ease — the prototype's
/// <c>.sbs</c> transition) and the landing shadow out (<see cref="M.Land"/>, ease-in);</description></item>
/// <item><term>PART_Copy / PART_CopyLift / PART_CopyShadow</term><description>the copy's glide between slots
/// (<see cref="M.Snap"/>), its lift scale 1 → 1.02 and its shadow fading in (<see cref="M.Lift"/>);</description></item>
/// <item><term>PART_Chip</term><description>the time chip fading in (<see cref="M.Fade"/>), gliding with the copy or a key
/// step (<see cref="M.Snap"/>) and gone at once when hidden.</description></item>
/// </list>
/// The block root's own opacity stays the tag filter's (a 300 ms XAML transition), so the two dims multiply and never
/// fight. While the dragged block flies (a landing or a key step) the blocks layer is drawn above the now-line and the
/// "+N"s (the prototype's <c>zIndex: 3</c>), and a keyboard focus ring slides with its block. With OS animations off
/// everything is instant and the copy has no lift, no shadow and no flight (it still follows the pointer from slot to
/// slot).
/// </summary>
public partial class ScheduleTimeline
{
    /// <summary>A FLIP smaller than this (px) is not worth an animation (the prototype's 0.5 px).</summary>
    private const double FlipEpsilon = 0.5;

    /// <summary>The class that keeps a block's shadow drawn while it fades out after a drop or a resize.</summary>
    private const string LandingClass = "landing";

    /// <summary>The block view-models whose state this timeline follows, by their item container.</summary>
    private readonly Dictionary<Control, ScheduleBlockViewModel> _watched = [];

    private ScheduleChipViewModel? _chip;

    /// <summary>The chip's top while it is shown (its glide starts from there), or null while hidden.</summary>
    private double? _chipTop;

    private double? _underOpacity;

    /// <summary>The dragged block's flights now running; while there is one, the blocks layer is raised.</summary>
    private readonly HashSet<MotionHandle> _flights = [];

    /// <summary>The dragged copy's position layer (tests).</summary>
    internal Panel CopyLiftPanel => PART_CopyLift;

    /// <summary>The dragged copy's shadow (tests).</summary>
    internal Border CopyShadow => PART_CopyShadow;

    /// <summary>The item container of <paramref name="block"/> (the layer of its FLIP and landing), if realised.</summary>
    internal Control? ContainerOf(ScheduleBlockViewModel block) => PART_Blocks.ContainerFromItem(block);

    /// <summary>Opacity of a block's body under the dragged copy (<c>Opacity.Schedule.Under</c>, 0.55).</summary>
    private double UnderOpacity =>
        _underOpacity ??= this.TryFindResource("Opacity.Schedule.Under", out var value) && value is double d
            ? d
            : throw new InvalidOperationException("The Opacity.Schedule.Under token is missing.");

    private void InitMotion()
    {
        PART_Blocks.ContainerPrepared += OnBlockContainerPrepared;
        PART_Blocks.ContainerClearing += OnBlockContainerClearing;
    }

    /// <summary>Follows the new view-model's chip (the old one's chip state is dropped at once).</summary>
    private void AttachMotion(ScheduleViewModel? vm)
    {
        if (_chip is not null)
        {
            _chip.PropertyChanged -= OnChipPropertyChanged;
        }

        _chip = vm?.Chip;
        if (_chip is not null)
        {
            _chip.PropertyChanged += OnChipPropertyChanged;
        }

        Animator.Reset(PART_Chip);
        _chipTop = _chip is { IsVisible: true } chip ? chip.Top : null;
    }

    // ---- Block state: the dim under the copy and the origin's content ------------------------------------------------

    private void OnBlockContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (PART_Blocks.ItemFromContainer(e.Container) is not ScheduleBlockViewModel block)
        {
            return;
        }

        Unwatch(e.Container);
        _watched[e.Container] = block;
        block.PropertyChanged += OnBlockPropertyChanged;
        if (block.IsUnderCopy || block.IsOrigin)
        {
            // A block drawn for the first time during a drag (the template is applied on the next layout pass).
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                SyncDim(block);
                SyncOrigin(block);
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnBlockContainerClearing(object? sender, ContainerClearingEventArgs e) => Unwatch(e.Container);

    private void Unwatch(Control container)
    {
        if (_watched.Remove(container, out var block))
        {
            block.PropertyChanged -= OnBlockPropertyChanged;
        }
    }

    private void OnBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ScheduleBlockViewModel block)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ScheduleBlockViewModel.IsUnderCopy):
                SyncDim(block);
                break;
            case nameof(ScheduleBlockViewModel.IsOrigin):
                SyncOrigin(block);
                break;
        }
    }

    /// <summary>Under the dragged copy (or a growing block) the body dims to 0.55, and back to 1 when it leaves; both
    /// 130 ms ease-out, continuing from where the body is drawn.</summary>
    private void SyncDim(ScheduleBlockViewModel block)
    {
        if (PartOf<Panel>(block, "PART_Body") is not { } body)
        {
            return;
        }

        var to = block.IsUnderCopy ? new MotionState(UnderOpacity, 0, 0, 1, 1) : MotionState.Rest;
        Animator.Retarget(body, to, M.Fade, M.EaseOut);
    }

    /// <summary>The origin of a move fades its content out (130 ms ease-out); it comes back at once.</summary>
    private void SyncOrigin(ScheduleBlockViewModel block)
    {
        if (PartOf<Border>(block, "PART_ContentBox") is not { } content)
        {
            return;
        }

        if (block.IsOrigin)
        {
            Animator.Retarget(content, MotionState.Hidden, M.Fade, M.EaseOut);
        }
        else
        {
            Animator.Reset(content);
        }
    }

    // ---- The time chip ---------------------------------------------------------------------------------------------------

    /// <summary>The chip fades in when it appears, glides to each new line while shown and is gone at once when hidden.</summary>
    private void OnChipPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_chip is not { } chip)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ScheduleChipViewModel.IsVisible) when chip.IsVisible:
                _chipTop = chip.Top;
                Animator.Animate(PART_Chip, MotionState.Hidden, MotionState.Rest, M.Fade, M.EaseOut);
                break;
            case nameof(ScheduleChipViewModel.IsVisible):
                _chipTop = null;
                Animator.Reset(PART_Chip);
                break;
            case nameof(ScheduleChipViewModel.Top) when chip.IsVisible && _chipTop is { } old:
                _chipTop = chip.Top;
                if (Math.Abs(old - chip.Top) >= FlipEpsilon)
                {
                    // From where it is drawn (mid-glide or mid-fade) to its new line.
                    var shown = Animator.Shown(PART_Chip);
                    Animator.Animate(PART_Chip, new MotionState(shown.Opacity, 0, old + shown.Y - chip.Top, 1, 1),
                        MotionState.Rest, M.Snap, M.EaseOut);
                }

                break;
        }
    }

    // ---- Drag ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The lift: a landing still running on the block ends at once; a move lifts the copy (scale 1 → 1.02 around its
    /// centre and its shadow 0 → 1, <see cref="M.Lift"/> ease-out — neither with OS animations off); a resize fades the
    /// block's shadow in (<see cref="M.Hover"/>, ease). The origin's fade and the neighbours' dim follow the view-model.
    /// </summary>
    partial void OnDragStarted(ScheduleBlockViewModel block, ScheduleDragMode mode)
    {
        if (ContainerOf(block) is { } container)
        {
            Animator.Running(container)?.Complete();
        }

        var shadow = PartOf<Border>(block, "PART_Shadow");
        if (shadow is not null)
        {
            Animator.Running(shadow)?.Complete();
        }

        var animate = M.SystemAnimationsEnabled;
        if (mode == ScheduleDragMode.Move)
        {
            Animator.Reset(PART_Copy);
            PART_CopyShadow.IsVisible = animate;
            if (!animate || _vm is not { } vm)
            {
                Animator.Reset(PART_CopyLift);
                Animator.Reset(PART_CopyShadow);
                return;
            }

            var centre = new RelativePoint(vm.Copy.Width / 2, vm.Copy.Height / 2, RelativeUnit.Absolute);
            Animator.Animate(PART_CopyLift, MotionState.Rest, MotionState.Zoomed(M.LiftScale), M.Lift, M.EaseOut, centre);
            Animator.Animate(PART_CopyShadow, MotionState.Hidden, MotionState.Rest, M.Lift, M.EaseOut);
        }
        else if (animate && shadow is not null)
        {
            Animator.Animate(shadow, MotionState.Hidden, MotionState.Rest, M.Hover, M.EaseStd);
        }
    }

    /// <summary>The copy glides to its new slot (<see cref="M.Snap"/> ease-out) from where it is drawn; a resize jumps
    /// (heights never animate). The chip follows through its own view-model.</summary>
    partial void OnDragStepped(ScheduleBlockViewModel block, ScheduleDragMode mode, ScheduleSlot from, ScheduleSlot to)
    {
        if (mode != ScheduleDragMode.Move)
        {
            return;
        }

        var dy = SlotTop(from.Start) + Animator.Shown(PART_Copy).Y - SlotTop(to.Start);
        if (Math.Abs(dy) >= FlipEpsilon)
        {
            Animator.Animate(PART_Copy, MotionState.Offset(0, dy), MotionState.Rest, M.Snap, M.EaseOut);
        }
    }

    /// <summary>
    /// The landing (the prototype's <c>sbFlip</c> after <c>sbEnd</c>): the copy is gone at once; a moved block flies from
    /// where the copy is drawn into its slot — a drop's new one, an Esc's old one — scaling 1.02 → 1 over
    /// <see cref="M.Land"/> ease-out while its shadow fades out (ease-in); a resized block's shadow fades out
    /// (<see cref="M.Hover"/>, ease) and it slides only if the re-layout moved it; every other block the drop re-laid out
    /// slides from its old place (<see cref="M.Land"/>; widths jump). Nothing flies with OS animations off.
    /// </summary>
    partial void OnDragEnded(ScheduleBlockViewModel block, ScheduleDragMode mode, bool committed, bool written, Rect from,
        IReadOnlyDictionary<long, Rect> before)
    {
        var move = mode == ScheduleDragMode.Move;
        var copy = Animator.Shown(PART_Copy);
        var lift = Animator.Shown(PART_CopyLift);
        double? copyShadow = PART_CopyShadow.IsVisible ? Animator.Shown(PART_CopyShadow).Opacity : null;
        Animator.Reset(PART_Copy);
        Animator.Reset(PART_CopyLift);
        Animator.Reset(PART_CopyShadow);

        if (!M.SystemAnimationsEnabled || _vm is not { } vm)
        {
            return;
        }

        FlipNeighbours(vm, before, block.Id);
        if (!vm.Blocks.Contains(block) || ContainerOf(block) is not { } container)
        {
            return; // folded under a "+N" by the drop
        }

        // Where the block is seen now: the copy (its glide and lift included), or the block itself for a resize.
        var shown = Animator.Shown(container);
        var origin = move ? new Point(from.X, from.Y + copy.Y) : new Point(from.X + shown.X, from.Y + shown.Y);
        var scale = move ? lift.ScaleX : 1;
        var dx = origin.X - block.X;
        var dy = origin.Y - block.Top;
        if (scale != 1 || Math.Abs(dx) >= FlipEpsilon || Math.Abs(dy) >= FlipEpsilon)
        {
            var centre = new RelativePoint(block.X + (block.Width / 2), block.Top + (block.Height / 2), RelativeUnit.Absolute);
            Fly(block, container, new MotionState(1, dx, dy, scale, scale), M.Land, centre);
        }

        if (PartOf<Border>(block, "PART_Shadow") is not { } shadow)
        {
            return;
        }

        if (move && copyShadow is { } lifted)
        {
            FadeShadow(block, shadow, lifted, M.Land, M.EaseIn);
        }
        else if (!move)
        {
            FadeShadow(block, shadow, Animator.Shown(shadow).Opacity, M.Hover, M.EaseStd);
        }
    }

    // ---- Keyboard ----------------------------------------------------------------------------------------------------------

    /// <summary>A key step: the block slides from its old place (<see cref="M.Snap"/> ease-out) with its keyboard focus
    /// ring, the blocks the step re-laid out from theirs (<see cref="M.Land"/>). Nothing slides with OS animations off.</summary>
    partial void OnKeyStepped(ScheduleBlockViewModel block, ScheduleDragMode mode, IReadOnlyDictionary<long, Rect> before)
    {
        if (!M.SystemAnimationsEnabled || _vm is not { } vm)
        {
            return;
        }

        FlipNeighbours(vm, before, block.Id);
        if (vm.Blocks.Contains(block) && ContainerOf(block) is { } container && before.TryGetValue(block.Id, out var old))
        {
            var shown = Animator.Shown(container);
            var dx = old.X + shown.X - block.X;
            var dy = old.Y + shown.Y - block.Top;
            if (Math.Abs(dx) >= FlipEpsilon || Math.Abs(dy) >= FlipEpsilon)
            {
                Fly(block, container, MotionState.Offset(dx, dy), M.Snap, null);
            }
        }
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------------------

    /// <summary>Every block other than <paramref name="primary"/> that the last write re-laid out slides from where it was
    /// drawn to its new place (<see cref="M.Land"/> ease-out). A block whose place did not change keeps whatever it is
    /// doing; one drawn for the first time just appears.</summary>
    private void FlipNeighbours(ScheduleViewModel vm, IReadOnlyDictionary<long, Rect> before, long primary)
    {
        foreach (var b in vm.Blocks)
        {
            if (b.Id == primary || !before.TryGetValue(b.Id, out var old) || (old.X == b.X && old.Y == b.Top)
                || ContainerOf(b) is not { } container)
            {
                continue;
            }

            var shown = Animator.Shown(container);
            Animator.Animate(container, MotionState.Offset(old.X + shown.X - b.X, old.Y + shown.Y - b.Top), MotionState.Rest,
                M.Land, M.EaseOut);
        }
    }

    /// <summary>
    /// The dragged block's flight into its slot, drawn above the other blocks, the now-line and the "+N"s while it lasts
    /// (the prototype's <c>zIndex: 3</c> during <c>sbFlip</c>, over the line's 2): its container is raised among the
    /// blocks and the blocks layer above <c>PART_Now</c> (still under the copy, the chip and the list). A keyboard focus
    /// ring on the block (drawn in the adorner layer, which composition offsets do not move) flies the same way, so it
    /// stays on the block as the prototype's outline does; it ends with the flight.
    /// </summary>
    private void Fly(ScheduleBlockViewModel block, Control container, MotionState from, int ms, RelativePoint? centre)
    {
        var ring = RingOf(block);
        // The ring is centred on the block (the open button fills it but for 1 px all round): its own centre is the origin.
        var ringHandle = ring is null ? null : Animator.Animate(ring, from, MotionState.Rest, ms, M.EaseOut);
        var handle = Animator.Animate(container, from, MotionState.Rest, ms, M.EaseOut, centre, h =>
        {
            ((Control)h.Target).ZIndex = 0;
            if (ringHandle is { IsAnimating: true })
            {
                if (h.WasCancelled)
                {
                    ringHandle.Cancel();
                }
                else
                {
                    ringHandle.Complete();
                }
            }

            if (_flights.Remove(h) && _flights.Count == 0)
            {
                PART_Blocks.ZIndex = 0;
            }
        });

        // Raised after starting: superseding an earlier flight runs its callback (which lowers it) first.
        if (handle.IsAnimating)
        {
            container.ZIndex = 1;
            _flights.Add(handle);
            PART_Blocks.ZIndex = PART_Now.ZIndex + 1;
        }
    }

    /// <summary>The visible keyboard focus ring on <paramref name="block"/>'s open button, if it has one.</summary>
    private Control? RingOf(ScheduleBlockViewModel block)
    {
        if (PartOf<Button>(block, "PART_Open") is not { IsFocused: true } open
            || AdornerLayer.GetAdornerLayer(open) is not { } layer)
        {
            return null;
        }

        return layer.Children.FirstOrDefault(a => a is FocusRing { IsVisible: true } && ReferenceEquals(AdornerLayer.GetAdornedElement(a), open));
    }

    /// <summary>Keeps <paramref name="block"/>'s shadow drawn while it fades from <paramref name="opacity"/> to nothing.</summary>
    private void FadeShadow(ScheduleBlockViewModel block, Border shadow, double opacity, int ms, Avalonia.Animation.Easings.Easing easing)
    {
        if (BlockOf(block) is not { } root)
        {
            return;
        }

        var handle = Animator.Animate(shadow, new MotionState(opacity, 0, 0, 1, 1), MotionState.Hidden, ms, easing, null, h =>
        {
            root.Classes.Remove(LandingClass);
            if (!h.WasCancelled)
            {
                Animator.Reset(h.Target); // hidden by the style again: back to rest for the next lift
            }
        });

        // Added after starting: superseding an earlier fade runs its callback (which removes the class) first.
        if (handle.IsAnimating)
        {
            root.Classes.Add(LandingClass);
        }
    }

    private T? PartOf<T>(ScheduleBlockViewModel block, string name)
        where T : Control =>
        BlockOf(block) is { } root
            ? root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
            : null;

    private static double SlotTop(int minutes) => ScheduleLayout.BlockTop(minutes, ScheduleLayout.DesignHourHeight);
}
