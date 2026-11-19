using Avalonia.Controls;
using Doba.App.Controls;
using Doba.App.Motion;
using Doba.App.ViewModels;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Screens.Day;

/// <summary>
/// The new-row entrance for Day cards and spans (motion spec: opacity 0→1, translateY −8→0, 360 ms ease-out, only for
/// the task just created). Uses the same pending-entrance source as list rows: the inherited
/// <see cref="TaskListRow.EntrancesProperty"/> the screen sets on its root.
/// </summary>
internal static class DayEntrance
{
    public static void TryPlay(Control row)
    {
        if (row.DataContext is TaskRowViewModel vm && TaskListRow.GetEntrances(row) is { } actions && actions.ConsumeEntrance(vm.Id))
        {
            Animator.SlideFrom(row, 0, M.NewRowY, M.NewRow);
        }
    }
}
