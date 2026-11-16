using Doba.Core.Model;

namespace Doba.Core.Demo;

/// <summary>
/// The design prototype's seed tasks and fixed clock (2026-11-15 22:26), so screenshots are comparable with the design.
/// Includes the deterministic random fill (mulberry32, seed 46) for the rest of 2026.
/// </summary>
public static class DemoData
{
    public static DateOnly Today { get; } = new(2026, 11, 15);

    public static TimeOnly Now { get; } = new(22, 26);

    /// <summary>A clock frozen at 2026-11-15 22:26 local (UTC offset 0).</summary>
    public static Time.FixedTimeProvider CreateClock() => new(new DateTimeOffset(2026, 11, 15, 22, 26, 0, TimeSpan.Zero));

    public static IReadOnlyList<string> Tags { get; } = ["work", "personal", "learning"];

    /// <summary>All seed tasks with Ids 1..N in the prototype's array order.</summary>
    public static IReadOnlyList<TaskItem> Tasks() => throw new NotImplementedException();
}
