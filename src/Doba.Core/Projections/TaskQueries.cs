using Doba.Core.Model;
using Doba.Core.Store;

namespace Doba.Core.Projections;

/// <summary>Plain task lists for the Claude bridge (no view models, no layout).</summary>
public static class TaskQueries
{
    /// <summary>Range order: by (start) date, then the day lists' <see cref="Projector.ByTime"/> (timed first by time,
    /// then priority, then Id).</summary>
    private static readonly Comparison<TaskItem> s_byDateThenTime = (x, y) =>
    {
        var c = x.Date!.Value.CompareTo(y.Date!.Value);
        return c != 0 ? c : Projector.ByTime(x, y);
    };

    /// <summary>Every task occupying a day in [<paramref name="from"/>, <paramref name="to"/>] (inclusive), each once.</summary>
    /// <remarks>Single-day tasks of each day plus the multi-day tasks overlapping the range (a span that started
    /// before <paramref name="from"/> sorts under its own start date, so it comes first). Sorted by date, then as a day
    /// list (<see cref="Projector.ByTime"/>). Undated tasks are never included. Empty when <paramref name="to"/> is
    /// before <paramref name="from"/>.</remarks>
    public static IReadOnlyList<TaskItem> InRange(ITaskReader store, DateOnly from, DateOnly to, bool includeDone)
    {
        ArgumentNullException.ThrowIfNull(store);
        var result = new List<TaskItem>();
        if (to < from)
        {
            return result;
        }

        foreach (var t in store.SpansOverlapping(from, to))
        {
            if (includeDone || !t.Done)
            {
                result.Add(t);
            }
        }

        for (var day = from; ; day = day.AddDays(1))
        {
            foreach (var t in store.OnDate(day))
            {
                if (includeDone || !t.Done)
                {
                    result.Add(t);
                }
            }

            if (day == to)
            {
                break;
            }
        }

        result.Sort(s_byDateThenTime);
        return result;
    }

    /// <summary>Undated tasks in Inbox order.</summary>
    /// <remarks>The Inbox screen's left column (<see cref="Projector.Inbox"/>): open tasks grouped by priority, ASAP
    /// first and None last, each group in Id order; then, when <paramref name="includeDone"/>, the completed ones in Id
    /// order.</remarks>
    public static IReadOnlyList<TaskItem> Inbox(ITaskReader store, bool includeDone)
    {
        ArgumentNullException.ThrowIfNull(store);
        var undated = store.Undated;
        var result = new List<TaskItem>(undated.Count);
        ReadOnlySpan<Priority> order = [Priority.Asap, Priority.High, Priority.Medium, Priority.Low, Priority.None];
        foreach (var p in order)
        {
            foreach (var t in undated)
            {
                if (!t.Done && t.Priority == p)
                {
                    result.Add(t);
                }
            }
        }

        if (includeDone)
        {
            foreach (var t in undated)
            {
                if (t.Done)
                {
                    result.Add(t);
                }
            }
        }

        return result;
    }
}
