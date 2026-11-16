using Doba.Core.Model;
using Doba.Core.Navigation;

namespace Doba.Core.Editing;

/// <summary>Which popover of the task dialog is open.</summary>
public enum DraftPopover { None, Date, Time, Priority, Tag }

/// <summary>
/// State of the New / Edit task dialog (the prototype's `modal`). Immutable; every UI action produces a new draft.
/// </summary>
/// <param name="EditId">Task being edited, or null for a new task.</param>
/// <param name="Days">1..14 ("Lasts N days"); &gt;1 makes a multi-day task and hides the Time pill.</param>
/// <param name="Timed">"Set time" vs "Any time".</param>
/// <param name="Time">Wheel value, minutes snapped to 5.</param>
/// <param name="DurationMinutes">15, 30, 60 or 120 (default 60).</param>
/// <param name="CalendarMonth">First day of the month shown in the mini calendar.</param>
public sealed record TaskDraft(
    long? EditId,
    string Title,
    string Notes,
    DateOnly? Date,
    int Days,
    bool Timed,
    TimeOnly Time,
    int DurationMinutes,
    Priority Priority,
    string? Tag,
    DraftPopover Open,
    DateOnly CalendarMonth)
{
    /// <summary>Create is enabled when the trimmed title is non-empty.</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Title);

    /// <summary>Time pill is visible only for a dated single-day task.</summary>
    public bool ShowTime => Date is not null && Days == 1;

    public DateOnly? EndDate => Date is { } d && Days > 1 ? d.AddDays(Days - 1) : null;
}

/// <summary>One cell of the 6×7 mini calendar in the Date popover.</summary>
/// <param name="InRange">Inside a multi-day selection (accent band 15%).</param>
/// <param name="BandStart">Band has a rounded left edge here (selection start or first column).</param>
/// <param name="BandEnd">Band has a rounded right edge here (selection end or last column).</param>
/// <param name="Marked">Start or end of the selection (filled accent square).</param>
public sealed record MiniCalendarDay(DateOnly Date, int DayNumber, bool InMonth, bool IsToday, bool IsPast, bool Selected, bool Marked, bool InRange, bool BandStart, bool BandEnd);

/// <summary>Dialog rules ported from the prototype (openModal, openEdit, createTask, pill labels, wheel).</summary>
public static class TaskDialog
{
    /// <summary>New task: undated in Inbox, the anchor in Day, otherwise today; priority = settings default; time 10:00, 60 min.</summary>
    public static TaskDraft NewDraft(AppView view, DateOnly anchor, DateOnly today, Priority defaultPriority) => throw new NotImplementedException();

    /// <summary>New task with an explicit date (the "Add task" rows: Inbox → null, Today → today, Day → anchor).</summary>
    public static TaskDraft NewDraft(DateOnly? date, DateOnly today, Priority defaultPriority) => throw new NotImplementedException();

    public static TaskDraft EditDraft(TaskItem task, DateOnly today) => throw new NotImplementedException();

    /// <summary>
    /// The task to store: End = Date + Days − 1 when Days &gt; 1; Time only when dated, single-day and Timed;
    /// Done is kept from <paramref name="existing"/> when editing.
    /// </summary>
    public static TaskItem ToTask(TaskDraft d, TaskItem? existing) => throw new NotImplementedException();

    /// <summary>"Today", "Tomorrow", "Mon 16 Nov", with " → 18 Nov" for spans; "Date" when undated.</summary>
    public static string DatePillLabel(TaskDraft d, DateOnly today) => throw new NotImplementedException();

    /// <summary>"14:00 · 1h" or "Time".</summary>
    public static string TimePillLabel(TaskDraft d) => throw new NotImplementedException();

    /// <summary>"High" or "Priority".</summary>
    public static string PriorityPillLabel(TaskDraft d) => throw new NotImplementedException();

    /// <summary>Tag name or "Tag".</summary>
    public static string TagPillLabel(TaskDraft d) => throw new NotImplementedException();

    /// <summary>Header "where": same as the date pill, or "Inbox".</summary>
    public static string WhereLabel(TaskDraft d, DateOnly today) => throw new NotImplementedException();

    /// <summary>"until 18 Nov" when Days &gt; 1, else empty.</summary>
    public static string UntilLabel(TaskDraft d) => throw new NotImplementedException();

    /// <summary>"1 day" / "3 days".</summary>
    public static string DaysLabel(TaskDraft d) => throw new NotImplementedException();

    /// <summary>"14:00 – 15:00".</summary>
    public static string RangeLabel(TaskDraft d) => throw new NotImplementedException();

    /// <summary>"Next week" quick option: the first day of next week.</summary>
    public static DateOnly NextWeek(DateOnly today, DayOfWeek weekStart) => throw new NotImplementedException();

    /// <summary>42 cells starting at the week start on/before the 1st of <see cref="TaskDraft.CalendarMonth"/>.</summary>
    public static IReadOnlyList<MiniCalendarDay> MiniCalendar(TaskDraft d, DateOnly today, DayOfWeek weekStart) => throw new NotImplementedException();

    /// <summary>Wheel: hour index 0..23 and minute index 0..11 for the draft's time.</summary>
    public static (int HourIndex, int MinuteIndex) WheelIndices(TimeOnly time) => throw new NotImplementedException();

    /// <summary>Wheel commit after 120 ms of stillness: sets the hour or minute (index·5) and Timed = true.</summary>
    public static TaskDraft CommitWheel(TaskDraft d, bool hourColumn, int index) => throw new NotImplementedException();
}
