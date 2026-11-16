namespace Doba.Core.Time;

/// <summary>"Today" and "now" always come from an injected <see cref="TimeProvider"/>, never DateTime.Now.</summary>
public static class TimeProviderExtensions
{
    public static DateTime LocalNow(this TimeProvider clock) => clock.GetLocalNow().DateTime;

    public static DateOnly Today(this TimeProvider clock) => DateOnly.FromDateTime(clock.LocalNow());

    public static TimeOnly TimeOfDay(this TimeProvider clock) => TimeOnly.FromDateTime(clock.LocalNow());
}

/// <summary>A clock frozen at one local instant (demo screenshots, tests). <see cref="Set"/> moves it.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Fixed", _now.Offset, "Fixed", "Fixed");

    public void Set(DateTimeOffset now) => _now = now;

    public void Advance(TimeSpan by) => _now += by;
}
