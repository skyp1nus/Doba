using System.Collections.ObjectModel;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.App.ViewModels;
using Doba.Core.Projections;

namespace Doba.App.Controls;

/// <summary>
/// Reconciles one or more bound group collections (Inbox: left + right column; Today: one list) with a fresh projection.
/// Groups are matched by <see cref="TaskGroup.Key"/> and rows by task id through <see cref="KeyedSync"/>, like
/// <see cref="TaskGroupViewModel.Update"/> — and, in addition, a row that changes group (an Inbox task completed moves
/// from "ASAP" to "Done", a Today task from "Anytime" to "Done", "→ Today" moves it out of "Overdue") keeps its
/// <see cref="TaskRowViewModel"/> instance: it is moved into the new group before that group is updated.
/// When nothing changed shape (same groups, same rows, same order) the rows are just updated in place, with no
/// allocation proportional to the store.
/// </summary>
public static class TaskGroupSync
{
    private static readonly TaskRow[] s_noRows = [];

    /// <summary>Applies each (bound collection, projected groups) pair. Task ids must be unique across all pairs.</summary>
    public static void Apply(
        IReadOnlyList<(ObservableCollection<TaskGroupViewModel> Target, IReadOnlyList<TaskGroup> Source)> lists,
        TaskActions actions,
        IAppCommands commands)
    {
        ArgumentNullException.ThrowIfNull(lists);

        // 0. The common refresh (tag filter, settings, priority/title edits that keep the order): the same groups with
        //    the same rows in the same order. Update in place without building any lookup table.
        if (IsSameShape(lists))
        {
            foreach (var (target, source) in lists)
            {
                for (var i = 0; i < source.Count; i++)
                {
                    target[i].Update(source[i]);
                }
            }

            return;
        }

        // 1. Where every existing row view-model lives now.
        var owner = new Dictionary<long, (TaskGroupViewModel Group, TaskRowViewModel Row)>();
        foreach (var (target, _) in lists)
        {
            foreach (var group in target)
            {
                foreach (var row in group.Items)
                {
                    owner[row.Id] = (group, row);
                }
            }
        }

        // 2. Groups by key (new ones start empty so step 3 can move existing rows into them).
        foreach (var (target, source) in lists)
        {
            KeyedSync.Apply(target, source, g => g.Key, vm => vm.Key,
                g => new TaskGroupViewModel(g with { Items = s_noRows }, actions, commands),
                static (_, _) => { });
        }

        // 3. Move row view-models that changed group, then let each group reconcile its rows (update / create / order).
        foreach (var (target, source) in lists)
        {
            for (var i = 0; i < source.Count; i++)
            {
                var group = target[i];
                foreach (var row in source[i].Items)
                {
                    if (owner.TryGetValue(row.Id, out var from) && !ReferenceEquals(from.Group, group))
                    {
                        // The old group may already have dropped it (it was reconciled earlier in this pass).
                        from.Group.Items.Remove(from.Row);
                        group.Items.Add(from.Row);
                        owner[row.Id] = (group, from.Row);
                    }
                }

                group.Update(source[i]);
            }
        }
    }

    /// <summary>Every bound collection has the projected groups (by key, in order), each with the same row ids in order.</summary>
    private static bool IsSameShape(
        IReadOnlyList<(ObservableCollection<TaskGroupViewModel> Target, IReadOnlyList<TaskGroup> Source)> lists)
    {
        foreach (var (target, source) in lists)
        {
            if (target.Count != source.Count)
            {
                return false;
            }

            for (var i = 0; i < source.Count; i++)
            {
                if (!string.Equals(target[i].Key, source[i].Key, StringComparison.Ordinal) || !target[i].HasSameRows(source[i].Items))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
