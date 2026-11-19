using System.Globalization;
using Doba.Core.Projections;
using Doba.Core.Time;

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
    public static NavResult Zoom(NavState s, AppView view, DateOnly anchor)
    {
        ArgumentNullException.ThrowIfNull(s);
        int from = Level(s.View), to = Level(view);
        var kind = to > from ? TransitionKind.ZoomIn : (to < from ? TransitionKind.ZoomOut : TransitionKind.Next);
        return new NavResult(Go(s, view, anchor), kind);
    }

    /// <summary>‹ / ›: year ±1, month ±1, week ±7 days, day ±1. For year/month, landing in today's month snaps the anchor to today. No-op outside the calendar.</summary>
    public static NavResult Shift(NavState s, int direction, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!s.IsCalendar || direction == 0)
        {
            return new NavResult(s, TransitionKind.None);
        }

        var a = s.Anchor;
        var n = s.View switch
        {
            AppView.Year => new DateOnly(a.Year + direction, a.Month, 1),
            AppView.Month => new DateOnly(a.Year, a.Month, 1).AddMonths(direction),
            AppView.Week => a.AddDays(7 * direction),
            _ => a.AddDays(direction),
        };

        if (s.View is AppView.Year or AppView.Month && n.Year == today.Year && n.Month == today.Month)
        {
            n = today;
        }

        return new NavResult(Go(s, s.View, n), direction > 0 ? TransitionKind.Next : TransitionKind.Prev);
    }

    /// <summary>"Today" button: from Inbox/Today/Settings go to Week (ZoomIn); in the calendar jump to today on the same level (Prev if the anchor is after today, else Next).</summary>
    public static NavResult GoToday(NavState s, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!s.IsCalendar)
        {
            return new NavResult(Go(s, AppView.Week, today), TransitionKind.ZoomIn);
        }

        return new NavResult(Go(s, s.View, today), s.Anchor > today ? TransitionKind.Prev : TransitionKind.Next);
    }

    /// <summary>Sidebar Inbox / Today: section transition (SectionDown when the target index is ≥ the current one, else SectionUp). None if already there.</summary>
    public static NavResult GoSection(NavState s, AppView view, DateOnly anchor)
    {
        ArgumentNullException.ThrowIfNull(s);
        var target = Go(s, view, anchor);
        int from = s.SectionIndex, to = target.SectionIndex;
        if (from == to && s.View == view)
        {
            return new NavResult(s, TransitionKind.None);
        }

        return new NavResult(target, to >= from ? TransitionKind.SectionDown : TransitionKind.SectionUp);
    }

    /// <summary>Sidebar Calendar: from Inbox/Today → Week (section transition); from Settings → Week ZoomIn; from Day → Week ZoomOut; else None.</summary>
    public static NavResult GoCalendar(NavState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.View switch
        {
            AppView.Inbox or AppView.Today => GoSection(s, AppView.Week, s.Anchor),
            AppView.Settings => new NavResult(Go(s, AppView.Week, s.Anchor), TransitionKind.ZoomIn),
            AppView.Day => new NavResult(Go(s, AppView.Week, s.Anchor), TransitionKind.ZoomOut),
            _ => new NavResult(s, TransitionKind.None),
        };
    }

    /// <summary>Opens Settings and remembers where it was opened from in <see cref="NavState.Back"/>. None if already open.</summary>
    public static NavResult OpenSettings(NavState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.View == AppView.Settings)
        {
            return new NavResult(s, TransitionKind.None);
        }

        var back = s with { Back = null };
        return new NavResult(s with { View = AppView.Settings, Back = back }, TransitionKind.SettingsIn);
    }

    /// <summary>Returns to where Settings was opened from (Week at today if unknown). None when not in Settings.</summary>
    public static NavResult BackFromSettings(NavState s, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.View != AppView.Settings)
        {
            return new NavResult(s, TransitionKind.None);
        }

        var b = s.Back;
        return new NavResult(new NavState(b?.View ?? AppView.Week, b?.Anchor ?? today, s.Tab), TransitionKind.SettingsOut);
    }

    /// <summary>Settings tab switch: Next if the new tab is lower in the list, else Prev; None if unchanged.</summary>
    public static NavResult SetTab(NavState s, SettingsTab tab)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.Tab == tab)
        {
            return new NavResult(s, TransitionKind.None);
        }

        return new NavResult(s with { Tab = tab }, tab > s.Tab ? TransitionKind.Next : TransitionKind.Prev);
    }

    /// <summary>Calendar level: Year 0 … Day 3; −1 outside the calendar (the prototype's LV.indexOf).</summary>
    private static int Level(AppView v) => v is >= AppView.Year and <= AppView.Day ? v - AppView.Year : -1;

    /// <summary>Same tab; Back is only kept while in Settings.</summary>
    private static NavState Go(NavState s, AppView view, DateOnly anchor) =>
        s with { View = view, Anchor = anchor, Back = view == AppView.Settings ? s.Back : null };
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
    public static HeaderModel Build(NavState s, DateOnly today, DayOfWeek weekStart)
    {
        ArgumentNullException.ThrowIfNull(s);
        var a = s.Anchor;
        var ws = DateMath.WeekStart(a, weekStart);
        var we = ws.AddDays(6);
        var weekLabel = "Week " + DateMath.WeekNumber(ws).ToString(CultureInfo.InvariantCulture);
        var year = new Crumb(a.Year.ToString(CultureInfo.InvariantCulture), new NavTarget(AppView.Year, a));
        var month = new Crumb(DateMath.MonthName(a.Month), new NavTarget(AppView.Month, a));
        switch (s.View)
        {
            case AppView.Inbox:
                return new HeaderModel([new Crumb("All tasks", null)], "Inbox", string.Empty);
            case AppView.Settings:
                return new HeaderModel([new Crumb("Settings", null)], TabTitle(s.Tab), string.Empty);
            case AppView.Today:
                return new HeaderModel([new Crumb(DateMath.WeekdayLong(today.DayOfWeek) + " · Today", null)], DateMath.DayMonthLong(today), string.Empty);
            case AppView.Year:
                return new HeaderModel([new Crumb("Calendar", null)], a.Year.ToString(CultureInfo.InvariantCulture), string.Empty);
            case AppView.Month:
                return new HeaderModel([year], DateMath.MonthName(a.Month), string.Empty);
            case AppView.Week:
                var sub = DateMath.DayText(ws.Day) + (ws.Month == we.Month ? string.Empty : " " + DateMath.MonthAbbrev(ws.Month))
                    + " – " + DateMath.DayMonth(we);
                return new HeaderModel([year, month], weekLabel, sub);
            default:
                return new HeaderModel([year, month, new Crumb(weekLabel, new NavTarget(AppView.Week, a))], DateMath.DayMonthLong(a),
                    DateMath.WeekdayLong(a.DayOfWeek) + (a == today ? " · Today" : string.Empty));
        }
    }

    /// <summary>Settings page titles: Appearance, Calendar, Tasks, Windows, Data, Keyboard shortcuts.</summary>
    public static string TabTitle(SettingsTab tab) => tab switch
    {
        SettingsTab.Appearance => "Appearance",
        SettingsTab.Calendar => "Calendar",
        SettingsTab.Tasks => "Tasks",
        SettingsTab.Windows => "Windows",
        SettingsTab.Data => "Data",
        _ => "Keyboard shortcuts",
    };
}
