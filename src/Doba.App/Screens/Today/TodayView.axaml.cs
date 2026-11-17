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
}
