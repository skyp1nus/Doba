using Doba.Core.Model;

namespace Doba.Core.Store;

/// <summary>Default <see cref="ITaskStore"/> with per-date and span indexes so projections stay under 5 ms at 10k tasks.</summary>
public sealed class TaskStore : ITaskStore
{
    public TaskStore(IEnumerable<TaskItem> tasks, IEnumerable<string> tags, ITaskPersistence persistence) => throw new NotImplementedException();

    public event EventHandler<TaskStoreChangedEventArgs>? Changed;

    public int Count => throw new NotImplementedException();
    public IReadOnlyList<TaskItem> All => throw new NotImplementedException();
    public IReadOnlyList<TaskItem> MultiDay => throw new NotImplementedException();
    public IReadOnlyList<TaskItem> Undated => throw new NotImplementedException();
    public IReadOnlyList<string> Tags => throw new NotImplementedException();

    public TaskItem? Get(long id) => throw new NotImplementedException();
    public IReadOnlyList<TaskItem> OnDate(DateOnly date) => throw new NotImplementedException();
    public IReadOnlyList<TaskItem> SpansOverlapping(DateOnly from, DateOnly to) => throw new NotImplementedException();
    public TaskItem Add(TaskItem item) => throw new NotImplementedException();
    public void Update(TaskItem item) => throw new NotImplementedException();
    public void Delete(long id) => throw new NotImplementedException();
    public void ToggleDone(long id) => throw new NotImplementedException();
    public void CyclePriority(long id) => throw new NotImplementedException();
    public void MoveToDate(long id, DateOnly date) => throw new NotImplementedException();
    public string? AddTag(string raw) => throw new NotImplementedException();
    public void ReplaceAll(IEnumerable<TaskItem> tasks, IEnumerable<string> tags) => throw new NotImplementedException();

    private void Raise(TaskChangeKind kind, TaskItem? oldItem, TaskItem? newItem) => Changed?.Invoke(this, new TaskStoreChangedEventArgs(kind, oldItem, newItem));
}
