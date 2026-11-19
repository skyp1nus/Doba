using Doba.Core.Model;

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
