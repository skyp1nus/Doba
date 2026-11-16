namespace Doba.Core.Projections;

/// <summary>A timed task on the day timeline (07:00–23:00).</summary>
/// <param name="Row">The task (Meta empty).</param>
/// <param name="StartMinutes">Minutes since midnight.</param>
/// <param name="DurationMinutes">Effective duration (0 → 30).</param>
/// <param name="Range">"14:00 – 15:30".</param>
public sealed record ScheduleBlock(TaskRow Row, int StartMinutes, int DurationMinutes, string Range);

/// <summary>Timeline of one day. Blocks starting before <see cref="FirstHour"/> are omitted, as in the prototype.</summary>
/// <param name="NowLabel">"22:26" when the day is today, else null (no now-line).</param>
public sealed record ScheduleModel(IReadOnlyList<ScheduleBlock> Blocks, int? NowMinutes, string? NowLabel)
{
    public const int FirstHour = 7;
    public const int LastHour = 23;
}

/// <summary>Pixel geometry of the timeline for a given hour height (design: 48 px/hour, grid height 16·48+2).</summary>
public static class ScheduleLayout
{
    /// <summary>top = round((start − 7h)/60 · hourHeight) + 1.</summary>
    public static double BlockTop(int startMinutes, double hourHeight) =>
        Math.Floor((startMinutes - ScheduleModel.FirstHour * 60) / 60.0 * hourHeight + 0.5) + 1;

    /// <summary>h = max(24, round(duration/60 · hourHeight) − 3).</summary>
    public static double BlockHeight(int durationMinutes, double hourHeight) =>
        Math.Max(24, Math.Floor(durationMinutes / 60.0 * hourHeight + 0.5) - 3);

    /// <summary>Blocks shorter than 44 px lay out title and range on one row.</summary>
    public static bool IsCompact(double blockHeight) => blockHeight < 44;

    /// <summary>round((now − 7h)/60 · hourHeight).</summary>
    public static double NowTop(int nowMinutes, double hourHeight) =>
        Math.Floor((nowMinutes - ScheduleModel.FirstHour * 60) / 60.0 * hourHeight + 0.5);

    public static double GridHeight(double hourHeight) => 16 * hourHeight + 2;
}
