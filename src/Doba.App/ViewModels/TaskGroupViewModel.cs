using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.Core.Projections;

namespace Doba.App.ViewModels;

/// <summary>A labelled section of rows (Inbox, Today). Rows are reconciled by task id. A click on the label link
/// (<see cref="Link"/>) is handled by the view (<c>TaskGroupList.FollowRequested</c>), which knows the click origin.</summary>
public sealed partial class TaskGroupViewModel : ObservableObject
{
    private readonly TaskActions _actions;
    private readonly IAppCommands _commands;

    public TaskGroupViewModel(TaskGroup group, TaskActions actions, IAppCommands commands)
    {
        _actions = actions;
        _commands = commands;
        Key = group.Key;
        Update(group);
    }

    public string Key { get; }

    [ObservableProperty] public partial string Label { get; private set; } = string.Empty;

    [ObservableProperty] public partial string Count { get; private set; } = string.Empty;

    [ObservableProperty] public partial GroupTone Tone { get; private set; }

    [ObservableProperty] public partial NavTarget? Link { get; private set; }

    public bool HasLink => Link is not null;

    public ObservableCollection<TaskRowViewModel> Items { get; } = [];

    public void Update(TaskGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Label = group.Label;
        Count = group.Count;
        Tone = group.Tone;
        Link = group.Link;
        OnPropertyChanged(nameof(HasLink));
        if (HasSameRows(group.Items))
        {
            // The common refresh (tag filter, settings, a title edit): same rows in the same order — update in place
            // without KeyedSync's per-call lookup tables.
            for (var i = 0; i < Items.Count; i++)
            {
                Items[i].Update(group.Items[i]);
            }

            return;
        }

        KeyedSync.Apply(Items, group.Items, r => r.Id, vm => vm.Id, r => new TaskRowViewModel(r, _actions, _commands), (vm, r) => vm.Update(r));
    }

    /// <summary>True when <see cref="Items"/> holds exactly <paramref name="rows"/>' tasks in the same order.</summary>
    public bool HasSameRows(IReadOnlyList<TaskRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count != Items.Count)
        {
            return false;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id != Items[i].Id)
            {
                return false;
            }
        }

        return true;
    }
}
