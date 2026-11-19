using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Controls;
using Doba.App.Services;
using Doba.App.ViewModels;
using Doba.Core.Projections;
using Doba.Core.Time;

namespace Doba.App.Screens.Today;

/// <summary>
/// Today: summary + progress, "Add task", groups (Overdue with "→ Today", In progress, Scheduled, Anytime, Done) and the
/// schedule (blocks + now-line). Everything comes from <see cref="Projector.Today"/>; rows and blocks are reconciled in
/// place. The minute tick only moves the now-line.
/// </summary>
public sealed partial class TodayViewModel : ScreenViewModel
{
    private readonly EntranceExpiry _entranceExpiry;
    private DateOnly _projectedDay;
    private bool _projected;

    public TodayViewModel(ScreenServices services)
        : base(services)
    {
        Schedule = new ScheduleViewModel(services.Actions, services.Commands);
        _entranceExpiry = new EntranceExpiry(services.App.Store, services.Actions);
    }

    public ObservableCollection<TaskGroupViewModel> Groups { get; } = [];

    /// <summary>"6 tasks · 4 done · 2 left · 3 overdue".</summary>
    [ObservableProperty] public partial string Summary { get; private set; } = string.Empty;

    /// <summary>0..1 done share of today's single-day tasks, in whole percents (from the projection).</summary>
    [ObservableProperty] public partial double Progress { get; private set; }

    public ScheduleViewModel Schedule { get; }

    /// <summary>Task actions whose pending entrance the rows consume (set on the view root).</summary>
    public TaskActions Entrances => Services.Actions;

    /// <summary>Number of projections run (tests).</summary>
    internal int Refreshes { get; private set; }

    protected override void Refresh()
    {
        Refreshes++;
        if (!_projected)
        {
            // First projection: no row here is new, whatever another screen left pending.
            _projected = true;
            _entranceExpiry.ExpireNow();
        }

        var ctx = Context;
        _projectedDay = ctx.Today;
        var model = Projector.Today(Store, ctx);
        TaskGroupSync.Apply([(Groups, model.Groups)], Services.Actions, Services.Commands);
        Summary = model.Summary;
        Progress = model.Progress;
        Schedule.Update(model.Schedule);
    }

    /// <summary>Minute tick: move the now-line only; a new day re-projects everything.</summary>
    protected override void OnMinute()
    {
        if (!IsActive)
        {
            return;
        }

        var clock = Services.App.DayClock;
        if (clock.Today != _projectedDay)
        {
            Refresh();
            return;
        }

        Schedule.SetNow(DateMath.Minutes(clock.Now));
    }

    public override void Dispose()
    {
        _entranceExpiry.Dispose();
        base.Dispose();
    }

    /// <summary>"Add task" row: New task dated today.</summary>
    [RelayCommand]
    private void AddTask() => Services.Commands.NewTask(Services.App.DayClock.Today, hasExplicitDate: true);
}
