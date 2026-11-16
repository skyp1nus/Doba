using System.Diagnostics;
using Doba.Core.Model;
using static Doba.Storage.Tests.Samples;

namespace Doba.Storage.Tests;

/// <summary>Measures the two numbers that matter at 10,000 tasks; bounds are loose (CI machines vary), numbers are logged.</summary>
public sealed class PerformanceTests
{
    private const int Count = 10_000;

    private static List<TaskItem> ManyTasks()
    {
        var start = new DateOnly(2026, 1, 1);
        return Enumerable.Range(1, Count).Select(i => new TaskItem(
            i,
            $"Task number {i} with a realistic title",
            i % 4 == 0 ? "Some notes about this task" : string.Empty,
            i % 10 == 0 ? null : start.AddDays(i % 365),
            i % 25 == 0 && i % 10 != 0 ? start.AddDays((i % 365) + 2) : null,
            i % 3 == 0 && i % 10 != 0 && !(i % 25 == 0) ? new TimeOnly(i % 24, i % 60) : null,
            i % 3 == 0 ? 45 : 0,
            (Priority)(i % 5),
            (i % 3) switch { 0 => "work", 1 => "personal", _ => null },
            i % 2 == 0)).ToList();
    }

    [Fact]
    public async Task Persist_and_load_10000_tasks()
    {
        using var temp = new TempFolder();
        var tasks = ManyTasks();
        var output = TestContext.Current.TestOutputHelper;

        double enqueueMs, persistMs;
        await using (var db = DobaDatabase.Open(temp.Location))
        {
            var watch = Stopwatch.StartNew();
            foreach (var task in tasks)
            {
                db.Tasks.Upsert(task);
            }

            enqueueMs = watch.Elapsed.TotalMilliseconds;
            await db.Tasks.FlushAsync(Ct);
            persistMs = watch.Elapsed.TotalMilliseconds;
        }

        // Updating all of them again (ON CONFLICT path).
        double updateMs;
        await using (var db = DobaDatabase.Open(temp.Location))
        {
            var watch = Stopwatch.StartNew();
            foreach (var task in tasks)
            {
                db.Tasks.Upsert(task with { Done = !task.Done });
            }

            await db.Tasks.FlushAsync(Ct);
            updateMs = watch.Elapsed.TotalMilliseconds;
        }

        // Load: cold open of the file + migration check + read everything (best of 3 to drop JIT/first-touch noise).
        double firstLoadMs = 0, bestLoadMs = double.MaxValue, bestOpenMs = double.MaxValue, bestLoadOnlyMs = double.MaxValue;
        LoadedData? data = null;
        for (int round = 0; round < 5; round++)
        {
            var watch = Stopwatch.StartNew();
            await using var db = DobaDatabase.Open(temp.Location);
            double openMs = watch.Elapsed.TotalMilliseconds;
            data = db.Load();
            double ms = watch.Elapsed.TotalMilliseconds;
            if (round == 0)
            {
                firstLoadMs = ms;
            }

            bestLoadMs = Math.Min(bestLoadMs, ms);
            bestOpenMs = Math.Min(bestOpenMs, openMs);
            bestLoadOnlyMs = Math.Min(bestLoadOnlyMs, ms - openMs);
        }

        Assert.NotNull(data);
        Assert.Equal(Count, data.Tasks.Count);
        Assert.Equal(tasks.Select(t => t with { Done = !t.Done }), data.Tasks);

        string report = $"10,000 tasks: enqueue {enqueueMs:F1} ms (caller thread), persist (enqueue + flush) {persistMs:F1} ms, " +
            $"re-upsert all {updateMs:F1} ms, Open+Load first {firstLoadMs:F1} ms / best {bestLoadMs:F1} ms " +
            $"(best Open {bestOpenMs:F1} ms, best Load {bestLoadOnlyMs:F1} ms)";
        output?.WriteLine(report);
        string? reportFile = Environment.GetEnvironmentVariable("DOBA_STORAGE_PERF_REPORT");
        if (!string.IsNullOrEmpty(reportFile))
        {
            await File.AppendAllTextAsync(reportFile, report + Environment.NewLine, Ct);
        }

        Assert.True(persistMs < 10_000, report);
        Assert.True(bestLoadMs < 2_000, report);
    }
}
