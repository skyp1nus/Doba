using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Controls;
using Doba.App.Services;
using Doba.App.ViewModels;
using Doba.Core.Projections;

namespace Doba.App.Screens.Inbox;

/// <summary>
/// Inbox: left column = undated tasks by priority (ASAP, High, Medium, Low, No priority) + Done; right column =
/// "Scheduled" (Overdue, Today, Tomorrow, the next 14 days, Later). Everything comes from <see cref="Projector.Inbox"/>;
/// rows are reconciled in place (<see cref="TaskGroupSync"/>).
/// </summary>
public sealed partial class InboxViewModel(ScreenServices services) : ScreenViewModel(services)
{
    private DateOnly _projectedDay;

    public ObservableCollection<TaskGroupViewModel> Left { get; } = [];

    public ObservableCollection<TaskGroupViewModel> Right { get; } = [];

    /// <summary>"51 open".</summary>
    [ObservableProperty] public partial string ScheduledCount { get; private set; } = string.Empty;

    /// <summary>Task actions whose pending entrance the rows consume (set on the view root).</summary>
    public TaskActions Entrances => Services.Actions;

    /// <summary>Number of projections run (tests).</summary>
    internal int Refreshes { get; private set; }

    /// <summary>A group label link was clicked: zoom to its day (or Later → month) from the click origin
    /// (relative to the content area, 0..1).</summary>
    public void Follow(TaskGroupViewModel group, Point? origin)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Link is { } link)
        {
            Services.Commands.ZoomTo(link.View, link.Anchor, origin);
        }
    }

    protected override void Refresh()
    {
        Refreshes++;
        var ctx = Context;
        _projectedDay = ctx.Today;
        var model = Projector.Inbox(Store, ctx);
        TaskGroupSync.Apply([(Left, model.Left), (Right, model.Right)], Services.Actions, Services.Commands);
        ScheduledCount = model.ScheduledCount;
    }

    /// <summary>The Inbox does not show the time: re-project only when the day changed.</summary>
    protected override void OnMinute()
    {
        if (Services.App.DayClock.Today != _projectedDay)
        {
            RefreshIfActive();
        }
    }

    /// <summary>"Add task" row: New task without a date.</summary>
    [RelayCommand]
    private void AddTask() => Services.Commands.NewTask(null, hasExplicitDate: true);
}
