using Doba.Core.Store;

namespace Doba.Core.Projections;

/// <summary>
/// Pure "what goes where" functions mirroring the prototype's inboxData / todayData / dayData / weekData /
/// monthData / yearData. Each must finish in under 5 ms against a 10,000-task store.
/// Sorting: single-day lists use the prototype's byTime (timed first, by time, then priority desc, then Id);
/// spans use byDate (Date, then Id).
/// </summary>
public static class Projector
{
    public static InboxModel Inbox(ITaskReader store, ProjectionContext ctx) => throw new NotImplementedException();

    public static TodayModel Today(ITaskReader store, ProjectionContext ctx) => throw new NotImplementedException();

    public static DayModel Day(ITaskReader store, ProjectionContext ctx, DateOnly date) => throw new NotImplementedException();

    /// <param name="anchor">Any day in the week to show.</param>
    public static WeekModel Week(ITaskReader store, ProjectionContext ctx, DateOnly anchor, WeekMetrics metrics) => throw new NotImplementedException();

    /// <param name="anchor">Any day in the month to show.</param>
    public static MonthModel Month(ITaskReader store, ProjectionContext ctx, DateOnly anchor, MonthMetrics metrics) => throw new NotImplementedException();

    public static YearModel Year(ITaskReader store, ProjectionContext ctx, int year) => throw new NotImplementedException();

    public static SidebarModel Sidebar(ITaskReader store, ProjectionContext ctx) => throw new NotImplementedException();

    /// <summary>Case-insensitive substring match on titles across all tasks; open tasks first, then by date. At most <paramref name="limit"/> hits.</summary>
    public static IReadOnlyList<SearchHit> Search(ITaskReader store, ProjectionContext ctx, string query, int limit = 50) => throw new NotImplementedException();
}
