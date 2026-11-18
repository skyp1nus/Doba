using Avalonia;
using Avalonia.Controls;

namespace Doba.App.Dialogs.Popovers;

/// <summary>
/// The Time popover. Each time it is shown both wheels jump (no animation) to the draft's time
/// (<c>TaskDialog.WheelIndices</c>); a settled wheel commits through <see cref="TaskDialogViewModel.CommitHour"/> /
/// <see cref="TaskDialogViewModel.CommitMinute"/>. The popover is removed from the tree when it closes, which flushes a
/// commit still waiting for the 120 ms of stillness (e.g. Done right after a scroll).
/// </summary>
public partial class TimePopover : UserControl
{
    public TimePopover()
    {
        InitializeComponent();

        // Set here, not bound: a binding would drop the items when the popover leaves the logical tree on close, which
        // resets the wheels before the visual detach can flush a pending commit.
        PART_Hours.Items = TaskDialogViewModel.HourLabels;
        PART_Minutes.Items = TaskDialogViewModel.MinuteLabels;
        PART_Hours.Committed += (_, index) => Dialog?.CommitHour(index);
        PART_Minutes.Committed += (_, index) => Dialog?.CommitMinute(index);
    }

    /// <summary>The dialog the wheels commit to (kept explicitly: a commit flushed while the popover is being removed
    /// arrives after the inherited DataContext is gone).</summary>
    public TaskDialogViewModel? Dialog { get; set; }

    /// <summary>The hour column.</summary>
    internal Controls.TimeWheel HourWheel => PART_Hours;

    /// <summary>The minute column.</summary>
    internal Controls.TimeWheel MinuteWheel => PART_Minutes;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var dialog = Dialog ?? DataContext as TaskDialogViewModel;
        if (dialog is not null)
        {
            Dialog = dialog;
            var (hour, minute) = dialog.WheelIndices;
            PART_Hours.ScrollToIndex(hour, animate: false);
            PART_Minutes.ScrollToIndex(minute, animate: false);
        }
    }
}
