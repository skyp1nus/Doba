using Doba.Core.Projections;

namespace Doba.Core.Navigation;

/// <summary>Top-level screens. Year..Day are the calendar zoom levels in this order.</summary>
public enum AppView
{
    Inbox,
    Today,
    Year,
    Month,
    Week,
    Day,
    Settings,
}

public enum SettingsTab
{
    Appearance,
    Calendar,
    Tasks,
    Windows,
    Data,
    Shortcuts,
}

/// <summary>Which content transition to play (motion spec, "Навігаційні переходи").</summary>
public enum TransitionKind
{
    /// <summary>Nothing changed.</summary>
    None,
    /// <summary>Deeper into the calendar: scale 0.86→1 from the click point, 460 ms.</summary>
    ZoomIn,
    /// <summary>Out of the calendar: scale 1.10→1, 460 ms.</summary>
    ZoomOut,
    /// <summary>Next period / tab: translateX +32→0, 340 ms.</summary>
    Next,
    /// <summary>Previous period / tab: translateX −32→0, 340 ms.</summary>
    Prev,
    /// <summary>Sidebar section with a higher index: exit up, enter from below (translateY 18→0), 130 + 380 ms.</summary>
    SectionDown,
    /// <summary>Sidebar section with a lower index: enter from above.</summary>
    SectionUp,
    /// <summary>Into Settings: zoom-in at 50% 30% + settings sidebar slides in from +16 px.</summary>
    SettingsIn,
    /// <summary>Back to the app: zoom-out at 50% 30% + app sidebar slides in from −16 px.</summary>
    SettingsOut,
}

/// <summary>Navigation state. <see cref="Back"/> remembers where Settings was opened from.</summary>
public sealed record NavState(AppView View, DateOnly Anchor, SettingsTab Tab = SettingsTab.Appearance, NavState? Back = null)
{
    public bool IsCalendar => View is AppView.Year or AppView.Month or AppView.Week or AppView.Day;

    /// <summary>Sidebar section index: Inbox 0, Today 1, Calendar 2, Settings 3.</summary>
    public int SectionIndex => View switch
    {
        AppView.Inbox => 0,
        AppView.Today => 1,
        AppView.Settings => 3,
        _ => 2,
    };
}

/// <summary>Result of a navigation command.</summary>
public readonly record struct NavResult(NavState State, TransitionKind Transition);

/// <summary>Pure navigation rules ported from the prototype (zoom, shift, goToday, navSection, goCalendar, settings).</summary>
public static class Navigator
{
    /// <summary>Calendar zoom to <paramref name="view"/> at <paramref name="anchor"/>: ZoomIn if deeper, ZoomOut if higher, Next if same level.</summary>
    public static NavResult Zoom(NavState s, AppView view, DateOnly anchor) => throw new NotImplementedException();

    /// <summary>‹ / ›: year ±1, month ±1, week ±7 days, day ±1. For year/month, landing in today's month snaps the anchor to today. No-op outside the calendar.</summary>
    public static NavResult Shift(NavState s, int direction, DateOnly today) => throw new NotImplementedException();

    /// <summary>"Today" button: from Inbox/Today/Settings go to Week (ZoomIn); in the calendar jump to today on the same level (Prev if the anchor is after today, else Next).</summary>
    public static NavResult GoToday(NavState s, DateOnly today) => throw new NotImplementedException();

    /// <summary>Sidebar Inbox / Today: section transition (SectionDown when the target index is ≥ the current one, else SectionUp). None if already there.</summary>
    public static NavResult GoSection(NavState s, AppView view, DateOnly anchor) => throw new NotImplementedException();

    /// <summary>Sidebar Calendar: from Inbox/Today → Week (section transition); from Settings → Week ZoomIn; from Day → Week ZoomOut; else None.</summary>
    public static NavResult GoCalendar(NavState s) => throw new NotImplementedException();

    public static NavResult OpenSettings(NavState s) => throw new NotImplementedException();

    public static NavResult BackFromSettings(NavState s, DateOnly today) => throw new NotImplementedException();

    /// <summary>Settings tab switch: Next if the new tab is lower in the list, else Prev; None if unchanged.</summary>
    public static NavResult SetTab(NavState s, SettingsTab tab) => throw new NotImplementedException();
}

/// <summary>Breadcrumb item; <see cref="Target"/> null = plain text.</summary>
public sealed record Crumb(string Label, NavTarget? Target);

/// <summary>Page header: breadcrumbs, title and the muted subtitle.</summary>
public sealed record HeaderModel(IReadOnlyList<Crumb> Crumbs, string Title, string Subtitle);

public static class HeaderProjection
{
    /// <summary>
    /// Inbox: "ALL TASKS" / "Inbox". Today: "Sunday · Today" / "15 November". Year: "Calendar" / "2026".
    /// Month: [2026] / "November". Week: [2026, November] / "Week 46" + "9 – 15 Nov". Day: [2026, November, Week 46] / "15 November" + "Sunday · Today".
    /// Settings: "Settings" / tab title.
    /// </summary>
    public static HeaderModel Build(NavState s, DateOnly today, DayOfWeek weekStart) => throw new NotImplementedException();
}
