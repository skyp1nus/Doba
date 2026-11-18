using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Store;

namespace Doba.Core.Tests;

/// <summary>Records every persistence call.</summary>
internal sealed class RecordingPersistence : ITaskPersistence
{
    public List<string> Calls { get; } = [];

    public void Upsert(TaskItem item) => Calls.Add("upsert " + item.Id);

    public void Delete(long id) => Calls.Add("delete " + id);

    public void SaveTags(IReadOnlyList<string> tags) => Calls.Add("tags " + string.Join(",", tags));

    public void ReplaceAll(IReadOnlyList<TaskItem> tasks, IReadOnlyList<string> tags) => Calls.Add($"replace {tasks.Count} {string.Join(",", tags)}");

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class TaskStoreTests
{
    private static readonly DateOnly D15 = new(2026, 11, 15);

    private static TaskItem T(long id, string title, DateOnly? date = null, DateOnly? end = null, TimeOnly? time = null, bool done = false, string? tag = null, Priority prio = Priority.None) =>
        new(id, title, string.Empty, date, end, time, 0, prio, tag, done);

    private static (TaskStore Store, RecordingPersistence P, List<TaskStoreChangedEventArgs> Events) Create(IEnumerable<TaskItem> tasks, params string[] tags)
    {
        var p = new RecordingPersistence();
        var s = new TaskStore(tasks, tags, p);
        var events = new List<TaskStoreChangedEventArgs>();
        s.Changed += (_, e) => events.Add(e);
        return (s, p, events);
    }

    [Fact]
    public void Constructor_indexes_and_does_not_persist()
    {
        var (s, p, events) = Create([
            T(3, "c", D15),
            T(1, "a", D15),
            T(2, "span", D15.AddDays(-1), D15.AddDays(1)),
            T(5, "undated"),
            T(4, "span2", D15.AddDays(-3), D15),
        ], "work");
        Assert.Equal([1L, 2, 3, 4, 5], s.All.Select(t => t.Id));
        Assert.Equal([1L, 3], s.OnDate(D15).Select(t => t.Id));
        Assert.Equal([4L, 2], s.MultiDay.Select(t => t.Id));
        Assert.Equal([5L], s.Undated.Select(t => t.Id));
        Assert.Equal(["work"], s.Tags);
        Assert.Equal(5, s.Count);
        Assert.Equal("c", s.Get(3)!.Title);
        Assert.Null(s.Get(99));
        Assert.Empty(s.OnDate(D15.AddDays(10)));
        Assert.Empty(p.Calls);
        Assert.Empty(events);
    }

    [Fact]
    public void Constructor_rejects_duplicate_ids() =>
        Assert.Throws<ArgumentException>(() => new TaskStore([T(1, "a"), T(1, "b")], [], NullTaskPersistence.Instance));

    [Fact]
    public void Add_assigns_max_plus_one_raises_once_and_persists()
    {
        var (s, p, events) = Create([T(7, "a", D15), T(3, "b")]);
        var added = s.Add(T(0, "new", D15, time: new TimeOnly(9, 0)));
        Assert.Equal(8, added.Id);
        Assert.Same(added, s.Get(8));
        Assert.Equal([7L, 8], s.OnDate(D15).Select(t => t.Id));
        Assert.Equal(["upsert 8"], p.Calls);
        var e = Assert.Single(events);
        Assert.Equal(TaskChangeKind.Added, e.Kind);
        Assert.Null(e.Old);
        Assert.Same(added, e.New);

        var (empty, _, _) = Create([]);
        Assert.Equal(1, empty.Add(T(42, "first")).Id);
    }

    [Fact]
    public void Add_never_stores_null_notes()
    {
        var (s, _, _) = Create([]);
        var added = s.Add(new TaskItem(0, "x", null!, null, null, null, 0, Priority.None, null, false));
        Assert.Equal(string.Empty, added.Notes);
    }

    [Fact]
    public void Update_moves_between_indexes()
    {
        var (s, p, events) = Create([T(1, "a", D15), T(2, "b", D15), T(3, "c")]);

        // single-day → span
        s.Update(T(1, "a", D15, D15.AddDays(2)));
        Assert.Equal([2L], s.OnDate(D15).Select(t => t.Id));
        Assert.Equal([1L], s.MultiDay.Select(t => t.Id));
        Assert.Equal([1L], s.SpansOverlapping(D15.AddDays(2), D15.AddDays(5)).Select(t => t.Id));

        // span → undated
        s.Update(T(1, "a"));
        Assert.Empty(s.MultiDay);
        Assert.Equal([1L, 3], s.Undated.Select(t => t.Id));

        // undated → another day
        s.Update(T(3, "c", D15.AddDays(1)));
        Assert.Equal([1L], s.Undated.Select(t => t.Id));
        Assert.Equal([3L], s.OnDate(D15.AddDays(1)).Select(t => t.Id));

        Assert.Equal(["upsert 1", "upsert 1", "upsert 3"], p.Calls);
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal(TaskChangeKind.Updated, e.Kind));
        Assert.Equal(D15, events[0].Old!.Date);
        Assert.Equal(D15.AddDays(2), events[0].New!.End);
        Assert.Equal([1L, 2, 3], s.All.Select(t => t.Id));
    }

    [Fact]
    public void Update_equal_or_unknown_is_a_no_op()
    {
        var (s, p, events) = Create([T(1, "a", D15)]);
        s.Update(T(1, "a", D15));
        s.Update(T(9, "missing", D15));
        s.Delete(9);
        s.ToggleDone(9);
        s.CyclePriority(9);
        s.MoveToDate(9, D15);
        Assert.Empty(p.Calls);
        Assert.Empty(events);
    }

    [Fact]
    public void Delete_removes_from_every_index()
    {
        var (s, p, events) = Create([T(1, "a", D15), T(2, "span", D15, D15.AddDays(1)), T(3, "c")]);
        s.Delete(1);
        s.Delete(2);
        s.Delete(3);
        Assert.Equal(0, s.Count);
        Assert.Empty(s.OnDate(D15));
        Assert.Empty(s.MultiDay);
        Assert.Empty(s.Undated);
        Assert.Null(s.Get(2));
        Assert.Equal(["delete 1", "delete 2", "delete 3"], p.Calls);
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal(TaskChangeKind.Removed, e.Kind));
        Assert.All(events, e => Assert.Null(e.New));
        Assert.Equal(2, events[1].Old!.Id);
    }

    [Fact]
    public void Shortcuts_are_updates()
    {
        var (s, p, events) = Create([T(1, "a", D15.AddDays(-3)), T(2, "span", D15, D15.AddDays(1)), T(3, "u", prio: Priority.Asap)]);
        s.ToggleDone(1);
        Assert.True(s.Get(1)!.Done);
        s.ToggleDone(1);
        Assert.False(s.Get(1)!.Done);
        s.CyclePriority(3);
        Assert.Equal(Priority.None, s.Get(3)!.Priority);
        s.CyclePriority(3);
        Assert.Equal(Priority.Low, s.Get(3)!.Priority);
        s.MoveToDate(1, D15);
        Assert.Equal([1L], s.OnDate(D15).Select(t => t.Id));
        Assert.Empty(s.OnDate(D15.AddDays(-3)));
        s.MoveToDate(2, D15.AddDays(5)); // spans are left alone
        Assert.Equal(D15, s.Get(2)!.Date);
        s.MoveToDate(3, D15); // an undated task gets a date
        Assert.Equal([1L, 3], s.OnDate(D15).Select(t => t.Id));
        Assert.Equal(6, events.Count);
        Assert.All(events, e => Assert.Equal(TaskChangeKind.Updated, e.Kind));
        Assert.Equal(6, p.Calls.Count);
    }

    [Fact]
    public void AddTag_normalises_and_rejects_duplicates()
    {
        var (s, p, events) = Create([], "work");
        Assert.Equal("deep-work", s.AddTag("  #Deep  Work "));
        Assert.Null(s.AddTag("WORK"));
        Assert.Null(s.AddTag("  # "));
        Assert.Equal(["work", "deep-work"], s.Tags);
        Assert.Equal(["tags work,deep-work"], p.Calls);
        var e = Assert.Single(events);
        Assert.Equal(TaskChangeKind.TagsChanged, e.Kind);
    }

    [Fact]
    public void ReplaceAll_resets_everything()
    {
        var (s, p, events) = Create([T(1, "a", D15), T(2, "b")], "work");
        s.ReplaceAll([T(10, "x", D15.AddDays(1)), T(4, "y", D15, D15.AddDays(3))], ["home"]);
        Assert.Equal([4L, 10], s.All.Select(t => t.Id));
        Assert.Empty(s.OnDate(D15));
        Assert.Empty(s.Undated);
        Assert.Equal([4L], s.MultiDay.Select(t => t.Id));
        Assert.Equal(["home"], s.Tags);
        Assert.Equal(["replace 2 home"], p.Calls);
        var e = Assert.Single(events);
        Assert.Equal(TaskChangeKind.Reset, e.Kind);
        Assert.Null(e.Old);
        Assert.Null(e.New);
        Assert.Equal(11, s.Add(T(0, "next")).Id);
    }

    [Fact]
    public void UpdateMany_replaces_every_changed_task_with_one_batch_event_and_one_write_each()
    {
        var (s, p, events) = Create([T(1, "a", D15.AddDays(-1)), T(2, "b", D15), T(3, "c"), T(4, "span", D15, D15.AddDays(2))]);
        var one = s.Get(1)! with { Date = D15 };
        var three = s.Get(3)! with { Date = D15.AddDays(1) };
        var four = s.Get(4)! with { Date = D15.AddDays(1), End = D15.AddDays(3) };

        var changed = s.UpdateMany([one, s.Get(2)!, three, four, T(99, "missing", D15)]);

        Assert.Equal(3, changed);
        Assert.Equal(["upsert 1", "upsert 3", "upsert 4"], p.Calls);
        var e = Assert.Single(events);
        Assert.Equal(TaskChangeKind.Batch, e.Kind);
        Assert.Null(e.Old);
        Assert.Null(e.New);
        Assert.Equal([1L, 2], s.OnDate(D15).Select(t => t.Id));
        Assert.Empty(s.OnDate(D15.AddDays(-1)));
        Assert.Equal([3L], s.OnDate(D15.AddDays(1)).Select(t => t.Id));
        Assert.Empty(s.Undated);
        Assert.Equal([4L], s.SpansOverlapping(D15.AddDays(3), D15.AddDays(3)).Select(t => t.Id));
        Assert.Null(s.Get(99));
    }

    [Fact]
    public void UpdateMany_without_changes_raises_nothing_and_writes_nothing()
    {
        var (s, p, events) = Create([T(1, "a", D15), T(2, "b")]);

        Assert.Equal(0, s.UpdateMany([s.Get(1)!, s.Get(2)!, T(7, "unknown")]));
        Assert.Equal(0, s.UpdateMany([]));

        Assert.Empty(p.Calls);
        Assert.Empty(events);
    }

    [Fact]
    public void Indexes_stay_consistent_under_random_mutations()
    {
        var rnd = new Random(7);
        var (s, p, events) = Create(DemoData.Tasks(), [.. DemoData.Tags]);
        var day0 = new DateOnly(2026, 1, 1);
        DateOnly? RandomDate() => rnd.Next(10) == 0 ? null : day0.AddDays(rnd.Next(400));

        for (var step = 0; step < 3000; step++)
        {
            var all = s.All;
            var pick = all.Count == 0 ? null : all[rnd.Next(all.Count)];
            var before = events.Count;
            switch (rnd.Next(7))
            {
                case 0:
                case 1:
                    var date = RandomDate();
                    DateOnly? end = date is { } d && rnd.Next(4) == 0 ? d.AddDays(1 + rnd.Next(20)) : null;
                    s.Add(T(0, "r" + step, date, end, done: rnd.Next(2) == 0));
                    break;
                case 2:
                    if (pick is not null)
                    {
                        var nd = RandomDate();
                        s.Update(pick with { Date = nd, End = nd is { } x && rnd.Next(3) == 0 ? x.AddDays(1 + rnd.Next(9)) : null });
                    }

                    break;
                case 3:
                    if (pick is not null)
                    {
                        s.Delete(pick.Id);
                    }

                    break;
                case 4:
                    if (pick is not null)
                    {
                        s.ToggleDone(pick.Id);
                    }

                    break;
                case 5:
                    if (pick is not null)
                    {
                        s.MoveToDate(pick.Id, day0.AddDays(rnd.Next(400)));
                    }

                    break;
                default:
                    if (pick is not null)
                    {
                        s.CyclePriority(pick.Id);
                    }

                    break;
            }

            Assert.True(events.Count - before <= 1, "at most one event per mutation");
            if (step % 250 == 0)
            {
                AssertConsistent(s, rnd);
            }
        }

        AssertConsistent(s, rnd);
        Assert.Equal(events.Count, p.Calls.Count);
    }

    private static void AssertConsistent(TaskStore s, Random rnd)
    {
        var all = s.All.ToList();
        Assert.Equal(all.OrderBy(t => t.Id).Select(t => t.Id), all.Select(t => t.Id));
        Assert.Equal(all.Count, s.Count);
        foreach (var t in all)
        {
            Assert.Same(t, s.Get(t.Id));
        }

        Assert.Equal(all.Where(t => t.Date is null).Select(t => t.Id), s.Undated.Select(t => t.Id));
        Assert.Equal(all.Where(t => t.Date is not null && t.End is not null).OrderBy(t => t.Date).ThenBy(t => t.Id).Select(t => t.Id), s.MultiDay.Select(t => t.Id));
        foreach (var g in all.Where(t => t.Date is not null && t.End is null).GroupBy(t => t.Date!.Value))
        {
            Assert.Equal(g.Select(t => t.Id), s.OnDate(g.Key).Select(t => t.Id));
        }

        for (var i = 0; i < 50; i++)
        {
            var from = new DateOnly(2025, 12, 1).AddDays(rnd.Next(450));
            var to = from.AddDays(rnd.Next(40));
            var expected = all.Where(t => t.Date is { } d && t.End is { } e && d <= to && e >= from).OrderBy(t => t.Date).ThenBy(t => t.Id).Select(t => t.Id);
            Assert.Equal(expected, s.SpansOverlapping(from, to).Select(t => t.Id));
        }
    }
}
