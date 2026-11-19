namespace Doba.Core.Model;

/// <summary>Task priority. Clicking the priority icon cycles None → Low → Medium → High → Asap → None.</summary>
public enum Priority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Asap = 4,
}

/// <summary>
/// One task. Immutable; the store replaces the instance on every change.
/// </summary>
/// <param name="Id">Store-assigned identity. Ids grow monotonically, so ordering by Id equals insertion order
/// (the prototype's stable array order) and is the final tie-breaker of every sort.</param>
/// <param name="Title">Non-empty, trimmed.</param>
/// <param name="Notes">Free text, may be empty.</param>
/// <param name="Date">Start day; null = undated (lives in Inbox).</param>
/// <param name="End">Inclusive last day of a multi-day span; null for single-day and undated tasks. Always &gt; Date when set.</param>
/// <param name="Time">Start time; only for single-day dated tasks.</param>
/// <param name="DurationMinutes">Length of a timed task in minutes. 0 = unspecified (schedule treats it as 30).</param>
/// <param name="Priority">Priority.</param>
/// <param name="Tag">At most one tag (tag name without '#'), or null.</param>
/// <param name="Done">Completed.</param>
public sealed record TaskItem(
    long Id,
    string Title,
    string Notes,
    DateOnly? Date,
    DateOnly? End,
    TimeOnly? Time,
    int DurationMinutes,
    Priority Priority,
    string? Tag,
    bool Done)
{
    /// <summary>True for a task spanning more than one day.</summary>
    public bool IsMultiDay => End is not null;

    /// <summary>True for a task without a date (Inbox).</summary>
    public bool IsUndated => Date is null;

    /// <summary>The last day the task occupies: End for spans, otherwise Date.</summary>
    public DateOnly? LastDay => End ?? Date;
}
