using System.ComponentModel;
using Avalonia.Controls;
using Doba.App.Controls;

namespace Doba.App.Screens.Day;

/// <summary>The Day screen. DataContext: <see cref="DayViewModel"/>. Its bounds are the shell's content area.</summary>
public partial class DayView : UserControl
{
    private readonly ScheduleScroll _scheduleScroll;
    private DayViewModel? _vm;

    public DayView()
    {
        InitializeComponent();
        _scheduleScroll = new ScheduleScroll(PART_ScheduleScroll, PART_Schedule);
    }

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

    /// <summary>The schedule's own scroller (tests).</summary>
    internal ScrollViewer ScheduleScroller => PART_ScheduleScroll;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as DayViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>The view is kept across days (screen cache): another day places its schedule again.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DayViewModel.Date))
        {
            _scheduleScroll.Reset();
        }
    }
}
