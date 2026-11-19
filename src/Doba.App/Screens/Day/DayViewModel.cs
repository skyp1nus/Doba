using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Controls;
using Doba.App.Services;
using Doba.App.ViewModels;
using Doba.Core.Projections;
using Doba.Core.Time;

namespace Doba.App.Screens.Day;

/// <summary>
/// The Day screen: "TASKS · N / M done", the multi-day spans covering the day ("Day k of n"), the day's single-day
/// tasks as cards (sorted by time), "Nothing here", the dashed "Add task" and the schedule (blocks + now-line when the
/// day is today). Everything comes from <see cref="Projector.Day"/> for <see cref="ScreenViewModel.State"/>'s anchor;
/// spans, cards and blocks are reconciled by task id (<see cref="KeyedSync"/>) so they update in place. The minute
/// tick only moves the now-line.
/// </summary>
public sealed partial class DayViewModel : ScreenViewModel
{
    private readonly Func<TaskRow, TaskRowViewModel> _createRow;
    private DateOnly _projectedToday;

    public DayViewModel(ScreenServices services)
        : base(services)
    {
        _createRow = r => new TaskRowViewModel(r, Services.Actions, Services.Commands);
        Schedule = new ScheduleViewModel(services.Actions, services.Commands);
    }

    /// <summary>Multi-day tasks covering the day (meta "Day k of n"), in the projection's order.</summary>
    public ObservableCollection<TaskRowViewModel> Spans { get; } = [];

    /// <summary>The day's single-day tasks, sorted by time as the projection gives them.</summary>
    public ObservableCollection<TaskRowViewModel> Items { get; } = [];

    /// <summary>"4 / 6 done"; empty when the day has no single-day tasks.</summary>
    [ObservableProperty] public partial string Progress { get; private set; } = string.Empty;

    [ObservableProperty] public partial bool HasSpans { get; private set; }

    [ObservableProperty] public partial bool HasItems { get; private set; }

    /// <summary>No spans and no tasks: "Nothing here".</summary>
    [ObservableProperty] public partial bool IsEmpty { get; private set; }

    /// <summary>The day shown (last projection).</summary>
    [ObservableProperty] public partial DateOnly Date { get; private set; }

    public ScheduleViewModel Schedule { get; }

    /// <summary>Task actions whose pending entrance the rows consume (set on the view root).</summary>
    public TaskActions Entrances => Services.Actions;

    /// <summary>Number of projections run (tests).</summary>
    internal int Refreshes { get; private set; }

    protected override void Refresh()
    {
        if (State is null)
        {
            return;
        }

        Refreshes++;
        var ctx = Context;
        _projectedToday = ctx.Today;
        var model = Projector.Day(Store, ctx, State.Anchor);
        KeyedSync.Apply(Spans, model.Spans, r => r.Id, vm => vm.Id, _createRow, static (vm, r) => vm.Update(r));
        KeyedSync.Apply(Items, model.Items, r => r.Id, vm => vm.Id, _createRow, static (vm, r) => vm.Update(r));
        Date = model.Date;
        Progress = model.Progress;
        HasSpans = Spans.Count > 0;
        HasItems = Items.Count > 0;
        IsEmpty = model.Empty;
        Schedule.Update(model.Schedule, model.Date);
    }

    /// <summary>Minute tick: move the now-line only (and only when the shown day is today); a new day re-projects.</summary>
    protected override void OnMinute()
    {
        if (!IsActive || State is null)
        {
            return;
        }

        var clock = Services.App.DayClock;
        if (clock.Today != _projectedToday)
        {
            Refresh();
            return;
        }

        if (State.Anchor == clock.Today)
        {
            Schedule.SetNow(DateMath.Minutes(clock.Now));
        }
    }

    /// <summary>"Add task": New task dated the shown day.</summary>
    [RelayCommand]
    private void AddTask() =>
        Services.Commands.NewTask(State?.Anchor ?? Services.App.DayClock.Today, hasExplicitDate: true);
}
