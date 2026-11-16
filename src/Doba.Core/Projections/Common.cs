using Doba.Core.Model;
using Doba.Core.Navigation;

namespace Doba.Core.Projections;

/// <summary>Inputs every projection needs besides the store.</summary>
/// <param name="Today">The current local day.</param>
/// <param name="Now">The current local time (for the now-line).</param>
/// <param name="WeekStart">First day of week from settings.</param>
/// <param name="FilterTag">Sidebar tag filter; tasks with another tag are dimmed. Null = no filter.</param>
public sealed record ProjectionContext(DateOnly Today, TimeOnly Now, DayOfWeek WeekStart, string? FilterTag);

/// <summary>
/// One task as a view shows it. Views pick colours from Done / IsMultiDay / Dimmed; all text is final.
/// Equality is by value, so a view can diff old vs new rows keyed by <see cref="Id"/>.
/// </summary>
/// <param name="Id">Task id (row key).</param>
/// <param name="Title">Title.</param>
/// <param name="Done">Completed.</param>
/// <param name="Priority">Priority (drives the bars / ASAP icon).</param>
/// <param name="TagLabel">"#work", or empty.</param>
/// <param name="Time">"HH:mm", or empty.</param>
/// <param name="Meta">Right-hand meta text (date, range, "Day 2 of 3", "3 days"...), or empty.</param>
/// <param name="IsMultiDay">Task spans several days (rendered as a tinted bar).</param>
/// <param name="Dimmed">A tag filter is active and this task has another tag (opacity 0.3).</param>
/// <param name="CanMoveToToday">Show the "→ Today" button (Today › Overdue, single-day only).</param>
public sealed record TaskRow(
    long Id,
    string Title,
    bool Done,
    Priority Priority,
    string TagLabel,
    string Time,
    string Meta,
    bool IsMultiDay,
    bool Dimmed,
    bool CanMoveToToday = false);

/// <summary>Colour role of a group label.</summary>
public enum GroupTone
{
    /// <summary>#8C8C92.</summary>
    Muted,
    /// <summary>#D9A066 (ASAP, Overdue).</summary>
    Warning,
    /// <summary>The accent colour (Inbox › "Today").</summary>
    Accent,
}

/// <summary>A navigation target produced by a projection ("Later" → month, a day label → day).</summary>
public sealed record NavTarget(AppView View, DateOnly Anchor);

/// <summary>A labelled list section.</summary>
/// <param name="Key">Stable key for diffing ("p-asap", "over", "2026-11-16", ...).</param>
/// <param name="Label">Upper-cased by the view (CSS text-transform), so keep natural case here.</param>
/// <param name="Count">Right-aligned count text ("3", "12 tasks").</param>
/// <param name="Tone">Label colour role.</param>
/// <param name="Items">Rows.</param>
/// <param name="Link">When set, the label is a button that zooms to this target.</param>
public sealed record TaskGroup(string Key, string Label, string Count, GroupTone Tone, IReadOnlyList<TaskRow> Items, NavTarget? Link = null);

/// <summary>
/// A multi-day bar inside a 7- or 4-column grid row.
/// Column is 0-based, Lane is 0-based (lane rows sit under the day header).
/// ClipStart/ClipEnd mean the span continues before/after this row (square corner, no margin).
/// </summary>
public sealed record SpanBar(TaskRow Row, int Column, int ColumnSpan, int Lane, bool ClipStart, bool ClipEnd);
