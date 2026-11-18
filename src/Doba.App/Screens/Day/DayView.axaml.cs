using Avalonia.Controls;
using Doba.App.Controls;

namespace Doba.App.Screens.Day;

/// <summary>The Day screen. DataContext: <see cref="DayViewModel"/>. Its bounds are the shell's content area.</summary>
public partial class DayView : UserControl
{
    public DayView() => InitializeComponent();

    /// <summary>The list scroller (tests).</summary>
    internal ScrollViewer Scroller => PART_Scroll;

    /// <summary>The list column (tests).</summary>
    internal StackPanel List => PART_List;

    /// <summary>"N / M done" (tests).</summary>
    internal TextBlock ProgressText => PART_Progress;

    /// <summary>"Nothing here" (tests).</summary>
    internal TextBlock EmptyText => PART_Empty;

    /// <summary>The "Add task" row (tests).</summary>
    internal AddTaskButton AddTask => PART_AddTask;

    /// <summary>The schedule column (tests).</summary>
    internal ScheduleTimeline Schedule => PART_Schedule;
}
