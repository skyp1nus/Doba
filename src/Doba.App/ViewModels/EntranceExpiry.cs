using Avalonia.Threading;
using Doba.App.Services;
using Doba.Core.Store;
using M = Doba.App.Motion.Motion;

namespace Doba.App.ViewModels;

/// <summary>
/// Makes <see cref="TaskActions.PendingEntranceId"/> short-lived for the list screens (Inbox, Today). The motion spec
/// plays the new-row entrance only when the new or "→ Today" task appears; a row that is realised much later (a task
/// created on Year or Month, where nothing consumes the entrance, and the list visited minutes afterwards; a new row
/// outside a long group's viewport, realised when scrolled to) must not play it.
/// </summary>
/// <remarks>
/// After every store change — once the synchronous work around it is done, so an id the dialog marks right after
/// creating the task is seen — the pending id gets one <see cref="M.NewRow"/> window to be consumed by a row; then it
/// is dropped. <see cref="ExpireNow"/> drops it at once (a list screen's first projection: nothing on it is new).
/// </remarks>
public sealed class EntranceExpiry : IDisposable
{
    private static readonly TimeSpan s_window = TimeSpan.FromMilliseconds(M.NewRow);

    private readonly ITaskStore _store;
    private readonly TaskActions _actions;

    public EntranceExpiry(ITaskStore store, TaskActions actions)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(actions);
        _store = store;
        _actions = actions;
        _store.Changed += OnStoreChanged;
    }

    /// <summary>Drops the pending entrance, if any.</summary>
    public void ExpireNow()
    {
        if (_actions.PendingEntranceId is { } id)
        {
            _actions.ConsumeEntrance(id);
        }
    }

    public void Dispose() => _store.Changed -= OnStoreChanged;

    private void OnStoreChanged(object? sender, TaskStoreChangedEventArgs e) =>
        Dispatcher.UIThread.Post(Arm, DispatcherPriority.Background);

    private void Arm()
    {
        if (_actions.PendingEntranceId is not { } id)
        {
            return;
        }

        DispatcherTimer.RunOnce(() =>
        {
            if (_actions.PendingEntranceId == id)
            {
                _actions.ConsumeEntrance(id);
            }
        }, s_window);
    }
}
