using Doba.Core.Model;
using Doba.Core.Store;

namespace Doba.Core.Projections;

/// <summary>Plain task lists for the Claude bridge (no view models, no layout).</summary>
public static class TaskQueries
{
    /// <summary>Every task occupying a day in [<paramref name="from"/>, <paramref name="to"/>] (inclusive), each once.</summary>
    public static IReadOnlyList<TaskItem> InRange(ITaskReader store, DateOnly from, DateOnly to, bool includeDone) =>
        throw new NotImplementedException();

    /// <summary>Undated tasks in Inbox order.</summary>
    public static IReadOnlyList<TaskItem> Inbox(ITaskReader store, bool includeDone) =>
        throw new NotImplementedException();
}
