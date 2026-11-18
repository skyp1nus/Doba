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

    /// <summary>Creates (or replaces) <c>doba.db</c> in <paramref name="folder"/> holding <paramref name="tasks"/> and the
    /// demo tags, with default settings, through <see cref="DobaDatabase"/>.</summary>
    public static async Task WriteDatabaseAsync(string folder, IReadOnlyList<TaskItem> tasks, CancellationToken ct)
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
        await db.Tasks.FlushAsync(ct);
    }

    /// <summary>Invariant "F1".</summary>
    public static string F1(double value) => value.ToString("F1", CultureInfo.InvariantCulture);
}
