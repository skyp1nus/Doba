using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Navigation;

namespace Doba.Core.Tests;

public class TaskDialogTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static TaskDraft Draft(DateOnly? date = null, int days = 1, bool timed = false, string title = "Write", int dur = 60) =>
        TaskDialog.NewDraft(date, Today, Priority.None) with { Title = title, Days = days, Timed = timed, DurationMinutes = dur };

    [Fact]
    public void NewDraft_defaults_by_view()
    {
        var anchor = new DateOnly(2026, 12, 3);
        Assert.Null(TaskDialog.NewDraft(AppView.Inbox, anchor, Today, Priority.None).Date);
        Assert.Equal(anchor, TaskDialog.NewDraft(AppView.Day, anchor, Today, Priority.None).Date);
        Assert.Equal(Today, TaskDialog.NewDraft(AppView.Month, anchor, Today, Priority.None).Date);
        var d = TaskDialog.NewDraft(AppView.Day, anchor, Today, Priority.High);
        Assert.Equal((Priority.High, new TimeOnly(10, 0), 60, 1, false), (d.Priority, d.Time, d.DurationMinutes, d.Days, d.Timed));
        Assert.Equal(new DateOnly(2026, 12, 1), d.CalendarMonth);
        Assert.Equal(new DateOnly(2026, 11, 1), TaskDialog.NewDraft(AppView.Inbox, anchor, Today, Priority.None).CalendarMonth);
        Assert.Equal(string.Empty, d.Notes);
        Assert.False(d.CanSave);
    }

    [Fact]
    public void ToTask_applies_span_and_time_rules()
    {
        var span = TaskDialog.ToTask(Draft(Today, days: 3, timed: true), null);
        Assert.Equal((Today, (DateOnly?)Today.AddDays(2), (TimeOnly?)null), (span.Date!.Value, span.End, span.Time));

        var undated = TaskDialog.ToTask(Draft(null, days: 1, timed: true), null);
        Assert.Null(undated.Time);
        Assert.Null(undated.End);

        var timed = TaskDialog.ToTask(Draft(Today, timed: true, title: "  Trim me  ") with { Time = new TimeOnly(14, 0) }, null);
        Assert.Equal(new TimeOnly(14, 0), timed.Time);
        Assert.Equal("Trim me", timed.Title);
        Assert.Equal(0, timed.Id);
        Assert.False(timed.Done);
        Assert.NotNull(timed.Notes);

        var existing = new TaskItem(12, "Old", "n", Today, null, null, 0, Priority.Low, "work", true);
        var edited = TaskDialog.ToTask(TaskDialog.EditDraft(existing, Today) with { Title = "New" }, existing);
        Assert.Equal((12L, "New", true, "work", Priority.Low, "n"), (edited.Id, edited.Title, edited.Done, edited.Tag, edited.Priority, edited.Notes));

        Assert.Throws<ArgumentException>(() => TaskDialog.ToTask(Draft(title: "   "), null));
    }

    [Fact]
    public void EditDraft_of_a_task_with_null_notes()
    {
        var t = new TaskItem(3, "x", null!, new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 20), null, 0, Priority.None, null, false);
        var d = TaskDialog.EditDraft(t, Today);
        Assert.Equal(string.Empty, d.Notes);
        Assert.Equal(3, d.Days);
        Assert.Equal(60, d.DurationMinutes);
        Assert.False(d.Timed);
        Assert.Equal(string.Empty, TaskDialog.ToTask(d, t).Notes);
    }

    [Fact]
    public void Labels()
    {
        var d = Draft(new DateOnly(2026, 11, 18), days: 3, timed: true, dur: 45);
        Assert.Equal("Wed 18 Nov → 20 Nov", TaskDialog.DatePillLabel(d, Today));
        Assert.Equal("Wed 18 Nov → 20 Nov", TaskDialog.WhereLabel(d, Today));
        Assert.Equal("until 20 Nov", TaskDialog.UntilLabel(d));
        Assert.Equal("3 days", TaskDialog.DaysLabel(d));
        Assert.Equal("10:00 · 45m", TaskDialog.TimePillLabel(d));
        Assert.Equal("10:00 – 10:45", TaskDialog.RangeLabel(d));
        Assert.Equal("Tomorrow", TaskDialog.DatePillLabel(Draft(Today.AddDays(1)), Today));
        Assert.Equal("Today", TaskDialog.DatePillLabel(Draft(Today), Today));
        Assert.Equal("Date", TaskDialog.DatePillLabel(Draft(), Today));
        Assert.Equal("Inbox", TaskDialog.WhereLabel(Draft(), Today));
        Assert.Equal("Time", TaskDialog.TimePillLabel(Draft(Today)));
        Assert.Equal("1 day", TaskDialog.DaysLabel(Draft(Today)));
        Assert.Equal(string.Empty, TaskDialog.UntilLabel(Draft(Today)));
        Assert.Equal("Priority", TaskDialog.PriorityPillLabel(Draft()));
        Assert.Equal("ASAP", TaskDialog.PriorityPillLabel(Draft() with { Priority = Priority.Asap }));
        Assert.Equal("Tag", TaskDialog.TagPillLabel(Draft()));
        Assert.Equal("work", TaskDialog.TagPillLabel(Draft() with { Tag = "work" }));
        Assert.Equal("2h", TaskDialog.DurationLabel(120));
        Assert.Equal(("New task", "Create task"), (TaskDialog.Heading(Draft()), TaskDialog.CommitLabel(Draft())));
        Assert.Equal(("Edit task", "Save", "↵ save · Esc close"), (TaskDialog.Heading(Draft() with { EditId = 1 }), TaskDialog.CommitLabel(Draft() with { EditId = 1 }), TaskDialog.KeyHint(Draft() with { EditId = 1 })));
        Assert.Equal("November 2026", TaskDialog.CalendarLabel(Draft(Today)));
    }

    [Fact]
    public void Next_week_and_letters_follow_the_week_start()
    {
        Assert.Equal(new DateOnly(2026, 11, 16), TaskDialog.NextWeek(Today, DayOfWeek.Monday));
        Assert.Equal(new DateOnly(2026, 11, 22), TaskDialog.NextWeek(Today, DayOfWeek.Sunday));
        Assert.Equal(["M", "T", "W", "T", "F", "S", "S"], TaskDialog.WeekdayLetters(DayOfWeek.Monday));
        Assert.Equal(["S", "M", "T", "W", "T", "F", "S"], TaskDialog.WeekdayLetters(DayOfWeek.Sunday));
        var opts = TaskDialog.DateOptions(Draft(new DateOnly(2026, 11, 22)), Today, DayOfWeek.Sunday);
        Assert.Equal(["Today", "Tomorrow", "Next week", "No date"], opts.Select(o => o.Label));
        Assert.Equal("22 Nov", opts[2].Hint);
        Assert.Equal([false, false, true, false], opts.Select(o => o.Selected));
    }

    [Fact]
    public void Mini_calendar_uses_the_configured_week_start()
    {
        var d = Draft(new DateOnly(2026, 11, 14), days: 3);
        var mon = TaskDialog.MiniCalendar(d, Today, DayOfWeek.Monday);
        var sun = TaskDialog.MiniCalendar(d, Today, DayOfWeek.Sunday);
        Assert.Equal(42, mon.Count);
        Assert.Equal(new DateOnly(2026, 10, 26), mon[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 1), sun[0].Date);
        Assert.Equal(new DateOnly(2026, 12, 12), sun[41].Date);

        // Sunday start: 14 Nov is the last column (Saturday), 15–16 Nov wrap to the next row.
        var start = sun.Single(c => c.Date == new DateOnly(2026, 11, 14));
        Assert.True(start is { Selected: true, Marked: true, InRange: true, BandStart: true, BandEnd: true });
        var mid = sun.Single(c => c.Date == new DateOnly(2026, 11, 15));
        Assert.True(mid is { Selected: false, Marked: false, InRange: true, BandStart: true, BandEnd: false, IsToday: true });
        var end = sun.Single(c => c.Date == new DateOnly(2026, 11, 16));
        Assert.True(end is { Marked: true, InRange: true, BandStart: false, BandEnd: true });
        Assert.False(sun.Single(c => c.Date == new DateOnly(2026, 11, 17)).InRange);
        Assert.True(sun.Single(c => c.Date == new DateOnly(2026, 11, 3)).IsPast);
        Assert.False(sun.Single(c => c.Date == new DateOnly(2026, 12, 3)).InMonth);
    }

    [Theory]
    [InlineData(9, 57, 9, 11)]
    [InlineData(9, 58, 9, 11)]
    [InlineData(10, 2, 10, 0)]
    [InlineData(10, 3, 10, 1)]
    [InlineData(18, 32, 18, 6)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(23, 55, 23, 11)]
    public void Wheel_indices_round_like_the_prototype(int h, int m, int hi, int mi) =>
        Assert.Equal((hi, mi), TaskDialog.WheelIndices(new TimeOnly(h, m)));

    [Fact]
    public void CommitWheel_snaps_and_sets_timed()
    {
        var d = Draft(Today) with { Time = new TimeOnly(9, 57) };
        var h = TaskDialog.CommitWheel(d, hourColumn: true, 14);
        Assert.Equal((new TimeOnly(14, 55), true), (h.Time, h.Timed));
        var m = TaskDialog.CommitWheel(d, hourColumn: false, 3);
        Assert.Equal(new TimeOnly(9, 15), m.Time);
        Assert.Equal(new TimeOnly(23, 55), TaskDialog.CommitWheel(d, true, 99).Time);
        Assert.Equal(new TimeOnly(9, 0), TaskDialog.CommitWheel(d, false, -4).Time);

        var timed = Draft(Today, timed: true) with { Time = new TimeOnly(10, 0) };
        Assert.Same(timed, TaskDialog.CommitWheel(timed, true, 10));
        var untimed = timed with { Timed = false };
        Assert.True(TaskDialog.CommitWheel(untimed, true, 10).Timed);
    }

    [Fact]
    public void Steppers_clamp()
    {
        var d = Draft(Today);
        Assert.Equal(1, TaskDialog.StepDays(d, -1).Days);
        Assert.Equal(14, TaskDialog.StepDays(d with { Days = 14 }, 1).Days);
        Assert.Equal(2, TaskDialog.StepDays(d, 1).Days);
        Assert.Equal(new DateOnly(2026, 12, 1), TaskDialog.ShiftCalendarMonth(d, 1).CalendarMonth);
        Assert.Equal(new DateOnly(2025, 12, 1), TaskDialog.ShiftCalendarMonth(d with { CalendarMonth = new DateOnly(2026, 1, 1) }, -1).CalendarMonth);

        var picked = TaskDialog.PickDate(d with { Open = DraftPopover.Date, Days = 4 }, new DateOnly(2027, 2, 9));
        Assert.Equal((new DateOnly(2027, 2, 9), new DateOnly(2027, 2, 1), DraftPopover.None, 4), (picked.Date!.Value, picked.CalendarMonth, picked.Open, picked.Days));
        var none = TaskDialog.PickDate(d with { Days = 4 }, null);
        Assert.Equal((null, 1), (none.Date, none.Days));
    }

    [Fact]
    public void Picking_a_duration_also_switches_on_set_time()
    {
        // Prototype durOpts.pick: setP({ dur, timed: true }).
        var untimed = Draft(Today, timed: false, dur: 60);
        var picked = TaskDialog.PickDuration(untimed, 30);
        Assert.Equal((30, true), (picked.DurationMinutes, picked.Timed));

        // The current chip on a timed draft changes nothing.
        var timed = Draft(Today, timed: true, dur: 60);
        Assert.Same(timed, TaskDialog.PickDuration(timed, 60));

        // The current chip on an untimed draft still switches on "Set time".
        Assert.True(TaskDialog.PickDuration(untimed, 60).Timed);
    }

    [Fact]
    public void Time_mode_switch_sets_or_clears_timed()
    {
        var d = Draft(Today, timed: false);
        Assert.True(TaskDialog.SetTimed(d, true).Timed);
        Assert.Same(d, TaskDialog.SetTimed(d, false));
        Assert.False(TaskDialog.SetTimed(d with { Timed = true }, false).Timed);
    }

    [Fact]
    public void Picking_a_priority_or_a_tag_closes_its_popover()
    {
        var d = Draft(Today) with { Open = DraftPopover.Priority };
        var p = TaskDialog.PickPriority(d, Priority.Asap);
        Assert.Equal((Priority.Asap, DraftPopover.None), (p.Priority, p.Open));

        var t = TaskDialog.PickTag(d with { Open = DraftPopover.Tag }, "work");
        Assert.Equal(("work", DraftPopover.None), (t.Tag, t.Open));
        var none = TaskDialog.PickTag(t with { Open = DraftPopover.Tag }, null);
        Assert.Equal(((string?)null, DraftPopover.None), (none.Tag, none.Open));
    }

    [Fact]
    public void A_pill_toggles_its_popover_and_closes_any_other()
    {
        var d = Draft(Today);
        var date = TaskDialog.TogglePopover(d, DraftPopover.Date);
        Assert.Equal(DraftPopover.Date, date.Open);
        Assert.Equal(DraftPopover.None, TaskDialog.TogglePopover(date, DraftPopover.Date).Open);   // same pill: closes
        Assert.Equal(DraftPopover.Tag, TaskDialog.TogglePopover(date, DraftPopover.Tag).Open);     // another: replaces

        Assert.Equal(DraftPopover.None, TaskDialog.ClosePopover(date).Open);
        Assert.Same(d, TaskDialog.ClosePopover(d));
    }

    [Fact]
    public void Calendar_today_shows_the_current_month_and_keeps_the_date_and_popover()
    {
        // Prototype calToday: setP({ calMonth: T.slice(0, 7) + '-01' }).
        var d = Draft(new DateOnly(2027, 2, 14)) with { CalendarMonth = new DateOnly(2027, 2, 1), Open = DraftPopover.Date };
        var shown = TaskDialog.ShowTodayMonth(d, Today);
        Assert.Equal((new DateOnly(2026, 11, 1), (DateOnly?)new DateOnly(2027, 2, 14), DraftPopover.Date), (shown.CalendarMonth, shown.Date, shown.Open));
        Assert.Same(shown, TaskDialog.ShowTodayMonth(shown, Today.AddDays(3)));
    }

    [Fact]
    public void A_time_popover_whose_pill_is_gone_shows_as_closed()
    {
        var open = Draft(Today) with { Open = DraftPopover.Time };
        Assert.Equal(DraftPopover.Time, TaskDialog.VisiblePopover(open));
        Assert.Equal(DraftPopover.None, TaskDialog.VisiblePopover(open with { Days = 2 }));  // "Lasts 2 days"
        Assert.Equal(DraftPopover.None, TaskDialog.VisiblePopover(open with { Date = null })); // "No date"
        Assert.Equal(DraftPopover.Date, TaskDialog.VisiblePopover(open with { Days = 2, Open = DraftPopover.Date }));
    }

    [Fact]
    public void Days_stepper_never_cuts_a_span_longer_than_its_maximum()
    {
        // A 20-day task (import allows any span) opened in the dialog.
        var task = new TaskItem(3, "Long trip", string.Empty, Today, Today.AddDays(19), null, 0, Priority.None, null, false);
        var d = TaskDialog.EditDraft(task, Today);
        Assert.Equal(20, d.Days);

        Assert.Equal(20, TaskDialog.StepDays(d, 1).Days);   // "+" does not shorten it to 14
        Assert.Equal(19, TaskDialog.StepDays(d, -1).Days);  // "−" takes one day off, not six
        Assert.Equal(Today.AddDays(19), TaskDialog.ToTask(TaskDialog.StepDays(d, 1), task).End);

        // Once at or under the maximum, the usual 1..14 range applies again.
        Assert.Equal(14, TaskDialog.StepDays(d with { Days = 15 }, -1).Days);
        Assert.Equal(14, TaskDialog.StepDays(d with { Days = 14 }, 1).Days);
    }
}
