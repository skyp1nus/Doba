using Doba.App.Services;
using Doba.Core.Time;

namespace Doba.App.Tests.Services;

public class DayClockTests
{
    [Fact]
    public void Raises_minute_and_day_changes_from_the_injected_clock()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 11, 15, 23, 59, 10, TimeSpan.Zero));
        using var clock = new DayClock(time);
        var ticks = 0;
        DateOnly? newDay = null;
        clock.MinuteTick += (_, _) => ticks++;
        clock.DayChanged += (_, d) => newDay = d;

        clock.Check();
        Assert.Equal(0, ticks);

        time.Advance(TimeSpan.FromSeconds(30)); // 23:59:40 — same minute
        clock.Check();
        Assert.Equal(0, ticks);

        time.Advance(TimeSpan.FromSeconds(30)); // 00:00:10 next day
        clock.Check();
        Assert.Equal(1, ticks);
        Assert.Equal(new DateOnly(2026, 11, 16), newDay);
        Assert.Equal(new DateOnly(2026, 11, 16), clock.Today);
        Assert.Equal(new TimeOnly(0, 0), clock.Now);
    }
}
