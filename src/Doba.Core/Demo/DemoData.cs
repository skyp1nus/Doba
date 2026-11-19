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

    // Prototype seed(): [date, title, tag, done, time, dur, end, asap-flag]
    private static readonly (string? Date, string Title, string? Tag, bool Done, string? Time, int Dur, string? End, bool AsapFlag)[] s_rows =
    [
        ("2026-01-12", "Q1 planning", "work", true, null, 0, "2026-01-14", false),
        ("2026-03-03", "Home office setup", "personal", true, null, 0, "2026-03-05", false),
        ("2026-05-12", "Lisbon trip", "personal", true, null, 0, "2026-05-16", false),
        ("2026-06-15", "Product launch", "work", true, null, 0, "2026-06-19", false),
        ("2026-08-03", "Vacation", "personal", true, null, 0, "2026-08-14", false),
        ("2026-09-22", "Conference", "learning", true, null, 0, "2026-09-24", false),
        ("2026-10-27", "Review pull requests", "work", true, null, 0, null, false),
        ("2026-10-29", "Gym — legs", null, true, null, 0, null, false),
        ("2026-11-01", "Weekly review", null, true, "21:00", 30, null, false),
        ("2026-11-02", "Team sync", null, true, "10:00", 30, null, false),
        ("2026-11-02", "Pay bills", null, true, null, 0, null, false),
        ("2026-11-03", "Gym — legs", null, true, null, 0, null, false),
        ("2026-11-04", "Client onboarding", "work", true, null, 0, "2026-11-06", false),
        ("2026-11-04", "Design review", "work", true, null, 0, null, false),
        ("2026-11-05", "Read 30 pages", "learning", true, null, 0, null, false),
        ("2026-11-05", "Groceries", null, true, null, 0, null, false),
        ("2026-11-06", "Monthly hours report", "work", true, null, 0, null, false),
        ("2026-11-07", "Farmers market", null, true, null, 0, null, false),
        ("2026-11-08", "Weekly review", null, true, "21:00", 30, null, false),
        ("2026-11-09", "API migration to the new gateway", "work", true, null, 0, "2026-11-11", false),
        ("2026-11-09", "Review open pull requests for the API", "work", true, null, 0, null, false),
        ("2026-11-09", "Team sync — sprint planning", null, true, "10:00", 30, null, false),
        ("2026-11-09", "Gym — upper body", null, true, "18:30", 60, null, false),
        ("2026-11-09", "Pay rent and utilities", null, true, null, 0, null, false),
        ("2026-11-10", "Write the database migration plan", "work", true, null, 0, null, false),
        ("2026-11-10", "Dentist check-up", "personal", true, "16:30", 45, null, false),
        ("2026-11-10", "Groceries for the week", null, true, null, 0, null, false),
        ("2026-11-11", "Portfolio site redesign", "personal", false, null, 0, "2026-11-14", false),
        ("2026-11-11", "Deploy the new build to staging", "work", true, null, 0, null, false),
        ("2026-11-11", "Coffee with Alex", null, true, null, 0, null, false),
        ("2026-11-11", "Read 30 pages of “Deep Work”", "learning", true, null, 0, null, false),
        ("2026-11-12", "Fix login redirect loop on Safari", "work", true, null, 0, null, false),
        ("2026-11-12", "Online course — module 3: async patterns", "learning", true, null, 0, null, false),
        ("2026-11-12", "Laundry", null, false, null, 0, null, false),
        ("2026-11-13", "Send the monthly hours report", "work", true, null, 0, null, false),
        ("2026-11-13", "Call mom", null, true, null, 0, null, false),
        ("2026-11-13", "Movie night", "personal", true, null, 0, null, false),
        ("2026-11-14", "Farmers market", null, true, null, 0, null, false),
        ("2026-11-14", "Take the bike in for a brake repair", null, false, null, 0, null, false),
        ("2026-11-15", "Morning run along the river", "personal", true, "08:00", 45, null, false),
        ("2026-11-15", "Meal prep for Monday–Wednesday", null, true, "12:00", 60, null, false),
        ("2026-11-15", "Coffee and reading — Deep Work, ch. 4", "learning", true, "14:00", 90, null, false),
        ("2026-11-15", "Weekly review", null, true, "21:00", 30, null, false),
        ("2026-11-15", "Plan next week and set top 3 priorities", "work", false, "22:00", 45, null, false),
        ("2026-11-15", "Water the plants and repot the ficus", null, false, null, 0, null, false),
        ("2026-11-16", "Team sync — sprint review", null, false, "10:00", 30, null, false),
        ("2026-11-16", "Review open pull requests", "work", false, null, 0, null, false),
        ("2026-11-17", "Send the November invoice to the client", "work", false, null, 0, null, false),
        ("2026-11-17", "Gym — legs", null, false, "18:30", 60, null, false),
        ("2026-11-18", "Write a blog post about the migration", "learning", false, null, 0, "2026-11-19", false),
        ("2026-11-18", "Design review for the onboarding flow", "work", false, "15:00", 60, null, false),
        ("2026-11-18", "Piano lesson", null, false, null, 0, null, false),
        ("2026-11-19", "Release v2.4 and write the changelog", "work", false, null, 0, null, false),
        ("2026-11-19", "Haircut", null, false, "17:00", 30, null, false),
        ("2026-11-20", "Weekend trip to Kraków", "personal", false, null, 0, "2026-11-22", false),
        ("2026-11-20", "Return the parcel at the post office", null, false, null, 0, null, false),
        ("2026-11-21", "Old town walk and Wawel castle", null, false, null, 0, null, false),
        ("2026-11-22", "Weekly review", null, false, "21:00", 30, null, false),
        ("2026-11-23", "Team sync", null, false, "10:00", 30, null, false),
        ("2026-11-24", "Gym — upper body", null, false, null, 0, null, false),
        ("2026-11-25", "Year-end report", "work", false, null, 0, "2026-11-27", false),
        ("2026-11-26", "Dentist follow-up", "personal", false, null, 0, null, false),
        ("2026-11-27", "Pay bills", null, false, null, 0, null, false),
        ("2026-11-28", "Dinner with friends", "personal", false, null, 0, null, false),
        ("2026-11-29", "Weekly review", null, false, null, 0, null, false),
        ("2026-11-30", "Monthly hours report", "work", false, null, 0, null, false),
        ("2026-12-01", "Review pull requests", "work", false, null, 0, null, false),
        ("2026-12-03", "Gym — legs", null, false, null, 0, null, false),
        ("2026-12-05", "Christmas market", null, false, null, 0, null, false),
        ("2026-12-21", "Holidays", "personal", false, null, 0, "2026-12-27", false),
        (null, "Renew passport", "personal", false, null, 0, null, true),
        (null, "Call the bank", null, false, null, 0, null, true),
        (null, "Fix the kitchen tap", null, false, null, 0, null, false),
        (null, "Read “Deep Work”", "learning", false, null, 0, null, false),
        (null, "Backup photos", null, false, null, 0, null, false),
        (null, "Research standing desks", null, false, null, 0, null, false),
    ];

    private static readonly Dictionary<string, Priority> s_prio = new(StringComparer.Ordinal)
    {
        ["Renew passport"] = Priority.Asap,
        ["Call the bank"] = Priority.Asap,
        ["Fix the kitchen tap"] = Priority.Medium,
        ["Read “Deep Work”"] = Priority.Low,
        ["Backup photos"] = Priority.Low,
        ["Fix login redirect loop on Safari"] = Priority.High,
        ["Send the November invoice to the client"] = Priority.High,
        ["Release v2.4 and write the changelog"] = Priority.High,
        ["Plan next week and set top 3 priorities"] = Priority.Medium,
        ["Review open pull requests"] = Priority.Medium,
        ["Design review for the onboarding flow"] = Priority.Medium,
        ["Water the plants and repot the ficus"] = Priority.Low,
        ["Laundry"] = Priority.Low,
        ["Take the bike in for a brake repair"] = Priority.Medium,
        ["Deploy the new build to staging"] = Priority.High,
        ["Write the database migration plan"] = Priority.High,
        ["Send the monthly hours report"] = Priority.High,
        ["Return the parcel at the post office"] = Priority.Low,
        ["Year-end report"] = Priority.High,
        ["Monthly hours report"] = Priority.High,
        ["Dentist follow-up"] = Priority.Medium,
    };

    private static readonly (string Title, string? Tag)[] s_pool =
    [
        ("Review pull requests", "work"), ("Team sync", null), ("Gym", null), ("Groceries", null), ("Read 30 pages", "learning"),
        ("Call mom", null), ("Deploy to staging", "work"), ("Pay bills", null), ("Long walk", null), ("Piano practice", "learning"),
        ("Design review", "work"), ("Laundry", null), ("Update CV", null),
    ];

    /// <summary>All seed tasks with Ids 1..N in the prototype's array order.</summary>
    public static IReadOnlyList<TaskItem> Tasks()
    {
        var output = new List<TaskItem>(320);
        foreach (var r in s_rows)
        {
            // PRIO[title] wins (it also matches the dated "Laundry"); otherwise the asap flag of undated rows.
            var prio = s_prio.TryGetValue(r.Title, out var p) ? p : (r.AsapFlag ? Priority.Asap : Priority.None);
            output.Add(new TaskItem(
                output.Count + 1,
                r.Title,
                string.Empty,
                r.Date is null ? null : DateOnly.ParseExact(r.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                r.End is null ? null : DateOnly.ParseExact(r.End, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                r.Time is null ? null : TimeOnly.ParseExact(r.Time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                r.Dur,
                prio,
                r.Tag,
                r.Done));
        }

        var rnd = new Mulberry32(46);
        var gapStart = new DateOnly(2026, 10, 26);
        var gapEnd = new DateOnly(2026, 12, 6);
        for (var d = new DateOnly(2026, 1, 1); d <= new DateOnly(2026, 12, 31); d = d.AddDays(1))
        {
            if (d >= gapStart && d <= gapEnd)
            {
                continue;
            }

            var r = rnd.Next();
            var n = r < 0.42 ? 0 : (r < 0.82 ? 1 : 2);
            for (var i = 0; i < n; i++)
            {
                var (title, tag) = s_pool[(int)Math.Floor(rnd.Next() * s_pool.Length)];
                output.Add(new TaskItem(output.Count + 1, title, string.Empty, d, null, null, 0, Priority.None, tag, d < Today));
            }
        }

        return output;
    }

    /// <summary>mulberry32 exactly as the prototype runs it in JavaScript (int32 wrap-around arithmetic).</summary>
    internal sealed class Mulberry32(int seed)
    {
        private int _a = seed;

        public double Next()
        {
            unchecked
            {
                _a += 0x6D2B79F5;
                var t = (_a ^ (int)((uint)_a >> 15)) * (1 | _a);
                t = (t + (t ^ (int)((uint)t >> 7)) * (61 | t)) ^ t;
                return (uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
            }
        }
    }
}
