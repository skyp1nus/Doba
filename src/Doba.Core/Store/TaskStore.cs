using Doba.Core.Model;

namespace Doba.Core.Store;

/// <summary>
/// Default <see cref="ITaskStore"/> with per-date and span indexes so projections stay under 5 ms at 10k tasks.
/// UI thread only: no locking. Lists returned by the read side are live views of the indexes; enumerate them
/// before the next mutation and do not keep them.
/// </summary>
public sealed class TaskStore : ITaskStore
{
    private static readonly Comparison<TaskItem> s_byDateThenId = (x, y) =>
    {
        var c = x.Date!.Value.CompareTo(y.Date!.Value);
        return c != 0 ? c : x.Id.CompareTo(y.Id);
    };

    private static readonly Comparer<TaskItem> s_spanComparer = Comparer<TaskItem>.Create(s_byDateThenId);

    private readonly ITaskPersistence _persistence;
    private readonly List<TaskItem> _all = [];
    private readonly Dictionary<long, TaskItem> _byId = [];
    private readonly Dictionary<DateOnly, List<TaskItem>> _byDate = [];
    private readonly List<TaskItem> _multi = [];
    private readonly List<TaskItem> _undated = [];
    private readonly List<string> _tags = [];

    // Longest span (End − Date) ever indexed since the last rebuild; bounds the backwards scan in SpansOverlapping.
    private int _maxSpanDays;

    public TaskStore(IEnumerable<TaskItem> tasks, IEnumerable<string> tags, ITaskPersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(persistence);
        _persistence = persistence;
        Load(tasks, tags);
    }

    public event EventHandler<TaskStoreChangedEventArgs>? Changed;

    public int Count => _all.Count;

    public IReadOnlyList<TaskItem> All => _all;

    public IReadOnlyList<TaskItem> MultiDay => _multi;

    public IReadOnlyList<TaskItem> Undated => _undated;

    public IReadOnlyList<string> Tags => _tags;

    public TaskItem? Get(long id) => _byId.GetValueOrDefault(id);

    public IReadOnlyList<TaskItem> OnDate(DateOnly date) => _byDate.TryGetValue(date, out var list) ? list : [];

    public IReadOnlyList<TaskItem> SpansOverlapping(DateOnly from, DateOnly to)
    {
        var result = new List<TaskItem>();
        if (_multi.Count == 0 || to < from)
        {
            return result;
        }

        // Every span overlapping [from, to] starts in [from − maxSpan, to].
        var i = LowerBound(_multi, from.AddDays(-_maxSpanDays));
        for (; i < _multi.Count; i++)
        {
            var t = _multi[i];
            if (t.Date!.Value > to)
            {
                break;
            }

            if (t.End!.Value >= from)
            {
                result.Add(t);
            }
        }

        return result;
    }

    public TaskItem Add(TaskItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var id = _all.Count == 0 ? 1 : _all[^1].Id + 1;
        var stored = Sanitize(item) with { Id = id };
        _all.Add(stored);
        _byId.Add(id, stored);
        IndexAdd(stored);
        _persistence.Upsert(stored);
        Raise(TaskChangeKind.Added, null, stored);
        return stored;
    }

    public void Update(TaskItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_byId.TryGetValue(item.Id, out var old))
        {
            return;
        }

        if (Replace(old, item) is { } stored)
        {
            Raise(TaskChangeKind.Updated, old, stored);
        }
    }

    public int UpdateMany(IReadOnlyList<TaskItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var changed = 0;
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!_byId.TryGetValue(item.Id, out var old))
            {
                continue;
            }

            if (Replace(old, item) is not null)
            {
                changed++;
            }
        }

        if (changed > 0)
        {
            Raise(TaskChangeKind.Batch, null, null);
        }

        return changed;
    }

    public void Delete(long id)
    {
        if (!_byId.TryGetValue(id, out var old))
        {
            return;
        }

        IndexRemove(old);
        _all.RemoveAt(IndexOfId(id));
        _byId.Remove(id);
        _persistence.Delete(id);
        Raise(TaskChangeKind.Removed, old, null);
    }

    public void ToggleDone(long id)
    {
        if (_byId.TryGetValue(id, out var t))
        {
            Update(t with { Done = !t.Done });
        }
    }

    public void CyclePriority(long id)
    {
        if (_byId.TryGetValue(id, out var t))
        {
            Update(t with { Priority = t.Priority.Next() });
        }
    }

    /// <summary>
    /// "→ Today": moves a single-day (or undated) task to <paramref name="date"/>. Multi-day tasks are left alone.
    /// </summary>
    public void MoveToDate(long id, DateOnly date)
    {
        if (_byId.TryGetValue(id, out var t) && !t.IsMultiDay)
        {
            Update(t with { Date = date });
        }
    }

    public string? AddTag(string raw)
    {
        var name = Model.Tags.Normalize(raw);
        if (name is null || _tags.Contains(name))
        {
            return null;
        }

        _tags.Add(name);
        _persistence.SaveTags(_tags.ToArray());
        Raise(TaskChangeKind.TagsChanged, null, null);
        return name;
    }

    public void ReplaceAll(IEnumerable<TaskItem> tasks, IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(tags);
        Load(tasks, tags);
        _persistence.ReplaceAll(_all.ToArray(), _tags.ToArray());
        Raise(TaskChangeKind.Reset, null, null);
    }

    private void Load(IEnumerable<TaskItem> tasks, IEnumerable<string> tags)
    {
        var items = tasks.Select(Sanitize).ToList();
        items.Sort(static (x, y) => x.Id.CompareTo(y.Id));
        for (var i = 1; i < items.Count; i++)
        {
            if (items[i].Id == items[i - 1].Id)
            {
                throw new ArgumentException($"Duplicate task id {items[i].Id}.", nameof(tasks));
            }
        }

        var tagList = new List<string>();
        foreach (var tag in tags)
        {
            if (!string.IsNullOrEmpty(tag) && !tagList.Contains(tag))
            {
                tagList.Add(tag);
            }
        }

        _all.Clear();
        _byId.Clear();
        _byDate.Clear();
        _multi.Clear();
        _undated.Clear();
        _tags.Clear();
        _maxSpanDays = 0;

        _all.AddRange(items);
        _tags.AddRange(tagList);
        foreach (var t in items)
        {
            _byId.Add(t.Id, t);
            if (t.Date is null)
            {
                _undated.Add(t);
            }
            else if (t.End is not null)
            {
                _multi.Add(t);
                _maxSpanDays = Math.Max(_maxSpanDays, t.End.Value.DayNumber - t.Date.Value.DayNumber);
            }
            else
            {
                if (!_byDate.TryGetValue(t.Date.Value, out var list))
                {
                    _byDate[t.Date.Value] = list = [];
                }

                list.Add(t);
            }
        }

        _multi.Sort(s_byDateThenId);
    }

    /// <summary>Swaps <paramref name="old"/> for <paramref name="item"/> in every index and persists it. Returns the
    /// stored item, or null (nothing done) when it equals <paramref name="old"/>. Raises no event.</summary>
    private TaskItem? Replace(TaskItem old, TaskItem item)
    {
        var stored = Sanitize(item);
        if (stored.Equals(old))
        {
            return null;
        }

        IndexRemove(old);
        _all[IndexOfId(old.Id)] = stored;
        _byId[old.Id] = stored;
        IndexAdd(stored);
        _persistence.Upsert(stored);
        return stored;
    }

    /// <summary>Undated tasks cannot be spans; Notes is never null.</summary>
    private static TaskItem Sanitize(TaskItem t)
    {
        if (t.Date is null && t.End is not null)
        {
            t = t with { End = null };
        }

        // Records can carry a null into a non-nullable string from deserialisation.
        return t.Notes is null ? t with { Notes = string.Empty } : t;
    }

    private void IndexAdd(TaskItem t)
    {
        if (t.Date is null)
        {
            InsertById(_undated, t);
        }
        else if (t.End is not null)
        {
            var i = _multi.BinarySearch(t, s_spanComparer);
            _multi.Insert(i < 0 ? ~i : i, t);
            _maxSpanDays = Math.Max(_maxSpanDays, t.End.Value.DayNumber - t.Date.Value.DayNumber);
        }
        else
        {
            if (!_byDate.TryGetValue(t.Date.Value, out var list))
            {
                _byDate[t.Date.Value] = list = [];
            }

            InsertById(list, t);
        }
    }

    private void IndexRemove(TaskItem t)
    {
        if (t.Date is null)
        {
            _undated.RemoveAt(IndexOfId(_undated, t.Id));
        }
        else if (t.End is not null)
        {
            var i = _multi.BinarySearch(t, s_spanComparer);
            if (i < 0)
            {
                throw new InvalidOperationException($"Task {t.Id} is missing from the span index.");
            }

            _multi.RemoveAt(i);
        }
        else
        {
            var list = _byDate[t.Date.Value];
            list.RemoveAt(IndexOfId(list, t.Id));
            if (list.Count == 0)
            {
                _byDate.Remove(t.Date.Value);
            }
        }
    }

    private int IndexOfId(long id) => IndexOfId(_all, id);

    private static int IndexOfId(List<TaskItem> list, long id)
    {
        int lo = 0, hi = list.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var c = list[mid].Id.CompareTo(id);
            if (c == 0)
            {
                return mid;
            }

            if (c < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        throw new InvalidOperationException($"Task {id} is missing from an index.");
    }

    private static void InsertById(List<TaskItem> list, TaskItem t)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (list[mid].Id < t.Id)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        list.Insert(lo, t);
    }

    /// <summary>First index whose Date is ≥ <paramref name="date"/> in a list sorted by Date.</summary>
    private static int LowerBound(List<TaskItem> list, DateOnly date)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (list[mid].Date!.Value < date)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private void Raise(TaskChangeKind kind, TaskItem? oldItem, TaskItem? newItem) => Changed?.Invoke(this, new TaskStoreChangedEventArgs(kind, oldItem, newItem));
}
