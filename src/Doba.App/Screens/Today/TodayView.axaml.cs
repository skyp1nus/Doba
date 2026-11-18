using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Doba.App.Controls;

namespace Doba.App.Screens.Today;

/// <summary>The Today screen; DataContext is a <see cref="TodayViewModel"/>. Its bounds are the shell's content area.</summary>
public partial class TodayView : UserControl
{
    /// <summary>The schedule has had its first layout (and, if it overflows, was scrolled to the now-line).</summary>
    private bool _scheduleShown;

    public TodayView()
    {
        InitializeComponent();
        PART_ScheduleScroll.PropertyChanged += OnScheduleScrollPropertyChanged;
    }

    /// <summary>The list scroller (tests).</summary>
    internal ScrollViewer Scroller => PART_Scroll;

    /// <summary>The schedule column (tests).</summary>
    internal ScheduleTimeline Schedule => PART_Schedule;

    /// <summary>The schedule's own scroller (tests).</summary>
    internal ScrollViewer ScheduleScroller => PART_ScheduleScroll;

    /// <summary>On the first layout of the schedule: when it is taller than the aside (small windows), centre the
    /// now-line — or, without one, the first block — in the aside.</summary>
    private void OnScheduleScrollPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_scheduleShown || (e.Property != ScrollViewer.ViewportProperty && e.Property != ScrollViewer.ExtentProperty)
            || PART_ScheduleScroll.Viewport.Height <= 0 || PART_ScheduleScroll.Extent.Height <= 0)
        {
            return;
        }

        _scheduleShown = true;
        if (PART_ScheduleScroll.Extent.Height > PART_ScheduleScroll.Viewport.Height)
        {
            // After the current layout pass, so the now-line and blocks have their final positions.
            Dispatcher.UIThread.Post(ScrollScheduleToNow, DispatcherPriority.Background);
        }
    }

    private void ScrollScheduleToNow()
    {
        Control? target = PART_Schedule.NowLine.IsVisible ? PART_Schedule.NowLine : PART_Schedule.FirstBlock;
        if (target?.TranslatePoint(new Point(0, target.Bounds.Height / 2), PART_Schedule) is not { } centre)
        {
            return;
        }

        var viewport = PART_ScheduleScroll.Viewport.Height;
        var max = Math.Max(0, PART_ScheduleScroll.Extent.Height - viewport);
        PART_ScheduleScroll.Offset = new Vector(0, Math.Clamp(Math.Round(centre.Y - (viewport / 2)), 0, max));
    }
}
