using Doba.App.Platform;
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
