using Doba.Core.Model;

namespace Doba.Core.Store;

/// <summary>
/// "Carry over unfinished tasks" (Settings › Tasks): overdue tasks move to Today. The rule is the prototype's
/// <c>moveToToday</c> (design reference lines 1536–1539) applied to every overdue task at once: the date becomes today
/// and everything else (time, duration, priority, tag, notes) is kept. Only open, single-day tasks dated before today
/// move; multi-day spans and undated tasks never do. Idempotent: a second run on the same day finds nothing.
/// </summary>
public static class CarryOver
{
    /// <summary>The tasks that would move: open, single-day, dated before <paramref name="today"/>, in Id order.</summary>
    public static IReadOnlyList<TaskItem> Select(ITaskReader store, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(store);
        var result = new List<TaskItem>();
        foreach (var t in store.All)
        {
            if (IsCarried(t, today))
            {
                result.Add(t);
            }
        }

        return result;
    }

    /// <summary>True when <paramref name="task"/> is open, single-day and dated before <paramref name="today"/>.</summary>
    public static bool IsCarried(TaskItem task, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(task);
        return !task.Done && task.End is null && task.Date is { } date && date < today;
    }

    /// <summary>
    /// Moves every carried task to <paramref name="today"/> in one store mutation (one
    /// <see cref="TaskChangeKind.Batch"/> event, one write per task). Returns how many moved.
    /// </summary>
    public static int Run(ITaskStore store, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(store);
        var carried = Select(store, today);
        if (carried.Count == 0)
        {
            return 0;
        }

        var moved = new TaskItem[carried.Count];
        for (var i = 0; i < moved.Length; i++)
        {
            moved[i] = carried[i] with { Date = today };
        }

        return store.UpdateMany(moved);
    }
}
