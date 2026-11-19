using System.Globalization;

namespace Doba.Core.Time;

/// <summary>Calendar arithmetic shared by every projection. Pure; all dates are local calendar days.</summary>
public static class DateMath
{
    // Private like the weekday arrays: a shared mutable array would let one caller change every label.
    private static readonly string[] s_monthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    private static readonly string[] s_monthShort = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>"January".."December", indexed by month − 1 (read-only).</summary>
    public static IReadOnlyList<string> MonthNames { get; } = Array.AsReadOnly(s_monthNames);

    /// <summary>"Jan".."Dec", indexed by month − 1 (read-only).</summary>
    public static IReadOnlyList<string> MonthShort { get; } = Array.AsReadOnly(s_monthShort);

    /// <summary>"November" for month 11.</summary>
    public static string MonthName(int month) => s_monthNames[month - 1];

    /// <summary>"Nov" for month 11.</summary>
    public static string MonthAbbrev(int month) => s_monthShort[month - 1];

    private static readonly string[] s_weekdayShort = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] s_weekdayLong = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    // "00:00".."23:59": every label is a cached string, so projections do not allocate for times.
    private static readonly string[] s_hhmm = BuildHhmm();

    private static string[] BuildHhmm()
    {
        var a = new string[24 * 60];
        for (var m = 0; m < a.Length; m++)
        {
            a[m] = string.Create(5, m, static (span, v) =>
            {
                int h = v / 60, mi = v % 60;
                span[0] = (char)('0' + h / 10);
                span[1] = (char)('0' + h % 10);
                span[2] = ':';
                span[3] = (char)('0' + mi / 10);
                span[4] = (char)('0' + mi % 10);
            });
        }

        return a;
    }

    /// <summary>"Mon".."Sun" indexed by <see cref="DayOfWeek"/>.</summary>
    public static string WeekdayShort(DayOfWeek d) => s_weekdayShort[(int)d];

    /// <summary>"Monday".."Sunday".</summary>
    public static string WeekdayLong(DayOfWeek d) => s_weekdayLong[(int)d];

    /// <summary>Start of the week containing <paramref name="date"/> for the given first day of week.</summary>
    public static DateOnly WeekStart(DateOnly date, DayOfWeek firstDay)
    {
        var back = ((int)date.DayOfWeek - (int)firstDay + 7) % 7;
        return date.AddDays(-back);
    }

    /// <summary>ISO-8601 week number of <paramref name="date"/>.</summary>
    public static int IsoWeek(DateOnly date) => ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// Week number shown for the week starting at <paramref name="weekStart"/>: the ISO week of the Monday inside
    /// that 7-day week (for Monday starts this is the ISO week of weekStart).
    /// </summary>
    public static int WeekNumber(DateOnly weekStart)
    {
        var toMonday = ((int)DayOfWeek.Monday - (int)weekStart.DayOfWeek + 7) % 7;
        return IsoWeek(weekStart.AddDays(toMonday));
    }

    /// <summary>b − a in days.</summary>
    public static int DaysBetween(DateOnly a, DateOnly b) => b.DayNumber - a.DayNumber;

    /// <summary>"Mon 16 Nov".</summary>
    public static string ShortWithWeekday(DateOnly d) =>
        string.Concat(s_weekdayShort[(int)d.DayOfWeek], " ", DayText(d.Day), " ", MonthAbbrev(d.Month));

    /// <summary>"16 Nov".</summary>
    public static string DayMonth(DateOnly d) => string.Concat(DayText(d.Day), " ", MonthAbbrev(d.Month));

    /// <summary>"16 November".</summary>
    public static string DayMonthLong(DateOnly d) => string.Concat(DayText(d.Day), " ", MonthName(d.Month));

    /// <summary>
    /// Label of a multi-day range: "20–22 Nov" inside one month, "28 Oct – 2 Nov" across months
    /// (with <paramref name="monthInSameMonth"/> false the same-month form is just "20–22", as in the Year view).
    /// </summary>
    public static string SpanLabel(DateOnly start, DateOnly end, bool monthInSameMonth = true)
    {
        if (start.Year == end.Year && start.Month == end.Month)
        {
            return monthInSameMonth
                ? string.Concat(DayText(start.Day), "–", DayText(end.Day), " ", MonthAbbrev(end.Month))
                : string.Concat(DayText(start.Day), "–", DayText(end.Day));
        }

        return string.Concat(DayMonth(start), " – ", DayMonth(end));
    }

    /// <summary>"yyyy-MM-dd".</summary>
    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"HH:mm" from minutes since midnight (wraps at 24 h).</summary>
    public static string Hhmm(int minutes) => s_hhmm[((minutes % 1440) + 1440) % 1440];

    /// <summary>"HH:mm" of a time of day (seconds ignored).</summary>
    public static string Hhmm(TimeOnly t) => s_hhmm[t.Hour * 60 + t.Minute];

    /// <summary>Minutes since midnight.</summary>
    public static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    /// <summary>The 7 weekdays in display order for the given first day.</summary>
    public static IReadOnlyList<DayOfWeek> WeekdayOrder(DayOfWeek firstDay)
    {
        var a = new DayOfWeek[7];
        for (var i = 0; i < 7; i++)
        {
            a[i] = (DayOfWeek)(((int)firstDay + i) % 7);
        }

        return a;
    }

    /// <summary>First day of the month containing <paramref name="d"/>.</summary>
    public static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);

    /// <summary>JavaScript <c>Math.round</c> (halves round up), used wherever the prototype rounds.</summary>
    public static int RoundHalfUp(double x) => (int)Math.Floor(x + 0.5);

    private static readonly string[] s_numbers = BuildNumbers();

    private static string[] BuildNumbers()
    {
        var a = new string[32];
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = i.ToString(CultureInfo.InvariantCulture);
        }

        return a;
    }

    /// <summary>Cached "1".."31".</summary>
    internal static string DayText(int day) => s_numbers[day];
}
