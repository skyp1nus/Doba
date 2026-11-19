using Avalonia;
using Avalonia.Controls;
using Doba.App.Controls;

namespace Doba.App.Screens.Today;

/// <summary>The Today screen; DataContext is a <see cref="TodayViewModel"/>. Its bounds are the shell's content area.</summary>
public partial class TodayView : UserControl
{
    public TodayView() => InitializeComponent();

    /// <summary>The list scroller (tests).</summary>
    internal ScrollViewer Scroller => PART_Scroll;

    /// <summary>The schedule column (tests).</summary>
    internal ScheduleTimeline Schedule => PART_Schedule;

    /// <summary>The schedule grid's own scroller, inside the timeline (tests).</summary>
    internal ScrollViewer ScheduleScroller => PART_Schedule.Scroller;

    /// <summary>The screen host hides a view it leaves (the view stays cached): close the schedule's "+N" list.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && !IsVisible)
        {
            PART_Schedule.CloseList();
        }
    }
}
