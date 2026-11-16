using System.Diagnostics;
using System.Globalization;
using Doba.Core.Model;
using Doba.Core.Projections;
using Doba.Core.Store;

namespace Doba.Core.Tests;

/// <summary>Runs alone, after the parallel tests, so timings are not skewed by other test threads.</summary>
[CollectionDefinition(nameof(SerialPerformance), DisableParallelization = true)]
public sealed class SerialPerformance;

/// <summary>Every projection must stay well under the 5 ms budget on a 10,000-task store (asserted with a ×4 CI margin).</summary>
[Collection(nameof(SerialPerformance))]
public class PerformanceTests(ITestOutputHelper output)
{
    private const int TaskCount = 10_000;
    private const int Runs = 20;
    private const double BudgetMs = 20;

    private static readonly DateOnly Today = new(2026, 11, 15);

    /// <summary>~3 years of tasks around today, ~10% multi-day, ~5% undated, random tags/priority/done, fixed seed.</summary>
    internal static TaskStore BuildStore()
    {
        var rnd = new Random(20261115);
        string?[] tags = [null, null, "work", "personal", "learning", "errands", "health"];
        var first = new DateOnly(2025, 6, 1);
        var tasks = new List<TaskItem>(TaskCount);
        for (var i = 1; i <= TaskCount; i++)
        {
            var roll = rnd.NextDouble();
            DateOnly? date = roll < 0.05 ? null : first.AddDays(rnd.Next(3 * 365));
            DateOnly? end = date is { } d && roll > 0.90 ? d.AddDays(1 + rnd.Next(9)) : null;
            TimeOnly? time = date is not null && end is null && rnd.Next(3) == 0 ? new TimeOnly(6 + rnd.Next(17), rnd.Next(12) * 5) : null;
            var done = date is { } dd && dd < Today ? rnd.Next(10) < 8 : rnd.Next(10) < 2;
            tasks.Add(new TaskItem(i, "Task " + i.ToString(CultureInfo.InvariantCulture) + (rnd.Next(4) == 0 ? " with a longer title that wraps" : string.Empty),
                string.Empty, date, end, time, time is null ? 0 : 15 * (1 + rnd.Next(8)), (Priority)rnd.Next(5), tags[rnd.Next(tags.Length)], done));
        }

        return new TaskStore(tasks, ["work", "personal", "learning", "errands", "health"], NullTaskPersistence.Instance);
    }

    [Fact]
    public void Projections_on_10k_tasks_stay_within_budget()
    {
        var store = BuildStore();
        Assert.Equal(TaskCount, store.Count);
        var ctx = new ProjectionContext(Today, new TimeOnly(22, 26), DayOfWeek.Monday, null);
        var filtered = ctx with { FilterTag = "work" };
        var week = WeekMetrics.Design();
        var month = new MonthMetrics();

        var cases = new (string Name, Func<object> Run)[]
        {
            ("Inbox", () => Projector.Inbox(store, ctx)),
            ("Today", () => Projector.Today(store, ctx)),
            ("Day", () => Projector.Day(store, ctx, Today)),
            ("Week", () => Projector.Week(store, ctx, Today, week)),
            ("Month", () => Projector.Month(store, ctx, Today, month)),
            ("Year", () => Projector.Year(store, ctx, Today.Year)),
            ("Sidebar", () => Projector.Sidebar(store, ctx)),
            ("Search", () => Projector.Search(store, ctx, "task 12")),
            ("Week (filtered)", () => Projector.Week(store, filtered, Today, week)),
        };

        var results = new List<(string Name, double Avg, double Max)>();
        foreach (var (name, run) in cases)
        {
            for (var i = 0; i < 10; i++)
            {
                GC.KeepAlive(run());
            }

            var sw = new Stopwatch();
            double max = 0;
            for (var i = 0; i < Runs; i++)
            {
                var t0 = sw.Elapsed;
                sw.Start();
                GC.KeepAlive(run());
                sw.Stop();
                max = Math.Max(max, (sw.Elapsed - t0).TotalMilliseconds);
            }

            results.Add((name, sw.Elapsed.TotalMilliseconds / Runs, max));
        }

        foreach (var (name, avg, max) in results)
        {
            var line = string.Create(CultureInfo.InvariantCulture, $"{name,-16} avg {avg,7:0.000} ms   max {max,7:0.000} ms   ({TaskCount} tasks, {Runs} runs)");
            output.WriteLine(line);
            Console.WriteLine("[perf] " + line);
        }

        Assert.All(results, r => Assert.True(r.Avg < BudgetMs, $"{r.Name}: {r.Avg:0.000} ms ≥ {BudgetMs} ms"));
    }
}
