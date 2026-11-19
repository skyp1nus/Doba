using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Doba.App.Controls;

namespace Doba.App.Screens.Day;

/// <summary>The Day screen. DataContext: <see cref="DayViewModel"/>. Its bounds are the shell's content area.</summary>
public partial class DayView : UserControl
{
    private DayViewModel? _vm;

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
            PART_Schedule.ResetScroll();
        }
    }
}
