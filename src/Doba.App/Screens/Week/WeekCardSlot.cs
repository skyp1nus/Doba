using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.ViewModels;

namespace Doba.App.Screens.Week;

/// <summary>
/// One card position in a week cell's list. The cell's card list is bound to slots, not to the tasks, so that moving
/// to another week can point an existing slot — and the <see cref="WeekCardHost"/> and TaskCard behind it — at another
/// task (<see cref="WeekCellViewModel.Update"/>) instead of throwing the card away and building a new one. Within one
/// week a slot keeps its task, and the cell reconciles slots by task id.
/// </summary>
public sealed partial class WeekCardSlot : ObservableObject
{
    public WeekCardSlot(TaskRowViewModel row) => Row = row;

    /// <summary>The task shown in this position.</summary>
    [ObservableProperty] public partial TaskRowViewModel Row { get; set; }

    public long Id => Row.Id;
}
