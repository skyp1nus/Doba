using Doba.Core.Model;
using Doba.Core.Store;

namespace Doba.Core.Editing;

/// <summary>Fields <see cref="TaskPatch.Clear"/> resets: notes to empty, time/tag to none, date to undated (Inbox).</summary>
[Flags]
public enum TaskClear
{
    None = 0,
    Notes = 1,
    Time = 2,
    Tag = 4,
    Date = 8,
}

/// <summary>
/// A change to a task from outside the task dialog (the Claude bridge). Every property is "not supplied" when null;
/// a supplied field replaces the task's value, <see cref="Clear"/> resets fields. Only supplied fields are checked.
/// </summary>
public sealed record TaskPatch
{
    public string? Title { get; init; }

    public string? Notes { get; init; }

    public DateOnly? Date { get; init; }

    /// <summary>Number of days the task spans, 1–<see cref="TaskDialog.MaxDays"/> (1 = single-day).</summary>
    public int? Days { get; init; }

    public TimeOnly? Time { get; init; }

    public int? DurationMinutes { get; init; }

    public Priority? Priority { get; init; }

    /// <summary>An existing tag (compared after <see cref="Tags.Normalize"/>).</summary>
    public string? Tag { get; init; }

    public TaskClear Clear { get; init; }
}

/// <summary>The task an edit produced (not stored yet), or the reason it was refused.</summary>
public readonly record struct EditResult(TaskItem? Task, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// Task rules for edits that do not go through the dialog: validates the supplied fields and returns an error text
/// instead of silently correcting them. The caller stores <see cref="EditResult.Task"/> (Add/Update).
/// </summary>
public static class TaskEdits
{
    public const int MaxDurationMinutes = 1440;

    /// <summary>A new task from <paramref name="patch"/> (title required; no date = Inbox; no priority =
    /// <paramref name="defaultPriority"/>). The returned task's Id is 0; the store assigns one.</summary>
    public static EditResult Create(ITaskReader store, TaskPatch patch, DateOnly today, Priority defaultPriority) =>
        throw new NotImplementedException();

    /// <summary><paramref name="existing"/> with <paramref name="patch"/> applied; fields not supplied stay as they are.</summary>
    public static EditResult Apply(ITaskReader store, TaskItem existing, TaskPatch patch, DateOnly today) =>
        throw new NotImplementedException();

    /// <summary>Moves <paramref name="existing"/> to <paramref name="date"/> (null = Inbox); a multi-day task moves
    /// whole, keeping its length.</summary>
    public static EditResult Move(ITaskReader store, TaskItem existing, DateOnly? date) =>
        throw new NotImplementedException();
}
