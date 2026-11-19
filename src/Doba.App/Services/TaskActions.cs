using Doba.App.Platform;
using Doba.Core.Editing;
using Doba.Core.Store;
using Doba.Core.Time;

namespace Doba.App.Services;

/// <summary>
/// The row-level task commands shared by every screen (checkbox, priority icon, "→ Today"). Mutates the store, plays
/// the completion sound when enabled, and remembers the last created/moved task so its row can play the new-row
/// entrance (motion spec: opacity 0→1, translateY −8→0, only for newly created or "→ Today" tasks).
/// </summary>
public sealed class TaskActions(ITaskStore store, ISettingsService settings, IShellIntegration shell, TimeProvider clock)
{
    /// <summary>Id of the task that should play the new-row entrance next time its row appears; consumed by the view.</summary>
    public long? PendingEntranceId { get; private set; }

    public void ToggleDone(long id)
    {
        var before = store.Get(id);
        store.ToggleDone(id);
        if (before is { Done: false } && settings.Current.CompletionSound)
        {
            shell.PlayCompletionSound();
        }
    }

    public void CyclePriority(long id) => store.CyclePriority(id);

    public void MoveToToday(long id)
    {
        PendingEntranceId = id;
        store.MoveToDate(id, clock.Today());
    }

    /// <summary>
    /// Writes a schedule change of the task <paramref name="id"/> (an arrow-key step or a drop) as ONE store update, in
    /// place: the start moves to <paramref name="slot"/>; a <see cref="ScheduleDragMode.Resize"/> also writes its length,
    /// a move keeps the task's own (so an unspecified duration stays unspecified). Returns false, writing nothing, when
    /// the task is gone, untimed, or already at that slot.
    /// </summary>
    public bool Reschedule(long id, ScheduleDragMode mode, ScheduleSlot slot)
    {
        if (store.Get(id) is not { } task || ScheduleEdit.Apply(task, mode, slot) is not { } changed)
        {
            return false;
        }

        store.Update(changed);
        return true;
    }

    /// <summary>Called by the dialog after creating a task.</summary>
    public void MarkCreated(long id) => PendingEntranceId = id;

    /// <summary>Returns and clears <see cref="PendingEntranceId"/> if it equals <paramref name="id"/>.</summary>
    public bool ConsumeEntrance(long id)
    {
        if (PendingEntranceId != id)
        {
            return false;
        }

        PendingEntranceId = null;
        return true;
    }
}
