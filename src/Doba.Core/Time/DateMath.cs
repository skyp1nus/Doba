namespace Doba.Core.Time;

/// <summary>Calendar arithmetic shared by every projection. Pure; all dates are local calendar days.</summary>
public static class DateMath
{
    public static readonly string[] MonthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    public static readonly string[] MonthShort = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>"Mon".."Sun" indexed by <see cref="DayOfWeek"/>.</summary>
    public static string WeekdayShort(DayOfWeek d) => throw new NotImplementedException();

    /// <summary>"Monday".."Sunday".</summary>
    public static string WeekdayLong(DayOfWeek d) => throw new NotImplementedException();

    /// <summary>Start of the week containing <paramref name="date"/> for the given first day of week.</summary>
    public static DateOnly WeekStart(DateOnly date, DayOfWeek firstDay) => throw new NotImplementedException();

    /// <summary>ISO-8601 week number of <paramref name="date"/>.</summary>
    public static int IsoWeek(DateOnly date) => throw new NotImplementedException();

    /// <summary>
    /// Week number shown for the week starting at <paramref name="weekStart"/>: the ISO week of the Monday inside
    /// that 7-day week (for Monday starts this is the ISO week of weekStart).
    /// </summary>
    public static int WeekNumber(DateOnly weekStart) => throw new NotImplementedException();

    /// <summary>b − a in days.</summary>
    public static int DaysBetween(DateOnly a, DateOnly b) => b.DayNumber - a.DayNumber;

    /// <summary>"Mon 16 Nov".</summary>
    public static string ShortWithWeekday(DateOnly d) => throw new NotImplementedException();

    /// <summary>"16 Nov".</summary>
    public static string DayMonth(DateOnly d) => throw new NotImplementedException();

    /// <summary>"16 November".</summary>
    public static string DayMonthLong(DateOnly d) => throw new NotImplementedException();

    /// <summary>"HH:mm" from minutes since midnight (wraps at 24 h).</summary>
    public static string Hhmm(int minutes) => throw new NotImplementedException();

    /// <summary>Minutes since midnight.</summary>
    public static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    /// <summary>The 7 weekdays in display order for the given first day.</summary>
    public static IReadOnlyList<DayOfWeek> WeekdayOrder(DayOfWeek firstDay) => throw new NotImplementedException();
}
