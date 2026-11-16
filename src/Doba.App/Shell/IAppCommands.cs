using Avalonia;
using Doba.Core.Navigation;

namespace Doba.App.Shell;

/// <summary>
/// What a screen may ask of the shell. Implemented by <c>MainViewModel</c>; screens never navigate or open dialogs
/// themselves. All calls happen on the UI thread.
/// </summary>
public interface IAppCommands
{
    /// <summary>Calendar zoom (day/week/month label, "+N more", "Later", group links). <paramref name="origin"/> is the
    /// clicked element's centre relative to the content area (0..1, 0..1); null = keyboard / no click (50% 40%).</summary>
    void ZoomTo(AppView view, DateOnly anchor, Point? origin = null);

    /// <summary>Opens the New task dialog. <paramref name="hasExplicitDate"/> = an "Add task" row that fixes the date
    /// (Inbox → null, Today → today, Day → anchor); false = the view's default (the N key / New task button).</summary>
    void NewTask(DateOnly? date = null, bool hasExplicitDate = false);

    /// <summary>Opens the Edit task dialog for a task (clicking its title).</summary>
    void EditTask(long id);
}
