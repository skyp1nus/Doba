using System.Diagnostics;
using Doba.App.Tests.Lifecycle;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Time;
using Doba.Storage;

namespace Doba.App.Tests.Performance;

/// <summary>
/// The databases <c>scripts/perf.ps1</c> launches the AOT build against: 348 demo-like tasks, 10,000 tasks, and the
/// 10,000 tasks plus a dense week (the Week view filled to capacity: about 40 cards) in comfortable and in compact
/// density. Normally written to a temp folder and checked; with <c>DOBA_PERF_SEED_DIR</c> set they are written there
/// (folders <c>db-348</c>, <c>db-10k</c>, <c>db-dense</c>, <c>db-dense-compact</c>, dated around the real today) for the
/// script to use.
/// </summary>
public sealed class PerfDatabaseTests
{
    [Fact]
    public async Task Perf_databases_hold_every_task_and_load_quickly()
    {
        var ct = TestContext.Current.CancellationToken;
        var target = Environment.GetEnvironmentVariable("DOBA_PERF_SEED_DIR");
        using var temp = string.IsNullOrEmpty(target) ? new TempFolder() : null;
        var root = temp?.Path ?? target!;
        var today = temp is null ? TimeProvider.System.Today() : DemoData.Today;

        var dense = PerfData.DenseWeek(10_000, today);
        (string Name, IReadOnlyList<TaskItem> Tasks, int Count, AppSettings? Settings)[] sets =
        [
            ("db-348", PerfData.DemoLike(today), 348, null),
            ("db-10k", PerfData.Many(10_000, today), 10_000, null),
            ("db-dense", dense, 10_000 + (7 * PerfData.DenseTasksPerDay) + PerfData.DenseBars, null),
            ("db-dense-compact", dense, 10_000 + (7 * PerfData.DenseTasksPerDay) + PerfData.DenseBars, new AppSettings { Density = Density.Compact }),
        ];
        foreach (var (name, tasks, count, settings) in sets)
        {
            Assert.Equal(count, tasks.Count);
            var folder = Path.Combine(root, name);
            await PerfData.WriteDatabaseAsync(folder, tasks, ct, settings);

            var watch = Stopwatch.StartNew();
            var db = DobaDatabase.Open(new DataLocation(folder));
            LoadedData data;
            try
            {
                data = db.Load();
            }
            finally
            {
                await db.DisposeAsync();
            }

            var ms = watch.Elapsed.TotalMilliseconds;
            TestContext.Current.TestOutputHelper?.WriteLine($"{name}: open + load {PerfData.F1(ms)} ms");
            Assert.Equal(tasks, data.Tasks);
            Assert.Equal(DemoData.Tags, data.Tags);
            Assert.Equal(settings ?? AppSettings.Default, data.Settings);
            Assert.True(ms < 3_000, $"{name}: open + load took {PerfData.F1(ms)} ms");
        }
    }

    [Fact]
    public void The_dense_week_fills_every_day_of_todays_week()
    {
        var today = DemoData.Today;
        var tasks = PerfData.DenseWeek(10_000, today);
        var monday = DateMath.WeekStart(today, DayOfWeek.Monday);

        for (var d = 0; d < 7; d++)
        {
            var day = monday.AddDays(d);
            Assert.True(tasks.Count(t => t.Date == day && t.End is null && !t.Done) >= PerfData.DenseTasksPerDay, $"{day}");
        }

        Assert.Equal(PerfData.DenseBars, tasks.Count(t => t.End is { } end && t.Date >= monday && end < monday.AddDays(7) && !t.Done && t.Id > 10_000));
        Assert.Equal(tasks.Count, tasks.Select(t => t.Id).Distinct().Count());
    }
}
