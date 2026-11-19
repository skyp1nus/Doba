using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Doba.Core.Navigation;

namespace Doba.App.Shell;

/// <summary>
/// Navigation and chrome state the shell exposes to the sidebar, the page header and the keyboard handler.
/// Implemented by <c>MainViewModel</c>. Every member is called on the UI thread. Navigation rules themselves live in
/// <see cref="Navigator"/> (Core); implementations only apply its <see cref="NavResult"/>, raise
/// <see cref="Navigated"/> and never decide "where to go" on their own.
/// <para><see cref="Nav"/>, <see cref="IsSidebarCollapsed"/> and <see cref="FilterTag"/> raise
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> with their own names, so the sidebar and header may bind to
/// them or subscribe; test doubles must raise it too.</para>
/// </summary>
public interface IShellNavigation : INotifyPropertyChanged
{
    /// <summary>The current navigation state. Changes only through the methods below; each change raises
    /// <see cref="Navigated"/> exactly once (never for a <see cref="TransitionKind.None"/> result).</summary>
    NavState Nav { get; }

    /// <summary>Sidebar Inbox / Today (<see cref="Navigator.GoSection"/>): section transition, None if already there.
    /// The anchor is today for Today and the current anchor otherwise.</summary>
    void GoSection(AppView view);

    /// <summary>Sidebar Calendar (<see cref="Navigator.GoCalendar"/>): Week from Inbox/Today/Settings/Day, no-op inside
    /// Year/Month/Week.</summary>
    void GoCalendar();

    /// <summary>Header Year / Month / Week / Day switch: zoom to <paramref name="level"/> at the current anchor
    /// (<see cref="Navigator.Zoom"/>, no click origin). Only calendar levels are valid.</summary>
    void SetZoomLevel(AppView level);

    /// <summary>Header ‹ / › and the arrow keys: <paramref name="direction"/> is −1 or +1
    /// (<see cref="Navigator.Shift"/>). No-op outside the calendar.</summary>
    void Shift(int direction);

    /// <summary>Header "Today" button (<see cref="Navigator.GoToday"/>).</summary>
    void GoToday();

    /// <summary>Sidebar Settings button (<see cref="Navigator.OpenSettings"/>); remembers where it was opened from.</summary>
    void OpenSettings();

    /// <summary>Settings "Back to app" (<see cref="Navigator.BackFromSettings"/>).</summary>
    void BackFromSettings();

    /// <summary>Settings sidebar tab (<see cref="Navigator.SetTab"/>): Next/Prev by list order, None if unchanged.</summary>
    void SetSettingsTab(SettingsTab tab);

    /// <summary>Sidebar collapsed to the rail. In-memory only: never persisted to settings.</summary>
    bool IsSidebarCollapsed { get; }

    /// <summary>Collapses / expands the sidebar (sidebar Collapse button, page-header toggle). Raises PropertyChanged, not
    /// <see cref="Navigated"/>.</summary>
    void ToggleSidebar();

    /// <summary>The sidebar tag filter (normalised tag name), null = no filter. Screens read it through
    /// <c>ScreenServices.FilterTag</c> and dim other tags; the active screen re-projects when it changes.</summary>
    string? FilterTag { get; }

    /// <summary>Sidebar tag click: sets the filter to <paramref name="tag"/>, or clears it when that tag is already
    /// the filter.</summary>
    void ToggleTagFilter(string tag);

    /// <summary>Sidebar "+ New tag": normalises <paramref name="raw"/> with <c>Tags.Normalize</c> and adds it to the
    /// store. Returns the normalised name, or null when it is rejected (empty after normalising, or already exists).</summary>
    string? AddTag(string raw);

    /// <summary>Opens the search palette overlay (Ctrl+K, title-bar search box).</summary>
    void OpenSearch();

    /// <summary>Raised after <see cref="Nav"/> changed, before the content swap is animated; the shell's view layer
    /// drives <see cref="IContentTransitions.Run"/> from it.</summary>
    event EventHandler<NavigatedEventArgs>? Navigated;
}

/// <summary>One navigation step: what changed and which transition to play.</summary>
public sealed class NavigatedEventArgs : EventArgs
{
    public NavigatedEventArgs(NavState from, NavState to, TransitionKind kind, Point? origin)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        To = to;
        Kind = kind;
        Origin = origin;
    }

    /// <summary>The state before the step.</summary>
    public NavState From { get; }

    /// <summary>The state after the step (equals the shell's current <see cref="IShellNavigation.Nav"/>).</summary>
    public NavState To { get; }

    /// <summary>The transition to play; never <see cref="TransitionKind.None"/>.</summary>
    public TransitionKind Kind { get; }

    /// <summary>Centre of the clicked element as fractions (0..1, 0..1) of the content area; null = no click
    /// (keyboard, header buttons), in which case zooms use the motion spec's default origin.</summary>
    public Point? Origin { get; }
}

/// <summary>
/// The controls a content transition animates. Supplied by the shell's view layer (MainWindow); a transition touches
/// only Opacity and render transforms of these, never layout properties.
/// </summary>
/// <param name="Content">The screen host (the main panel's body).</param>
/// <param name="Title">The page header's h1 (title + subtitle); it plays its own entrance on every navigation.</param>
/// <param name="AppSidebar">The app sidebar, animated on <see cref="TransitionKind.SettingsOut"/>; null when absent.</param>
/// <param name="SettingsSidebar">The settings sidebar, animated on <see cref="TransitionKind.SettingsIn"/>; null when absent.</param>
public sealed record ContentTransitionTargets(Control Content, Control Title, Control? AppSidebar, Control? SettingsSidebar);

/// <summary>
/// Plays the motion spec's navigation transitions ("Навігаційні переходи") around a content swap. Implemented by the
/// <c>Motion.ContentTransitions</c> (the default in <see cref="ShellComposition.Transitions"/>); <see cref="InstantContentTransitions"/> is the test double.
/// </summary>
/// <remarks>
/// Rules every implementation keeps:
/// <list type="bullet">
/// <item><see cref="Run"/> invokes <c>swap</c> exactly once. For <see cref="TransitionKind.SectionDown"/> /
/// <see cref="TransitionKind.SectionUp"/> it is invoked only after the exit phase has finished; for every other kind
/// it is invoked before the enter animation starts.</item>
/// <item>A new <see cref="Run"/> (or <see cref="Launch"/>) cancels the running one: a pending swap is invoked at once
/// and the cancelled animation is reset, so swaps are never lost or reordered.</item>
/// <item>Content is never left invisible: after cancellation, completion, or with animations off
/// (<c>Motion.SystemAnimationsEnabled == false</c>, all durations 0) every target ends at Opacity 1 with an identity
/// render transform.</item>
/// </list>
/// </remarks>
public interface IContentTransitions
{
    /// <summary>Animates the step <paramref name="kind"/> and calls <paramref name="swap"/> (which replaces the
    /// visible screen) exactly once at the right moment. <paramref name="origin"/>: zoom origin as 0..1 fractions of the
    /// content area, null = the spec's default. <see cref="TransitionKind.None"/> just swaps.</summary>
    void Run(TransitionKind kind, Point? origin, Action swap, ContentTransitionTargets targets);

    /// <summary>The app-launch entrance (window content fades/rises in once after the first frame).</summary>
    void Launch(ContentTransitionTargets targets);
}

/// <summary>No animation: <see cref="Run"/> swaps immediately, <see cref="Launch"/> does nothing. Used by tests
/// and wherever motion must be off; the app's default is <c>Motion.ContentTransitions</c>.</summary>
public sealed class InstantContentTransitions : IContentTransitions
{
    /// <inheritdoc />
    public void Run(TransitionKind kind, Point? origin, Action swap, ContentTransitionTargets targets)
    {
        ArgumentNullException.ThrowIfNull(swap);
        swap();
    }

    /// <inheritdoc />
    public void Launch(ContentTransitionTargets targets)
    {
    }
}
