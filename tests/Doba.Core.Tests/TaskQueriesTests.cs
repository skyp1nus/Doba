using Doba.Core.Model;
using Doba.Core.Projections;
using Doba.Core.Store;

namespace Doba.Core.Tests;

public class TaskQueriesTests
{
    private static readonly DateOnly D = new(2026, 11, 19);

    private static TaskItem T(long id, DateOnly? date = null, DateOnly? end = null, TimeOnly? time = null,
        Priority prio = Priority.None, bool done = false) =>
        new(id, "t" + id, string.Empty, date, end, time, 0, prio, null, done);

    private static TaskStore Store(params TaskItem[] tasks) => new(tasks, [], NullTaskPersistence.Instance);

    [Fact]
    public void In_range_lists_each_task_once_sorted_by_date_then_time()
    {
        var store = Store(
            T(1, D, time: new TimeOnly(14, 0)),
            T(2, D),
            T(3, D, time: new TimeOnly(9, 0)),
            T(4, D.AddDays(-3), D.AddDays(5)),        // span covering the whole range, started before it
            T(5, D.AddDays(1), D.AddDays(2)),         // span inside the range: listed once, not per day
            T(6, D.AddDays(1), prio: Priority.High),
            T(7, D.AddDays(1)),
            T(8),                                      // undated: never in a range
            T(9, D.AddDays(3)),                        // after the range
            T(10, D.AddDays(-1)));                     // before the range

        var ids = TaskQueries.InRange(store, D, D.AddDays(2), includeDone: false).Select(t => t.Id);
        Assert.Equal([4L, 3, 1, 2, 6, 5, 7], ids);
    }

    [Fact]
    public void In_range_leaves_out_done_tasks_unless_asked()
    {
        var store = Store(T(1, D, done: true), T(2, D), T(3, D.AddDays(-1), D.AddDays(1), done: true));
        Assert.Equal([2L], TaskQueries.InRange(store, D, D, includeDone: false).Select(t => t.Id));
        Assert.Equal([3L, 1, 2], TaskQueries.InRange(store, D, D, includeDone: true).Select(t => t.Id));
    }

    [Fact]
    public void In_range_at_the_first_day_of_the_calendar_is_empty_with_spans_in_the_store()
    {
        var store = Store(T(1, D, D.AddDays(2)), T(2, D));

        Assert.Empty(TaskQueries.InRange(store, DateOnly.MinValue, DateOnly.MinValue.AddDays(5), includeDone: true));
        Assert.Empty(store.SpansOverlapping(DateOnly.MinValue, DateOnly.MinValue));
        Assert.Equal([1L], store.SpansOverlapping(DateOnly.MinValue, D).Select(t => t.Id));
    }

    [Fact]
    public void In_range_of_one_day_and_an_inverted_range()
    {
        var store = Store(T(1, D), T(2, D.AddDays(1)));
        Assert.Equal([1L], TaskQueries.InRange(store, D, D, false).Select(t => t.Id));
        Assert.Empty(TaskQueries.InRange(store, D.AddDays(1), D, false));
    }

    [Fact]
    public void In_range_finds_a_span_that_ends_on_the_first_day_or_starts_on_the_last()
    {
        var store = Store(T(1, D.AddDays(-4), D), T(2, D.AddDays(6), D.AddDays(9)), T(3, D.AddDays(-4), D.AddDays(-1)));
        Assert.Equal([1L, 2], TaskQueries.InRange(store, D, D.AddDays(6), false).Select(t => t.Id));
    }

    [Fact]
    public void Inbox_is_in_inbox_screen_order()
    {
        var store = Store(
            T(1, prio: Priority.Low),
            T(2),
            T(3, prio: Priority.Asap),
            T(4, prio: Priority.Low, done: true),
            T(5, prio: Priority.High),
            T(6, prio: Priority.Asap),
            T(7, done: true),
            T(8, D));                                  // dated: not in the Inbox
        Assert.Equal([3L, 6, 5, 1, 2], TaskQueries.Inbox(store, includeDone: false).Select(t => t.Id));
        Assert.Equal([3L, 6, 5, 1, 2, 4, 7], TaskQueries.Inbox(store, includeDone: true).Select(t => t.Id));
    }

    [Fact]
    public void Inbox_matches_the_projector_left_column()
    {
        var store = Store(T(1, prio: Priority.Medium), T(2, prio: Priority.Asap), T(3, done: true), T(4));
        var ctx = new ProjectionContext(D, new TimeOnly(9, 0), DayOfWeek.Monday, null);
        var screen = Projector.Inbox(store, ctx).Left.SelectMany(g => g.Items).Select(r => r.Id);
        Assert.Equal(screen, TaskQueries.Inbox(store, includeDone: true).Select(t => t.Id));
    }
}
