using Doba.Core.Model;

namespace Doba.Core.Store;

public enum TaskChangeKind
{
    Added,
    Updated,
    Removed,
    /// <summary>Everything replaced (import, initial load). Old/New are null.</summary>
    Reset,
    TagsChanged,
    /// <summary>Several tasks updated in one step (carry-over). Old/New are null; re-read the store.</summary>
    Batch,
}

/// <summary>One change to the store. Old is null for Added, New is null for Removed.</summary>
public sealed class TaskStoreChangedEventArgs(TaskChangeKind kind, TaskItem? oldItem, TaskItem? newItem) : EventArgs
{
    public TaskChangeKind Kind { get; } = kind;
    public TaskItem? Old { get; } = oldItem;
    public TaskItem? New { get; } = newItem;
}

/// <summary>Read side of the in-memory task store. All views read from here; nothing reads the database.</summary>
public interface ITaskReader
{
    /// <summary>Number of tasks.</summary>
    int Count { get; }

    /// <summary>All tasks in insertion (Id) order.</summary>
    IReadOnlyList<TaskItem> All { get; }

    TaskItem? Get(long id);

    /// <summary>Single-day tasks (End == null) whose Date is <paramref name="date"/>, in Id order. O(1) lookup.</summary>
    IReadOnlyList<TaskItem> OnDate(DateOnly date);

    /// <summary>All multi-day tasks, sorted by Date then Id.</summary>
    IReadOnlyList<TaskItem> MultiDay { get; }

    /// <summary>Multi-day tasks overlapping [from, to] inclusive, sorted by Date then Id.</summary>
    IReadOnlyList<TaskItem> SpansOverlapping(DateOnly from, DateOnly to);

    /// <summary>Undated tasks in Id order.</summary>
    IReadOnlyList<TaskItem> Undated { get; }

    /// <summary>Tag names in creation order.</summary>
    IReadOnlyList<string> Tags { get; }
}

/// <summary>
/// The in-memory task store: loaded once at startup, mutated on the UI thread, persisted asynchronously
/// through <see cref="ITaskPersistence"/>. Every mutation raises exactly one <see cref="Changed"/> event.
/// </summary>
public interface ITaskStore : ITaskReader
{
    event EventHandler<TaskStoreChangedEventArgs>? Changed;

    /// <summary>Adds a task; the Id of <paramref name="item"/> is ignored and a new one assigned. Returns the stored item.</summary>
    TaskItem Add(TaskItem item);

    /// <summary>Replaces the task with the same Id. No-op (no event) if equal to the stored one.</summary>
    void Update(TaskItem item);

    void Delete(long id);

    void ToggleDone(long id);

    void CyclePriority(long id);

    /// <summary>"→ Today": moves a single-day task to <paramref name="date"/>.</summary>
    void MoveToDate(long id, DateOnly date);

    /// <summary>
    /// Replaces every task in <paramref name="items"/> that exists and differs from the stored one, persisting each,
    /// and raises ONE <see cref="TaskChangeKind.Batch"/> event if anything changed (none otherwise).
    /// Returns the number of tasks changed.
    /// </summary>
    int UpdateMany(IReadOnlyList<TaskItem> items);

    /// <summary>Adds a tag after <see cref="Model.Tags.Normalize"/>. Returns the stored name, or null if empty or a duplicate.</summary>
    string? AddTag(string raw);

    /// <summary>Replaces everything (import). Ids are kept; the next Id continues after the largest.</summary>
    void ReplaceAll(IEnumerable<TaskItem> tasks, IEnumerable<string> tags);
}

/// <summary>Write-behind persistence used by the store. Implemented by Doba.Storage; calls must not block on disk I/O.</summary>
public interface ITaskPersistence
{
    void Upsert(TaskItem item);

    void Delete(long id);

    void SaveTags(IReadOnlyList<string> tags);

    void ReplaceAll(IReadOnlyList<TaskItem> tasks, IReadOnlyList<string> tags);

    /// <summary>Completes when every queued write has reached disk.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>No-op persistence for demo mode and tests.</summary>
public sealed class NullTaskPersistence : ITaskPersistence
{
    public static NullTaskPersistence Instance { get; } = new();

    public void Upsert(TaskItem item) { }

    public void Delete(long id) { }

    public void SaveTags(IReadOnlyList<string> tags) { }

    public void ReplaceAll(IReadOnlyList<TaskItem> tasks, IReadOnlyList<string> tags) { }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
