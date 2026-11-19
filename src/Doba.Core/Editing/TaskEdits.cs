using Doba.Core.Model;
using Doba.Core.Store;
using Doba.Core.Time;

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
/// <remarks>
/// <para>Only supplied fields are checked, so a stored task the dialog could not produce (an imported span longer than
/// <see cref="TaskDialog.MaxDays"/> days) can still get other changes. A field that is not supplied keeps its stored
/// value as it is (<see cref="TaskItem.DurationMinutes"/> included: 0 stays 0, unlike the dialog's
/// <see cref="TaskDialog.EditDraft"/>).</para>
/// <para>Error texts are short English sentences that name the argument as the Claude bridge calls it, so the model can
/// fix its call.</para>
/// </remarks>
public static class TaskEdits
{
    public const int MaxDurationMinutes = 1440;

    /// <summary>A new task from <paramref name="patch"/> (title required; no date = Inbox; no priority =
    /// <paramref name="defaultPriority"/>). The returned task's Id is 0; the store assigns one.</summary>
    /// <remarks>A duration that is not supplied is 0 ("unspecified"), not the dialog's 60.</remarks>
    public static EditResult Create(ITaskReader store, TaskPatch patch, DateOnly today, Priority defaultPriority)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(patch);
        if (patch.Clear != TaskClear.None)
        {
            return Fail("clear is only for changing an existing task.");
        }

        if (patch.Title is null)
        {
            return Fail("title is required.");
        }

        var blank = new TaskItem(0, string.Empty, string.Empty, null, null, null, 0, defaultPriority, null, false);
        return Build(store, blank, patch, creating: true);
    }

    /// <summary><paramref name="existing"/> with <paramref name="patch"/> applied; fields not supplied stay as they are.</summary>
    /// <remarks>
    /// A new date without days moves a multi-day task whole (its length is kept). Clearing the date moves the task to
    /// the Inbox, which also drops its time and span (as moving it there does). A time on a task that is (or becomes)
    /// multi-day or undated is an error, also when it is the stored time: the patch has to clear it
    /// (<see cref="TaskClear.Time"/>).
    /// </remarks>
    public static EditResult Apply(ITaskReader store, TaskItem existing, TaskPatch patch, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(patch);
        return Build(store, existing, patch, creating: false);
    }

    /// <summary>Moves <paramref name="existing"/> to <paramref name="date"/> (null = Inbox); a multi-day task moves
    /// whole, keeping its length.</summary>
    /// <remarks>Moving to the Inbox drops the time and the span (an undated task has neither); a single-day task keeps
    /// its time on the new day.</remarks>
    public static EditResult Move(ITaskReader store, TaskItem existing, DateOnly? date)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(existing);
        if (date is not { } start)
        {
            return new EditResult(existing with { Date = null, End = null, Time = null }, null);
        }

        if (existing is { Date: { } from, End: { } end })
        {
            var length = DateMath.DaysBetween(from, end);
            if (start.DayNumber > DateOnly.MaxValue.DayNumber - length)
            {
                return Fail("date is too far in the future.");
            }

            return new EditResult(existing with { Date = start, End = start.AddDays(length) }, null);
        }

        return new EditResult(existing with { Date = start, End = null }, null);
    }

    private static EditResult Build(ITaskReader store, TaskItem existing, TaskPatch patch, bool creating)
    {
        var clear = patch.Clear;
        if ((clear & ~(TaskClear.Notes | TaskClear.Time | TaskClear.Tag | TaskClear.Date)) != 0)
        {
            return Fail("clear accepts only notes, time, tag and date.");
        }

        if (Contradiction(patch) is { } both)
        {
            return Fail(both + " is both set and cleared; leave out one of them.");
        }

        // Title: trimmed like the dialog's ToTask.
        var title = existing.Title;
        if (patch.Title is { } rawTitle)
        {
            title = rawTitle.Trim();
            if (title.Length == 0)
            {
                return Fail("title must not be empty.");
            }
        }

        var notes = (clear & TaskClear.Notes) != 0 ? string.Empty : patch.Notes ?? existing.Notes ?? string.Empty;

        // Days: checked only when supplied; otherwise the stored length is kept (a long imported span stays valid).
        if (patch.Days is { } suppliedDays && (suppliedDays < 1 || suppliedDays > TaskDialog.MaxDays))
        {
            return Fail($"days must be between 1 and {TaskDialog.MaxDays}.");
        }

        var storedDays = existing is { Date: { } d0, End: { } e0 } ? DateMath.DaysBetween(d0, e0) + 1 : 1;
        var days = patch.Days ?? storedDays;

        // Date: clearing it moves the task to the Inbox, which has no span and no time.
        var toInbox = (clear & TaskClear.Date) != 0;
        var date = toInbox ? null : patch.Date ?? existing.Date;
        if (date is null)
        {
            if (patch.Days > 1)
            {
                return Fail(toInbox
                    ? "days needs a date, and clear 'date' moves the task to the Inbox; leave out one of them."
                    : "days needs a date: give a date (YYYY-MM-DD) too, or leave days out for an Inbox task.");
            }

            days = 1;
        }

        DateOnly? end = null;
        if (date is { } start && days > 1)
        {
            if (start.DayNumber > DateOnly.MaxValue.DayNumber - (days - 1))
            {
                return Fail("date is too far in the future.");
            }

            end = start.AddDays(days - 1);
        }

        // Time: only for a dated single-day task. A supplied time on anything else is an error, and so is a stored time
        // on a task this patch makes multi-day: the caller clears it, nothing is dropped silently.
        TimeOnly? time = (clear & TaskClear.Time) != 0 ? null : patch.Time ?? existing.Time;
        if (toInbox && patch.Time is null)
        {
            time = null; // moving to the Inbox drops the stored time, as move_task to "inbox" does
        }

        if (time is { } t)
        {
            if (date is null)
            {
                return Fail(toInbox
                    ? "time needs a date, and clear 'date' moves the task to the Inbox; leave out one of them."
                    : "time needs a date: give a date (YYYY-MM-DD) too, or leave time out for an Inbox task.");
            }

            if (end is not null)
            {
                return Fail(patch.Time is not null
                    ? "time is only for a single-day task; leave time out or set days to 1."
                    : $"The task has a time ({DateMath.Hhmm(t)}) and a multi-day task cannot have one: add clear: [\"time\"] or keep days at 1.");
            }
        }

        var duration = existing.DurationMinutes;
        if (patch.DurationMinutes is { } minutes)
        {
            if (minutes < 0 || minutes > MaxDurationMinutes)
            {
                return Fail($"duration_minutes must be between 0 and {MaxDurationMinutes}.");
            }

            duration = minutes;
        }

        var priority = existing.Priority;
        if (patch.Priority is { } p)
        {
            if ((uint)p > (uint)Priority.Asap)
            {
                return Fail("priority must be one of none, low, medium, high, asap.");
            }

            priority = p;
        }

        // Tag: only an existing one, compared after normalising.
        var tag = (clear & TaskClear.Tag) != 0 ? null : existing.Tag;
        if (patch.Tag is { } rawTag)
        {
            var name = Tags.Normalize(rawTag);
            if (name is null)
            {
                return Fail("tag must not be empty; to remove the tag use clear: [\"tag\"].");
            }

            if (!Contains(store.Tags, name))
            {
                return Fail(UnknownTag(rawTag, store.Tags));
            }

            tag = name;
        }

        var task = new TaskItem(creating ? 0 : existing.Id, title, notes, date, end, time, duration, priority, tag,
            !creating && existing.Done);
        return new EditResult(task, null);
    }

    /// <summary>The first field that is both supplied and cleared, or null.</summary>
    private static string? Contradiction(TaskPatch patch)
    {
        var clear = patch.Clear;
        if (patch.Notes is not null && (clear & TaskClear.Notes) != 0)
        {
            return "notes";
        }

        if (patch.Time is not null && (clear & TaskClear.Time) != 0)
        {
            return "time";
        }

        if (patch.Tag is not null && (clear & TaskClear.Tag) != 0)
        {
            return "tag";
        }

        return patch.Date is not null && (clear & TaskClear.Date) != 0 ? "date" : null;
    }

    private static bool Contains(IReadOnlyList<string> tags, string name)
    {
        foreach (var t in tags)
        {
            if (string.Equals(t, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string UnknownTag(string raw, IReadOnlyList<string> tags) =>
        tags.Count == 0
            ? $"Unknown tag '{raw.Trim()}': there are no tags yet. Tags are created by the user in Doba; leave tag out."
            : $"Unknown tag '{raw.Trim()}'. Existing tags: {string.Join(", ", tags)}. New tags are created by the user in Doba.";

    private static EditResult Fail(string error) => new(null, error);
}
