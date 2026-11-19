using System.Globalization;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Storage;

namespace Doba.App.Tests.Performance;

/// <summary>
/// Demo-like task sets for the performance tests and for <c>scripts/perf.ps1</c>, which writes them to databases under
/// <c>artifacts\</c> through the app's own Storage API (no hand-written SQL).
/// </summary>
internal static class PerfData
{
    /// <summary>The 348 demo seed tasks (titles, tags, priorities, spans, times) with their dates moved by
    /// <c>today − DemoData.Today</c>, so a real-clock launch shows the same mix around its own today.</summary>
    public static IReadOnlyList<TaskItem> DemoLike(DateOnly today)
    {
        var shift = today.DayNumber - DemoData.Today.DayNumber;
        return [.. DemoData.Tasks().Select(t => t with { Date = t.Date?.AddDays(shift), End = t.End?.AddDays(shift) })];
    }

    /// <summary>
    /// <paramref name="count"/> tasks in the demo's style, dense around <paramref name="today"/>: the demo seed first
    /// (moved to today), then tasks over roughly three years (two back, one ahead) with the demo's titles and tags,
    /// ~5 % undated, ~8 % multi-day (2–6 days), a third timed, all priorities; past tasks are done except in the last
    /// week (about one in five still open, as overdue work would be). Fixed seed: the same set every run.
    /// </summary>
    public static IReadOnlyList<TaskItem> Many(int count, DateOnly today)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var tasks = new List<TaskItem>(count);
        tasks.AddRange(DemoLike(today).Take(count));
        var titles = DemoData.Tasks().Select(t => (t.Title, t.Tag)).Distinct().ToArray();
        var rnd = new Random(20261118);
        var first = today.AddDays(-2 * 365);
        while (tasks.Count < count)
        {
            var (title, tag) = titles[rnd.Next(titles.Length)];
            var roll = rnd.NextDouble();
            DateOnly? date = roll < 0.05 ? null : first.AddDays(rnd.Next(3 * 365));
            DateOnly? end = date is { } d && roll > 0.92 ? d.AddDays(1 + rnd.Next(5)) : null;
            TimeOnly? time = date is not null && end is null && rnd.Next(3) == 0 ? new TimeOnly(7 + rnd.Next(15), rnd.Next(4) * 15) : null;
            var done = date is { } past && past < today && (past < today.AddDays(-7) || rnd.Next(5) != 0);
            tasks.Add(new TaskItem(tasks.Count + 1, title, string.Empty, date, end, time, time is null ? 0 : 15 * (1 + rnd.Next(8)),
                (Priority)rnd.Next(5), tag, done));
        }

        return tasks;
    }

    /// <summary>Single-day tasks added to every day of the dense week (more than any Week cell can show).</summary>
    public const int DenseTasksPerDay = 10;

    /// <summary>Multi-day tasks added inside the dense week (bars above the cards).</summary>
    public const int DenseBars = 2;

    /// <summary>
    /// The Week view at its fullest for the frame budget ("60 fps on the Week view with about 40 cards"): the
    /// <see cref="Many"/> set plus, in the Monday-start week of <paramref name="today"/>, <see cref="DenseTasksPerDay"/>
    /// open single-day tasks on every day (the demo's titles, tags and priorities, every third one timed) and
    /// <see cref="DenseBars"/> multi-day tasks, so every day cell is filled to what its height holds and shows "+N more".
    /// </summary>
    public static IReadOnlyList<TaskItem> DenseWeek(int count, DateOnly today)
    {
        var tasks = new List<TaskItem>(Many(count, today));
        var titles = DemoData.Tasks().Where(t => t.End is null).Select(t => (t.Title, t.Tag, t.Priority)).Distinct().ToArray();
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var n = 0;
        for (var d = 0; d < 7; d++)
        {
            for (var k = 0; k < DenseTasksPerDay; k++, n++)
            {
                var (title, tag, priority) = titles[n % titles.Length];
                TimeOnly? time = n % 3 == 0 ? new TimeOnly(8 + (k % 10), 0) : null;
                tasks.Add(new TaskItem(tasks.Count + 1, title, string.Empty, monday.AddDays(d), null, time, time is null ? 0 : 45,
                    priority, tag, Done: false));
            }
        }

        for (var b = 0; b < DenseBars; b++)
        {
            var (title, tag, _) = titles[b];
            tasks.Add(new TaskItem(tasks.Count + 1, title, string.Empty, monday.AddDays(1 + (2 * b)), monday.AddDays(3 + (2 * b)), null, 0,
                Priority.Medium, tag, Done: false));
        }

        return tasks;
    }

    /// <summary>Creates (or replaces) <c>doba.db</c> in <paramref name="folder"/> holding <paramref name="tasks"/> and the
    /// demo tags, with <paramref name="settings"/> (default settings when null), through <see cref="DobaDatabase"/>.</summary>
    public static async Task WriteDatabaseAsync(string folder, IReadOnlyList<TaskItem> tasks, CancellationToken ct, AppSettings? settings = null)
    {
        Directory.CreateDirectory(folder);
        foreach (var stale in Directory.EnumerateFiles(folder, "doba.db*"))
        {
            File.Delete(stale);
        }

        var backups = new DataLocation(folder).BackupFolder;
        if (Directory.Exists(backups))
        {
            Directory.Delete(backups, recursive: true);
        }

        await using var db = DobaDatabase.Open(new DataLocation(folder));
        db.Tasks.ReplaceAll(tasks, DemoData.Tags);
        if (settings is not null)
        {
            db.Settings.Save(settings);
        }

        await db.Tasks.FlushAsync(ct);
    }

    /// <summary>Invariant "F1".</summary>
    public static string F1(double value) => value.ToString("F1", CultureInfo.InvariantCulture);
}
