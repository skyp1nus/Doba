using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.Core.Model;
using Doba.Core.Projections;

namespace Doba.App.ViewModels;

/// <summary>
/// A task as every list, card, bar and block binds to it. Long-lived: <see cref="KeyedSync"/> pushes new
/// <see cref="TaskRow"/> records into it with <see cref="Update"/>, so completing a task changes properties (and plays
/// the check/strike/dim transitions) instead of recreating the visual.
/// </summary>
public sealed partial class TaskRowViewModel : ObservableObject
{
    private readonly TaskActions _actions;
    private readonly IAppCommands _commands;

    public TaskRowViewModel(TaskRow row, TaskActions actions, IAppCommands commands)
    {
        _actions = actions;
        _commands = commands;
        Id = row.Id;
        Update(row);
    }

    public long Id { get; }

    [ObservableProperty] public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty] public partial bool Done { get; private set; }

    [ObservableProperty] public partial Priority Priority { get; private set; }

    [ObservableProperty] public partial string TagLabel { get; private set; } = string.Empty;

    [ObservableProperty] public partial string Time { get; private set; } = string.Empty;

    [ObservableProperty] public partial string Meta { get; private set; } = string.Empty;

    [ObservableProperty] public partial bool IsMultiDay { get; private set; }

    [ObservableProperty] public partial bool Dimmed { get; private set; }

    [ObservableProperty] public partial bool CanMoveToToday { get; private set; }

    public bool HasTag => TagLabel.Length > 0;

    public bool HasTime => Time.Length > 0;

    public bool HasMeta => Meta.Length > 0;

    public bool HasPriority => Priority != Priority.None;

    /// <summary>"Mark done: …" / "Mark not done: …" (checkbox accessible name).</summary>
    public string ToggleLabel => (Done ? "Mark not done: " : "Mark done: ") + Title;

    /// <summary>"Priority: High — change for …".</summary>
    public string PriorityLabel => "Priority: " + (Priority == Priority.None ? "none" : Priority.DisplayName()) + " — change for " + Title;

    /// <summary>Tooltip: "High priority" / "No priority".</summary>
    public string PriorityTitle => Priority == Priority.None ? "No priority" : Priority.DisplayName() + " priority";

    public string MoveLabel => "Move to today: " + Title;

    /// <summary>The record last pushed in.</summary>
    public TaskRow Row { get; private set; } = null!;

    public void Update(TaskRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (ReferenceEquals(Row, row) || Equals(Row, row))
        {
            return;
        }

        Row = row;
        Title = row.Title;
        Done = row.Done;
        Priority = row.Priority;
        TagLabel = row.TagLabel;
        Time = row.Time;
        Meta = row.Meta;
        IsMultiDay = row.IsMultiDay;
        Dimmed = row.Dimmed;
        CanMoveToToday = row.CanMoveToToday;
        OnPropertyChanged(nameof(HasTag));
        OnPropertyChanged(nameof(HasTime));
        OnPropertyChanged(nameof(HasMeta));
        OnPropertyChanged(nameof(HasPriority));
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(PriorityLabel));
        OnPropertyChanged(nameof(PriorityTitle));
        OnPropertyChanged(nameof(MoveLabel));
    }

    [RelayCommand] private void Toggle() => _actions.ToggleDone(Id);

    [RelayCommand] private void CyclePriority() => _actions.CyclePriority(Id);

    [RelayCommand] private void MoveToToday() => _actions.MoveToToday(Id);

    [RelayCommand] private void Open() => _commands.EditTask(Id);
}
