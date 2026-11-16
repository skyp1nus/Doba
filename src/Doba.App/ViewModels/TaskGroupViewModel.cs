using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.Core.Projections;

namespace Doba.App.ViewModels;

/// <summary>A labelled section of rows (Inbox, Today). Rows are reconciled by task id.</summary>
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

    /// <summary>Rows created by the last update (for the new-row entrance).</summary>
    public int Created { get; private set; }

    public void Update(TaskGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Label = group.Label;
        Count = group.Count;
        Tone = group.Tone;
        Link = group.Link;
        OnPropertyChanged(nameof(HasLink));
        Created = KeyedSync.Apply(Items, group.Items, r => r.Id, vm => vm.Id, r => new TaskRowViewModel(r, _actions, _commands), (vm, r) => vm.Update(r));
    }

    /// <summary>Group label click (zoom to the linked day / month).</summary>
    [RelayCommand] private void Follow() { if (Link is { } l) _commands.ZoomTo(l.View, l.Anchor); }
}
