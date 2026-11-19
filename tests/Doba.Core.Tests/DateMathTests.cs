using Doba.Core.Model;
using Doba.Core.Projections;
using Doba.Core.Time;

namespace Doba.Core.Tests;

public class DateMathTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    [InlineData(2026, 11, 15, DayOfWeek.Monday, 2026, 11, 9)]
    [InlineData(2026, 11, 9, DayOfWeek.Monday, 2026, 11, 9)]
    [InlineData(2026, 11, 15, DayOfWeek.Sunday, 2026, 11, 15)]
    [InlineData(2026, 11, 14, DayOfWeek.Sunday, 2026, 11, 8)]
    [InlineData(2026, 11, 16, DayOfWeek.Sunday, 2026, 11, 15)]
    [InlineData(2027, 1, 1, DayOfWeek.Monday, 2026, 12, 28)]
    [InlineData(2027, 1, 1, DayOfWeek.Sunday, 2026, 12, 27)]
    public void WeekStart_for_monday_and_sunday(int y, int m, int d, DayOfWeek first, int ey, int em, int ed) =>
        Assert.Equal(D(ey, em, ed), DateMath.WeekStart(D(y, m, d), first));

    [Theory]
    [InlineData(2026, 1, 1, 1)]      // Thursday: week 1 of 2026
    [InlineData(2025, 12, 29, 1)]    // Monday of ISO 2026-W01
    [InlineData(2025, 12, 28, 52)]
    [InlineData(2026, 11, 15, 46)]
    [InlineData(2026, 11, 16, 47)]
    [InlineData(2026, 12, 31, 53)]   // 2026 has 53 ISO weeks
    [InlineData(2027, 1, 3, 53)]
    [InlineData(2027, 1, 4, 1)]
    [InlineData(2021, 1, 3, 53)]     // belongs to 2020-W53
    [InlineData(2024, 12, 30, 1)]    // belongs to 2025-W01
    [InlineData(2019, 12, 30, 1)]
    public void IsoWeek_handles_year_boundaries(int y, int m, int d, int week) =>
        Assert.Equal(week, DateMath.IsoWeek(D(y, m, d)));

    [Fact]
    public void WeekNumber_uses_the_monday_inside_the_week()
    {
        Assert.Equal(46, DateMath.WeekNumber(D(2026, 11, 9)));   // Monday start
        Assert.Equal(47, DateMath.WeekNumber(D(2026, 11, 15)));  // Sunday start 15–21 Nov → Monday 16
        Assert.Equal(53, DateMath.WeekNumber(D(2026, 12, 27)));  // Sunday 27 Dec → Monday 28 Dec
        Assert.Equal(1, DateMath.WeekNumber(D(2027, 1, 3)));     // Sunday 3 Jan → Monday 4 Jan
        Assert.Equal(1, DateMath.WeekNumber(D(2025, 12, 28)));   // Sunday 28 Dec 2025 → Monday 29 Dec = 2026-W01
    }

    [Fact]
    public void Labels()
    {
        Assert.Equal("Mon 16 Nov", DateMath.ShortWithWeekday(D(2026, 11, 16)));
        Assert.Equal("Sun 1 Mar", DateMath.ShortWithWeekday(D(2026, 3, 1)));
        Assert.Equal("16 Nov", DateMath.DayMonth(D(2026, 11, 16)));
        Assert.Equal("5 January", DateMath.DayMonthLong(D(2026, 1, 5)));
        Assert.Equal("20–22 Nov", DateMath.SpanLabel(D(2026, 11, 20), D(2026, 11, 22)));
        Assert.Equal("20–22", DateMath.SpanLabel(D(2026, 11, 20), D(2026, 11, 22), monthInSameMonth: false));
        Assert.Equal("28 Oct – 2 Nov", DateMath.SpanLabel(D(2026, 10, 28), D(2026, 11, 2)));
        Assert.Equal("28 Oct – 2 Nov", DateMath.SpanLabel(D(2026, 10, 28), D(2026, 11, 2), monthInSameMonth: false));
        Assert.Equal("2026-03-07", DateMath.Iso(D(2026, 3, 7)));
        Assert.Equal("Sun", DateMath.WeekdayShort(DayOfWeek.Sunday));
        Assert.Equal("Wednesday", DateMath.WeekdayLong(DayOfWeek.Wednesday));
    }

    [Fact]
    public void Hhmm_wraps()
    {
        Assert.Equal("00:00", DateMath.Hhmm(0));
        Assert.Equal("09:05", DateMath.Hhmm(545));
        Assert.Equal("23:59", DateMath.Hhmm(1439));
        Assert.Equal("00:30", DateMath.Hhmm(1470));
        Assert.Equal("23:30", DateMath.Hhmm(-30));
        Assert.Equal("14:07", DateMath.Hhmm(new TimeOnly(14, 7, 59)));
        Assert.Equal(14 * 60 + 7, DateMath.Minutes(new TimeOnly(14, 7)));
    }

    [Fact]
    public void WeekdayOrder_starts_at_first_day()
    {
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday], DateMath.WeekdayOrder(DayOfWeek.Monday));
        Assert.Equal(DayOfWeek.Sunday, DateMath.WeekdayOrder(DayOfWeek.Sunday)[0]);
        Assert.Equal(DayOfWeek.Saturday, DateMath.WeekdayOrder(DayOfWeek.Sunday)[6]);
    }

    [Fact]
    public void RoundHalfUp_matches_javascript()
    {
        Assert.Equal(3, DateMath.RoundHalfUp(2.5));
        Assert.Equal(1, DateMath.RoundHalfUp(0.5));
        Assert.Equal(2, DateMath.RoundHalfUp(2.49));
        Assert.Equal(-2, DateMath.RoundHalfUp(-2.5));
    }

    [Fact]
    public void ScheduleLayout_matches_design_geometry()
    {
        Assert.Equal(337, ScheduleLayout.BlockTop(14 * 60, 48));
        Assert.Equal(1, ScheduleLayout.BlockTop(7 * 60, 48));
        Assert.Equal(24, ScheduleLayout.BlockHeight(30, 48));   // round(24) − 3 = 21 → min 24
        Assert.Equal(45, ScheduleLayout.BlockHeight(60, 48));
        Assert.Equal(69, ScheduleLayout.BlockHeight(90, 48));
        Assert.True(ScheduleLayout.IsCompact(33));
        Assert.False(ScheduleLayout.IsCompact(45));
        Assert.Equal(741, ScheduleLayout.NowTop(22 * 60 + 26, 48));
        Assert.Equal(770, ScheduleLayout.GridHeight(48));
    }

    [Fact]
    public void Clock_extensions_read_the_injected_provider()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 15, 23, 59, 0, TimeSpan.FromHours(2)));
        Assert.Equal(D(2026, 11, 15), clock.Today());
        Assert.Equal(new TimeOnly(23, 59), clock.TimeOfDay());
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(D(2026, 11, 16), clock.Today());
    }
}

public class TagsTests
{
    [Theory]
    [InlineData("work", "work")]
    [InlineData("  Deep Work ", "deep-work")]
    [InlineData("##Work", "work")]
    [InlineData("#Side   Project\tIdeas", "side-project-ideas")]
    [InlineData("# deep work", "deep-work")]
    [InlineData("ÜBER", "über")]
    [InlineData("a-b", "a-b")]
    public void Normalize(string raw, string expected) => Assert.Equal(expected, Tags.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData(" ### ")]
    public void Normalize_returns_null_when_empty(string? raw) => Assert.Null(Tags.Normalize(raw));

    [Fact]
    public void Month_names_are_read_only()
    {
        Assert.Equal("November", DateMath.MonthNames[10]);
        Assert.Equal("Nov", DateMath.MonthShort[10]);
        Assert.Equal(("January", "Dec"), (DateMath.MonthName(1), DateMath.MonthAbbrev(12)));
        Assert.Equal(12, DateMath.MonthNames.Count);
        Assert.Equal(12, DateMath.MonthShort.Count);

        // No caller can rewrite a label every projection shares.
        Assert.False(DateMath.MonthNames is string[]);
        Assert.False(DateMath.MonthShort is string[]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)DateMath.MonthShort)[10] = "X");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)DateMath.MonthNames)[10] = "X");
        Assert.Equal("Mon 16 Nov", DateMath.ShortWithWeekday(new DateOnly(2026, 11, 16)));
    }
}
