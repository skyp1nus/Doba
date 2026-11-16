using System.Globalization;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Time;

namespace Doba.Core.Editing;

/// <summary>A quick option of the Date popover ("Today", "Tomorrow", "Next week" with hint "23 Nov", "No date").</summary>
/// <param name="Date">The date it picks; null for "No date".</param>
public sealed record DateQuickOption(string Label, string Hint, DateOnly? Date, bool Selected);

/// <summary>Dialog rules ported from the prototype (openModal, openEdit, createTask, pill labels, wheel).</summary>
public static class TaskDialog
{
    /// <summary>Default wheel time of a new task (10:00).</summary>
    public static TimeOnly DefaultTime { get; } = new(10, 0);

    /// <summary>Default duration of the dialog (60 min).</summary>
    public const int DefaultDuration = 60;

    /// <summary>Longest span the "Lasts N days" stepper allows.</summary>
    public const int MaxDays = 14;

    /// <summary>The duration chips of the Time popover.</summary>
    public static IReadOnlyList<int> DurationOptions { get; } = [15, 30, 60, 120];

    /// <summary>New task: undated in Inbox, the anchor in Day, otherwise today; priority = settings default; time 10:00, 60 min.</summary>
    public static TaskDraft NewDraft(AppView view, DateOnly anchor, DateOnly today, Priority defaultPriority)
    {
        DateOnly? date = view switch
        {
            AppView.Inbox => null,
            AppView.Day => anchor,
            _ => today,
        };
        return NewDraft(date, today, defaultPriority);
    }

    /// <summary>New task with an explicit date (the "Add task" rows: Inbox → null, Today → today, Day → anchor).</summary>
    public static TaskDraft NewDraft(DateOnly? date, DateOnly today, Priority defaultPriority) =>
        new(null, string.Empty, string.Empty, date, 1, false, DefaultTime, DefaultDuration, defaultPriority, null, DraftPopover.None,
            DateMath.FirstOfMonth(date ?? today));

    /// <summary>Edit an existing task (the prototype's openEdit): untimed tasks show 10:00, unspecified durations 60.</summary>
    public static TaskDraft EditDraft(TaskItem task, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(task);
        var days = task is { Date: { } d, End: { } e } ? DateMath.DaysBetween(d, e) + 1 : 1;
        return new TaskDraft(task.Id, task.Title, task.Notes ?? string.Empty, task.Date, days, task.Time is not null,
            task.Time ?? DefaultTime, task.DurationMinutes > 0 ? task.DurationMinutes : DefaultDuration, task.Priority, task.Tag,
            DraftPopover.None, DateMath.FirstOfMonth(task.Date ?? today));
    }

    /// <summary>
    /// The task to store: End = Date + Days − 1 when Days &gt; 1; Time only when dated, single-day and Timed;
    /// Done is kept from <paramref name="existing"/> when editing. Id is the existing/edited Id, or 0 for a new task
    /// (the store assigns one).
    /// </summary>
    /// <exception cref="ArgumentException">The draft has no title (<see cref="TaskDraft.CanSave"/> is false).</exception>
    public static TaskItem ToTask(TaskDraft d, TaskItem? existing)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (!d.CanSave)
        {
            throw new ArgumentException("A task needs a title.", nameof(d));
        }

        var single = d.Date is not null && d.Days == 1;
        return new TaskItem(
            existing?.Id ?? d.EditId ?? 0,
            d.Title.Trim(),
            d.Notes ?? string.Empty,
            d.Date,
            d.EndDate,
            single && d.Timed ? d.Time : null,
            d.DurationMinutes,
            d.Priority,
            d.Tag,
            existing?.Done ?? false);
    }

    /// <summary>"Today", "Tomorrow", "Mon 16 Nov", with " → 18 Nov" for spans; "Date" when undated.</summary>
    public static string DatePillLabel(TaskDraft d, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (d.Date is not { } date)
        {
            return "Date";
        }

        var name = date == today ? "Today" : (date == today.AddDays(1) ? "Tomorrow" : DateMath.ShortWithWeekday(date));
        return d.EndDate is { } end ? name + " → " + DateMath.DayMonth(end) : name;
    }

    /// <summary>"14:00 · 1h" or "Time".</summary>
    public static string TimePillLabel(TaskDraft d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d.Timed ? DateMath.Hhmm(d.Time) + " · " + DurationLabel(d.DurationMinutes) : "Time";
    }

    /// <summary>"High" or "Priority".</summary>
    public static string PriorityPillLabel(TaskDraft d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d.Priority == Priority.None ? "Priority" : d.Priority.DisplayName();
    }

    /// <summary>Tag name or "Tag".</summary>
    public static string TagPillLabel(TaskDraft d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return string.IsNullOrEmpty(d.Tag) ? "Tag" : d.Tag;
    }

    /// <summary>Header "where": same as the date pill, or "Inbox".</summary>
    public static string WhereLabel(TaskDraft d, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d.Date is null ? "Inbox" : DatePillLabel(d, today);
    }

    /// <summary>"until 18 Nov" when Days &gt; 1, else empty.</summary>
    public static string UntilLabel(TaskDraft d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d.EndDate is { } end ? "until " + DateMath.DayMonth(end) : string.Empty;
    }

    /// <summary>"1 day" / "3 days".</summary>
    public static string DaysLabel(TaskDraft d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d.Days.ToString(CultureInfo.InvariantCulture) + (d.Days == 1 ? " day" : " days");
    }

    /// <summary>"14:00 – 15:00".</summary>
    public static string RangeLabel(TaskDraft d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var dur = d.DurationMinutes > 0 ? d.DurationMinutes : DefaultDuration;
        return DateMath.Hhmm(d.Time) + " – " + DateMath.Hhmm(DateMath.Minutes(d.Time) + dur);
    }

    /// <summary>"15m", "30m", "1h", "2h"; other values as "45m".</summary>
    public static string DurationLabel(int minutes) => minutes switch
    {
        15 => "15m",
        30 => "30m",
        60 => "1h",
        120 => "2h",
        _ => minutes.ToString(CultureInfo.InvariantCulture) + "m",
    };

    /// <summary>Heading "New task" / "Edit task".</summary>
    public static string Heading(TaskDraft d) => d?.EditId is null ? "New task" : "Edit task";

    /// <summary>Primary button "Create task" / "Save".</summary>
    public static string CommitLabel(TaskDraft d) => d?.EditId is null ? "Create task" : "Save";

    /// <summary>Footer hint "↵ create · Esc close" / "↵ save · Esc close".</summary>
    public static string KeyHint(TaskDraft d) => d?.EditId is null ? "↵ create · Esc close" : "↵ save · Esc close";

    /// <summary>"Next week" quick option: the first day of next week.</summary>
    public static DateOnly NextWeek(DateOnly today, DayOfWeek weekStart) => DateMath.WeekStart(today, weekStart).AddDays(7);

    /// <summary>The four quick options of the Date popover, with the current one selected.</summary>
    public static IReadOnlyList<DateQuickOption> DateOptions(TaskDraft d, DateOnly today, DayOfWeek weekStart)
    {
        ArgumentNullException.ThrowIfNull(d);
        var tomorrow = today.AddDays(1);
        var next = NextWeek(today, weekStart);
        return
        [
            new DateQuickOption("Today", string.Empty, today, d.Date == today),
            new DateQuickOption("Tomorrow", string.Empty, tomorrow, d.Date == tomorrow),
            new DateQuickOption("Next week", DateMath.DayMonth(next), next, d.Date == next),
            new DateQuickOption("No date", string.Empty, null, d.Date is null),
        ];
    }

    /// <summary>
    /// Picks a date (quick option or mini-calendar day): closes the popover and shows that month in the calendar;
    /// "No date" also resets the span to one day.
    /// </summary>
    public static TaskDraft PickDate(TaskDraft d, DateOnly? date)
    {
        ArgumentNullException.ThrowIfNull(d);
        return date is { } v
            ? d with { Date = v, CalendarMonth = DateMath.FirstOfMonth(v), Open = DraftPopover.None }
            : d with { Date = null, Days = 1, Open = DraftPopover.None };
    }

    /// <summary>‹ / › next to the date: one day back or forward (no-op when undated).</summary>
    public static TaskDraft StepDate(TaskDraft d, int direction)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d.Date is { } v ? d with { Date = v.AddDays(Math.Sign(direction)) } : d;
    }

    /// <summary>"Lasts N days" stepper, clamped to 1..14.</summary>
    public static TaskDraft StepDays(TaskDraft d, int direction)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d with { Days = Math.Clamp(d.Days + Math.Sign(direction), 1, MaxDays) };
    }

    /// <summary>‹ / › next to the time: ±15 minutes, clamped to 00:00..23:45.</summary>
    public static TaskDraft StepTime(TaskDraft d, int direction)
    {
        ArgumentNullException.ThrowIfNull(d);
        var m = Math.Clamp(DateMath.Minutes(d.Time) + 15 * Math.Sign(direction), 0, 23 * 60 + 45);
        return d with { Time = new TimeOnly(m / 60, m % 60) };
    }

    /// <summary>Mini calendar ‹ / ›: the previous or next month.</summary>
    public static TaskDraft ShiftCalendarMonth(TaskDraft d, int direction)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d with { CalendarMonth = DateMath.FirstOfMonth(d.CalendarMonth).AddMonths(direction) };
    }

    /// <summary>"November 2026".</summary>
    public static string CalendarLabel(TaskDraft d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return DateMath.MonthNames[d.CalendarMonth.Month - 1] + " " + d.CalendarMonth.Year.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Weekday initials over the mini calendar in configured order ("M T W T F S S" for Monday starts).</summary>
    public static IReadOnlyList<string> WeekdayLetters(DayOfWeek weekStart) =>
        DateMath.WeekdayOrder(weekStart).Select(w => DateMath.WeekdayShort(w)[..1]).ToArray();

    /// <summary>42 cells starting at the week start on/before the 1st of <see cref="TaskDraft.CalendarMonth"/>.</summary>
    public static IReadOnlyList<MiniCalendarDay> MiniCalendar(TaskDraft d, DateOnly today, DayOfWeek weekStart)
    {
        ArgumentNullException.ThrowIfNull(d);
        var month = DateMath.FirstOfMonth(d.CalendarMonth);
        var gs = DateMath.WeekStart(month, weekStart);
        var end = d.EndDate;
        var cells = new MiniCalendarDay[42];
        for (var i = 0; i < 42; i++)
        {
            var day = gs.AddDays(i);
            var col = i % 7;
            var sel = day == d.Date;
            var isEnd = end is not null && day == end;
            var inside = end is not null && day > d.Date && day < end;
            var band = end is not null && (sel || isEnd || inside);
            cells[i] = new MiniCalendarDay(
                day,
                day.Day,
                day.Year == month.Year && day.Month == month.Month,
                day == today,
                day < today,
                sel,
                sel || isEnd,
                band,
                band && (sel || col == 0),
                band && (isEnd || col == 6));
        }

        return cells;
    }

    /// <summary>Wheel: hour index 0..23 and minute index 0..11 for the draft's time.</summary>
    public static (int HourIndex, int MinuteIndex) WheelIndices(TimeOnly time)
    {
        var m = DateMath.Minutes(time);
        return (m / 60, Math.Min(11, DateMath.RoundHalfUp(m % 60 / 5.0)));
    }

    /// <summary>Wheel commit after 120 ms of stillness: sets the hour or minute (index·5) and Timed = true.</summary>
    public static TaskDraft CommitWheel(TaskDraft d, bool hourColumn, int index)
    {
        ArgumentNullException.ThrowIfNull(d);
        var (h, mi) = WheelIndices(d.Time);
        mi *= 5;
        if (hourColumn)
        {
            h = Math.Clamp(index, 0, 23);
        }
        else
        {
            mi = Math.Clamp(index, 0, 11) * 5;
        }

        var t = new TimeOnly(h, mi);
        return t != d.Time || !d.Timed ? d with { Time = t, Timed = true } : d;
    }
}
