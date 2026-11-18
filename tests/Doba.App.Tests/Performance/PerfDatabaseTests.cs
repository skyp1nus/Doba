using System.Diagnostics;
using Doba.App.Tests.Lifecycle;
using Doba.Core.Demo;
using Doba.Core.Time;
using Doba.Storage;

namespace Doba.App.Tests.Performance;

/// <summary>
/// The databases <c>scripts/perf.ps1</c> launches the AOT build against (348 demo-like tasks, 10,000 tasks). Normally
/// written to a temp folder and checked; with <c>DOBA_PERF_SEED_DIR</c> set they are written there (folders
/// <c>db-348</c> and <c>db-10k</c>, dated around the real today) for the script to use.
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

        (string Name, int Count)[] sets = [("db-348", 348), ("db-10k", 10_000)];
        foreach (var (name, count) in sets)
        {
            var tasks = count == 348 ? PerfData.DemoLike(today) : PerfData.Many(count, today);
            Assert.Equal(count, tasks.Count);
            var folder = Path.Combine(root, name);
            await PerfData.WriteDatabaseAsync(folder, tasks, ct);

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
            Assert.True(ms < 3_000, $"{name}: open + load took {PerfData.F1(ms)} ms");
        }
    }
}
