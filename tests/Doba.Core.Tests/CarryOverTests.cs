using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Store;

namespace Doba.Core.Tests;

public class CarryOverTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static TaskItem T(long id, DateOnly? date = null, DateOnly? end = null, TimeOnly? time = null, bool done = false) =>
        new(id, "t" + id, "notes " + id, date, end, time, 45, Priority.High, "work", done);

    private static List<TaskItem> Mixed() =>
    [
        T(1, Today.AddDays(-1), time: new TimeOnly(9, 30)),     // carried, keeps its time
        T(2, Today.AddDays(-7)),                                 // carried
        T(3, Today.AddDays(-2), done: true),                     // done: stays
        T(4, Today),                                             // today: stays
        T(5, Today.AddDays(1)),                                  // future: stays
        T(6),                                                    // undated: stays
        T(7, Today.AddDays(-5), Today.AddDays(-3)),              // past span: never moved
        T(8, Today.AddDays(-2), Today.AddDays(2)),               // running span: never moved
        T(9, Today.AddDays(-30)),                                // carried
    ];

    [Fact]
    public void Select_finds_exactly_the_open_single_day_tasks_dated_before_today()
    {
        var store = new TaskStore(Mixed(), [], NullTaskPersistence.Instance);

        Assert.Equal([1L, 2, 9], CarryOver.Select(store, Today).Select(t => t.Id));
    }

    [Fact]
    public void Run_moves_them_to_today_keeping_everything_else_with_one_event_and_one_write_each()
    {
        var persistence = new RecordingPersistence();
        var store = new TaskStore(Mixed(), [], persistence);
        var before = store.All.ToDictionary(t => t.Id);
        var events = new List<TaskStoreChangedEventArgs>();
        store.Changed += (_, e) => events.Add(e);

        var moved = CarryOver.Run(store, Today);

        Assert.Equal(3, moved);
        var e = Assert.Single(events);
        Assert.Equal(TaskChangeKind.Batch, e.Kind);
        Assert.Equal(["upsert 1", "upsert 2", "upsert 9"], persistence.Calls);
        foreach (var id in new long[] { 1, 2, 9 })
        {
            Assert.Equal(before[id] with { Date = Today }, store.Get(id));
        }

        Assert.Equal(new TimeOnly(9, 30), store.Get(1)!.Time);
        foreach (var id in new long[] { 3, 4, 5, 6, 7, 8 })
        {
            Assert.Same(before[id], store.Get(id));
        }

        // The date index follows: today now holds 1, 2, 4, 9 (Id order); yesterday is empty.
        Assert.Equal([1L, 2, 4, 9], store.OnDate(Today).Select(t => t.Id));
        Assert.Empty(store.OnDate(Today.AddDays(-1)));
    }

    [Fact]
    public void Run_is_idempotent()
    {
        var persistence = new RecordingPersistence();
        var store = new TaskStore(Mixed(), [], persistence);
        CarryOver.Run(store, Today);
        persistence.Calls.Clear();
        var events = 0;
        store.Changed += (_, _) => events++;

        Assert.Equal(0, CarryOver.Run(store, Today));
        Assert.Empty(CarryOver.Select(store, Today));
        Assert.Equal(0, events);
        Assert.Empty(persistence.Calls);
    }

    [Fact]
    public void Run_on_the_next_day_moves_yesterdays_open_tasks_again()
    {
        var store = new TaskStore(Mixed(), [], NullTaskPersistence.Instance);
        CarryOver.Run(store, Today);

        // 1, 2, 4 and 9 were open on the 15th; 5 was already dated the 16th.
        Assert.Equal(4, CarryOver.Run(store, Today.AddDays(1)));
        Assert.Equal([1L, 2, 4, 5, 9], store.OnDate(Today.AddDays(1)).Select(t => t.Id));
    }

    [Fact]
    public void Run_with_nothing_overdue_raises_no_event()
    {
        var store = new TaskStore([T(1, Today), T(2)], [], NullTaskPersistence.Instance);
        var events = 0;
        store.Changed += (_, _) => events++;

        Assert.Equal(0, CarryOver.Run(store, Today));
        Assert.Equal(0, events);
    }

    [Fact]
    public void Demo_data_carries_its_overdue_tasks()
    {
        var store = new TaskStore(DemoData.Tasks(), DemoData.Tags, NullTaskPersistence.Instance);
        var expected = store.All.Where(t => !t.Done && t.End is null && t.Date < DemoData.Today).Select(t => t.Id).ToList();
        Assert.NotEmpty(expected);

        Assert.Equal(expected.Count, CarryOver.Run(store, DemoData.Today));
        Assert.All(expected, id => Assert.Equal(DemoData.Today, store.Get(id)!.Date));
        Assert.Empty(CarryOver.Select(store, DemoData.Today));
    }
}
