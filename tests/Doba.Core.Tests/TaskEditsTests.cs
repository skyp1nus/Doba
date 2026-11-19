using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Store;

namespace Doba.Core.Tests;

public class TaskEditsTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);
    private static readonly TimeOnly Ten = new(10, 0);

    private static TaskStore Store(params string[] tags) => new([], tags, NullTaskPersistence.Instance);

    private static TaskItem Task(long id = 7, DateOnly? date = null, DateOnly? end = null, TimeOnly? time = null,
        int duration = 0, string notes = "n", string? tag = null, Priority priority = Priority.Low, bool done = false) =>
        new(id, "Write", notes, date, end, time, duration, priority, tag, done);

    private static TaskItem Created(TaskPatch patch, Priority defaultPriority = Priority.None, params string[] tags)
    {
        var result = TaskEdits.Create(Store(tags), patch, Today, defaultPriority);
        Assert.True(result.Ok, result.Error);
        return result.Task!;
    }

    private static string CreateError(TaskPatch patch, params string[] tags)
    {
        var result = TaskEdits.Create(Store(tags), patch, Today, Priority.None);
        Assert.False(result.Ok);
        Assert.Null(result.Task);
        return result.Error!;
    }

    private static TaskItem Applied(TaskItem existing, TaskPatch patch, params string[] tags)
    {
        var result = TaskEdits.Apply(Store(tags), existing, patch, Today);
        Assert.True(result.Ok, result.Error);
        return result.Task!;
    }

    private static string ApplyError(TaskItem existing, TaskPatch patch, params string[] tags)
    {
        var result = TaskEdits.Apply(Store(tags), existing, patch, Today);
        Assert.False(result.Ok);
        Assert.Null(result.Task);
        return result.Error!;
    }

    // ---------- Create ----------

    [Fact]
    public void Create_with_a_title_only_goes_to_the_inbox_with_the_default_priority()
    {
        var t = Created(new TaskPatch { Title = "  Buy bread  " }, Priority.High);
        Assert.Equal(new TaskItem(0, "Buy bread", string.Empty, null, null, null, 0, Priority.High, null, false), t);
    }

    [Fact]
    public void Create_needs_a_non_empty_title()
    {
        Assert.Contains("title is required", CreateError(new TaskPatch { Notes = "x" }), StringComparison.Ordinal);
        Assert.Contains("title must not be empty", CreateError(new TaskPatch { Title = "   " }), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_takes_every_supplied_field()
    {
        var t = Created(new TaskPatch
        {
            Title = "Call",
            Notes = "about the flat",
            Date = Today,
            Time = new TimeOnly(9, 30),
            DurationMinutes = 45,
            Priority = Priority.Asap,
            Tag = "# Deep  Work ",
        }, Priority.Low, "deep-work", "home");
        Assert.Equal(new TaskItem(0, "Call", "about the flat", Today, null, new TimeOnly(9, 30), 45, Priority.Asap, "deep-work", false), t);
    }

    [Fact]
    public void Create_without_a_duration_leaves_it_unspecified_not_the_dialog_default()
    {
        Assert.Equal(0, Created(new TaskPatch { Title = "a", Date = Today, Time = Ten }).DurationMinutes);
    }

    [Fact]
    public void Create_spans_days_from_the_date()
    {
        var t = Created(new TaskPatch { Title = "Trip", Date = Today, Days = 3 });
        Assert.Equal((Today, (DateOnly?)Today.AddDays(2)), (t.Date!.Value, t.End));
        Assert.Null(Created(new TaskPatch { Title = "One", Date = Today, Days = 1 }).End);
        Assert.Equal(Today.AddDays(13), Created(new TaskPatch { Title = "Max", Date = Today, Days = 14 }).End);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(-3)]
    public void Create_refuses_days_outside_1_to_14(int days) =>
        Assert.Contains("days must be between 1 and 14", CreateError(new TaskPatch { Title = "a", Date = Today, Days = days }), StringComparison.Ordinal);

    [Fact]
    public void Create_refuses_a_span_or_a_time_without_a_date()
    {
        Assert.Contains("days needs a date", CreateError(new TaskPatch { Title = "a", Days = 3 }), StringComparison.Ordinal);
        Assert.Contains("time needs a date", CreateError(new TaskPatch { Title = "a", Time = Ten }), StringComparison.Ordinal);
        Assert.Null(Created(new TaskPatch { Title = "a", Days = 1 }).Date); // one day is no span: fine in the Inbox
    }

    [Fact]
    public void Create_refuses_a_time_on_a_multi_day_task_instead_of_dropping_it()
    {
        var error = CreateError(new TaskPatch { Title = "a", Date = Today, Days = 2, Time = Ten });
        Assert.Contains("time is only for a single-day task", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void Create_refuses_a_duration_outside_0_to_1440(int minutes) =>
        Assert.Contains("duration_minutes must be between 0 and 1440",
            CreateError(new TaskPatch { Title = "a", Date = Today, Time = Ten, DurationMinutes = minutes }), StringComparison.Ordinal);

    [Fact]
    public void Create_accepts_duration_limits()
    {
        Assert.Equal(1440, Created(new TaskPatch { Title = "a", Date = Today, Time = Ten, DurationMinutes = 1440 }).DurationMinutes);
        Assert.Equal(0, Created(new TaskPatch { Title = "a", Date = Today, Time = Ten, DurationMinutes = 0 }).DurationMinutes);
    }

    [Fact]
    public void Create_refuses_an_unknown_tag_and_lists_the_existing_ones()
    {
        var error = CreateError(new TaskPatch { Title = "a", Tag = "Errands" }, "work", "home");
        Assert.Contains("Unknown tag 'Errands'", error, StringComparison.Ordinal);
        Assert.Contains("work, home", error, StringComparison.Ordinal);

        Assert.Contains("there are no tags yet", CreateError(new TaskPatch { Title = "a", Tag = "work" }), StringComparison.Ordinal);
        Assert.Contains("tag must not be empty", CreateError(new TaskPatch { Title = "a", Tag = " # " }, "work"), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_refuses_an_undefined_priority() =>
        Assert.Contains("priority", CreateError(new TaskPatch { Title = "a", Priority = (Priority)9 }), StringComparison.Ordinal);

    [Fact]
    public void Create_refuses_clear() =>
        Assert.Contains("clear", CreateError(new TaskPatch { Title = "a", Clear = TaskClear.Notes }), StringComparison.Ordinal);

    // ---------- Apply ----------

    [Fact]
    public void Apply_keeps_every_field_that_is_not_supplied()
    {
        var existing = Task(date: Today, time: Ten, duration: 0, tag: "work", done: true);
        var t = Applied(existing, new TaskPatch { Title = "Write the report" }, "work");
        Assert.Equal(existing with { Title = "Write the report" }, t);
        Assert.Equal(0, t.DurationMinutes); // not the dialog's 60
        Assert.Equal(existing, Applied(existing, new TaskPatch(), "work"));
    }

    [Fact]
    public void Apply_replaces_supplied_fields()
    {
        var existing = Task(date: Today);
        var t = Applied(existing, new TaskPatch
        {
            Notes = "",
            Date = Today.AddDays(1),
            Time = Ten,
            DurationMinutes = 30,
            Priority = Priority.None,
            Tag = "HOME",
        }, "home");
        Assert.Equal(existing with { Notes = "", Date = Today.AddDays(1), Time = Ten, DurationMinutes = 30, Priority = Priority.None, Tag = "home" }, t);
    }

    [Fact]
    public void Apply_refuses_an_empty_title() =>
        Assert.Contains("title must not be empty", ApplyError(Task(), new TaskPatch { Title = "\t" }), StringComparison.Ordinal);

    [Fact]
    public void Apply_moves_a_multi_day_task_whole_when_only_the_date_changes()
    {
        var existing = Task(date: Today, end: Today.AddDays(2));
        var t = Applied(existing, new TaskPatch { Date = Today.AddDays(10) });
        Assert.Equal((Today.AddDays(10), (DateOnly?)Today.AddDays(12)), (t.Date!.Value, t.End));
    }

    [Fact]
    public void Apply_changes_the_length_from_days()
    {
        var existing = Task(date: Today, end: Today.AddDays(2));
        Assert.Equal(Today.AddDays(4), Applied(existing, new TaskPatch { Days = 5 }).End);
        Assert.Null(Applied(existing, new TaskPatch { Days = 1 }).End);
    }

    [Fact]
    public void Apply_checks_days_only_when_supplied()
    {
        // An imported 20-day span is longer than the dialog allows; other edits still work and keep it.
        var existing = Task(date: Today, end: Today.AddDays(19));
        Assert.Equal(existing with { Title = "Renamed" }, Applied(existing, new TaskPatch { Title = "Renamed" }));
        Assert.Equal(Today.AddDays(21), Applied(existing, new TaskPatch { Date = Today.AddDays(2) }).End);
        Assert.Contains("days must be between 1 and 14", ApplyError(existing, new TaskPatch { Days = 20 }), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_refuses_making_a_timed_task_multi_day_unless_the_time_is_cleared()
    {
        var timed = Task(date: Today, time: Ten);
        var error = ApplyError(timed, new TaskPatch { Days = 3 });
        Assert.Contains("10:00", error, StringComparison.Ordinal);
        Assert.Contains("clear: [\"time\"]", error, StringComparison.Ordinal);

        var t = Applied(timed, new TaskPatch { Days = 3, Clear = TaskClear.Time });
        Assert.Equal((Today.AddDays(2), (TimeOnly?)null), (t.End!.Value, t.Time));
    }

    [Fact]
    public void Apply_refuses_a_time_on_a_multi_day_task()
    {
        var span = Task(date: Today, end: Today.AddDays(2));
        Assert.Contains("time is only for a single-day task", ApplyError(span, new TaskPatch { Time = Ten }), StringComparison.Ordinal);
        var single = Applied(span, new TaskPatch { Time = Ten, Days = 1 });
        Assert.Equal((Ten, (DateOnly?)null), (single.Time!.Value, single.End));
    }

    [Fact]
    public void Apply_refuses_a_time_or_a_span_on_an_inbox_task_without_a_date()
    {
        var inbox = Task();
        Assert.Contains("time needs a date", ApplyError(inbox, new TaskPatch { Time = Ten }), StringComparison.Ordinal);
        Assert.Contains("days needs a date", ApplyError(inbox, new TaskPatch { Days = 2 }), StringComparison.Ordinal);
        var dated = Applied(inbox, new TaskPatch { Date = Today, Time = Ten });
        Assert.Equal((Today, (TimeOnly?)Ten), (dated.Date!.Value, dated.Time));
    }

    [Fact]
    public void Apply_clears_fields()
    {
        var existing = Task(date: Today, time: Ten, tag: "work");
        Assert.Equal(existing with { Notes = "", Time = null, Tag = null },
            Applied(existing, new TaskPatch { Clear = TaskClear.Notes | TaskClear.Time | TaskClear.Tag }, "work"));
    }

    [Fact]
    public void Clearing_the_date_moves_to_the_inbox_and_drops_time_and_span()
    {
        var timed = Task(date: Today, time: Ten, duration: 45);
        Assert.Equal(timed with { Date = null, Time = null }, Applied(timed, new TaskPatch { Clear = TaskClear.Date }));

        var span = Task(date: Today, end: Today.AddDays(3));
        Assert.Equal(span with { Date = null, End = null }, Applied(span, new TaskPatch { Clear = TaskClear.Date }));
    }

    [Fact]
    public void Clearing_the_date_with_a_new_time_span_or_date_is_refused()
    {
        var existing = Task(date: Today);
        Assert.Contains("time needs a date", ApplyError(existing, new TaskPatch { Clear = TaskClear.Date, Time = Ten }), StringComparison.Ordinal);
        Assert.Contains("days needs a date", ApplyError(existing, new TaskPatch { Clear = TaskClear.Date, Days = 2 }), StringComparison.Ordinal);
        Assert.Contains("date is both set and cleared", ApplyError(existing, new TaskPatch { Clear = TaskClear.Date, Date = Today }), StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_both_set_and_cleared_is_refused()
    {
        Assert.Contains("notes is both set and cleared", ApplyError(Task(), new TaskPatch { Notes = "x", Clear = TaskClear.Notes }), StringComparison.Ordinal);
        Assert.Contains("time is both set and cleared", ApplyError(Task(date: Today), new TaskPatch { Time = Ten, Clear = TaskClear.Time }), StringComparison.Ordinal);
        Assert.Contains("tag is both set and cleared", ApplyError(Task(), new TaskPatch { Tag = "work", Clear = TaskClear.Tag }, "work"), StringComparison.Ordinal);
        Assert.Contains("clear accepts only", ApplyError(Task(), new TaskPatch { Clear = (TaskClear)64 }), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_refuses_an_unknown_tag_and_keeps_the_task()
    {
        var existing = Task(tag: "work");
        var error = ApplyError(existing, new TaskPatch { Tag = "play" }, "work", "home");
        Assert.Contains("Existing tags: work, home", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_keeps_done_and_id()
    {
        var existing = Task(id: 42, done: true);
        var t = Applied(existing, new TaskPatch { Date = Today });
        Assert.Equal((42L, true), (t.Id, t.Done));
    }

    // ---------- Move ----------

    [Fact]
    public void Move_to_the_inbox_drops_time_and_span()
    {
        var store = Store();
        var timed = Task(date: Today, time: Ten, duration: 30);
        Assert.Equal(timed with { Date = null, Time = null }, TaskEdits.Move(store, timed, null).Task);
        var span = Task(date: Today, end: Today.AddDays(2));
        Assert.Equal(span with { Date = null, End = null }, TaskEdits.Move(store, span, null).Task);
    }

    [Fact]
    public void Move_shifts_a_span_whole_and_keeps_a_single_days_time()
    {
        var store = Store();
        var span = Task(date: Today, end: Today.AddDays(19));
        var moved = TaskEdits.Move(store, span, Today.AddDays(-5)).Task!;
        Assert.Equal((Today.AddDays(-5), (DateOnly?)Today.AddDays(14)), (moved.Date!.Value, moved.End));

        var timed = Task(date: Today, time: Ten);
        Assert.Equal(timed with { Date = Today.AddDays(1) }, TaskEdits.Move(store, timed, Today.AddDays(1)).Task);

        var inbox = Task();
        Assert.Equal(inbox with { Date = Today }, TaskEdits.Move(store, inbox, Today).Task);
    }

    [Fact]
    public void Move_past_the_end_of_the_calendar_is_refused()
    {
        var span = Task(date: Today, end: Today.AddDays(2));
        var result = TaskEdits.Move(Store(), span, DateOnly.MaxValue.AddDays(-1));
        Assert.False(result.Ok);
        Assert.Contains("too far", result.Error, StringComparison.Ordinal);
    }
}
