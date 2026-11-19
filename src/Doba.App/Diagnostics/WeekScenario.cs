using System.Diagnostics;
using System.Globalization;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Shell;
using Doba.Core.Navigation;
using Doba.Core.Store;
using Doba.Core.Time;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Diagnostics;

/// <summary>
/// <c>--perf-scenario week</c>: opens the Week view on the busiest week near today, then repeats next / previous week,
/// Inbox and back to the calendar, Month and back to Week, through the shell's public navigation API (so it plays
/// whatever content transitions are installed), while <see cref="FrameMeter"/> records frames on both threads.
/// <para>Besides the frame statistics the line carries what the long frames (over two 60 Hz frames) had in them
/// (<see cref="LongFrames"/>: a GC, a step start), the KB allocated per warm step in round order (next, previous,
/// Inbox, calendar, Month, Week: <c>warm-step-alloc-kb</c>), the GC counts (<c>gc0</c> counts every collection,
/// <c>gc1</c> those of gen 1 or 2, <c>gc2</c> those of gen 2) and the density the Week view was drawn in.</para>
/// </summary>
internal static class WeekScenario
{
    /// <summary>Default number of rounds (each round is <see cref="StepsPerRound"/> navigations).</summary>
    public const int DefaultRounds = 5;

    public const int StepsPerRound = 6;

    /// <summary>Weeks opened before the measurement to find the one that shows the most cards.</summary>
    public const int Candidates = 6;

    /// <summary>The wait after each navigation: the longest transition (zoom 460 ms plus the 130 ms exit) has ended.</summary>
    public static int StepMs => AppMotion.Zoom + AppMotion.Exit;

    /// <summary>Runs the scenario and returns its <c>doba-perf scenario=week …</c> line.</summary>
    public static async Task<string> RunAsync(MainWindow window, int rounds)
    {
        ArgumentNullException.ThrowIfNull(window);
        var vm = window.ViewModel ?? throw new InvalidOperationException("The main window has no view-model.");
        rounds = rounds > 0 ? rounds : DefaultRounds;
        var app = vm.App;
        // The week showing the most cards: of the weeks with the most single-day tasks, the one that actually draws the
        // most (multi-day bars and "+N more" decide how many fit), found by opening each before measuring.
        DateOnly anchor = default;
        int cards = -1, bars = 0, tasksInWeek = 0;
        foreach (var (week, dayTasks) in BusiestWeeks(app.Store, app.DayClock.Today, app.Settings.Current.WeekStart, Candidates))
        {
            vm.ZoomTo(AppView.Week, week);
            await Task.Delay(StepMs).ConfigureAwait(true);
            var (c, b) = CountWeekItems(window);
            if (c > cards)
            {
                (anchor, cards, bars, tasksInWeek) = (week, c, b, dayTasks);
            }
        }

        vm.ZoomTo(AppView.Week, anchor);
        await Task.Delay(StepMs).ConfigureAwait(true);

        var steps = new List<double>(rounds * StepsPerRound);
        var stepStarts = new List<double>(rounds * StepsPerRound);
        // Bytes allocated from one step's start to the next (its call, transition, layout and rendering), per step of
        // the round, over the warm rounds only (first visits build views).
        var warmAllocated = new long[StepsPerRound];
        Action[] round =
        [
            () => vm.Shift(1),
            () => vm.Shift(-1),
            () => vm.GoSection(AppView.Inbox),
            vm.GoCalendar,
            () => vm.SetZoomLevel(AppView.Month),
            () => vm.SetZoomLevel(AppView.Week),
        ];

        var meter = new FrameMeter(window);
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        var gcPause = GC.GetTotalPauseDuration();
        var allocated = GC.GetTotalAllocatedBytes();
        meter.Start();
        var start = FrameMeter.Now;
        var warmFrom = double.NaN;
        for (var r = 0; r < rounds; r++)
        {
            if (r == 1)
            {
                warmFrom = FrameMeter.Now;
            }

            for (var s = 0; s < round.Length; s++)
            {
                var bytes = GC.GetTotalAllocatedBytes();
                stepStarts.Add(FrameMeter.Now);
                var t0 = Stopwatch.GetTimestamp();
                round[s]();
                steps.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                await Task.Delay(StepMs).ConfigureAwait(true);
                if (r > 0)
                {
                    warmAllocated[s] += GC.GetTotalAllocatedBytes() - bytes;
                }
            }
        }

        var end = FrameMeter.Now;
        await meter.StopAsync().ConfigureAwait(true);

        // GC activity inside the measured window (a blocking GC stops both threads), then the live managed heap once
        // every visited screen exists (a full collection, taken after the measurement).
        var gc = string.Create(CultureInfo.InvariantCulture,
            $"gc0={GC.CollectionCount(0) - gc0} gc1={GC.CollectionCount(1) - gc1} gc2={GC.CollectionCount(2) - gc2} " +
            $"gc-pause-ms={(GC.GetTotalPauseDuration() - gcPause).TotalMilliseconds:F1} " +
            $"allocated-mb={(GC.GetTotalAllocatedBytes() - allocated) / (1024.0 * 1024.0):F1} " +
            $"live-heap-mb={GC.GetTotalMemory(forceFullCollection: true) / (1024.0 * 1024.0):F1}");
        string memory;
        using (var process = Process.GetCurrentProcess())
        {
            memory = string.Create(CultureInfo.InvariantCulture,
                $"working-set-mb={process.WorkingSet64 / (1024.0 * 1024.0):F1} private-mb={process.PrivateMemorySize64 / (1024.0 * 1024.0):F1}");
        }
        steps.Sort();

        // "warm" = rounds 2+: every view already exists, so only the transitions and re-projections remain.
        var ui = FrameStats.FromTimestamps(FrameMeter.Between(meter.UiFrames, start, end));
        var render = FrameStats.FromTimestamps(FrameMeter.Between(meter.RenderFrames, start, end));
        var warm = double.IsNaN(warmFrom) ? "" : string.Concat(
            " ", FrameStats.FromTimestamps(FrameMeter.Between(meter.UiFrames, warmFrom, end)).Format("ui-warm"),
            " ", FrameStats.FromTimestamps(FrameMeter.Between(meter.RenderFrames, warmFrom, end)).Format("render-warm"));
        // Long frames (over two 60 Hz frames) and what was in them.
        var uiLong = LongFrames.Find(meter.UiFrames, meter.UiGcCounts, stepStarts, start, end, 2 * FrameStats.Frame60);
        var renderLong = LongFrames.Find(meter.RenderFrames, meter.RenderGcCounts, stepStarts, start, end, 2 * FrameStats.Frame60);
        var warmRounds = Math.Max(rounds - 1, 1);
        var alloc = string.Join(',', warmAllocated.Select(b => (b / 1024.0 / warmRounds).ToString("F0", CultureInfo.InvariantCulture)));
        return string.Create(CultureInfo.InvariantCulture,
            $"doba-perf scenario=week rounds={rounds} steps={steps.Count} step-wait-ms={StepMs} seconds={(end - start) / 1000:F1} " +
            $"anchor={anchor:yyyy-MM-dd} day-tasks-in-week={tasksInWeek} cards={cards} bars={bars} density={app.Settings.Current.Density} " +
            $"{ui.Format("ui")} {render.Format("render")}{warm} {uiLong.Format("ui")} {renderLong.Format("render")} " +
            $"step-call-p50-ms={PerfProbe.Ms(FrameStats.Percentile(steps, 0.5))} step-call-max-ms={PerfProbe.Ms(steps.Count > 0 ? steps[^1] : double.NaN)} " +
            $"warm-step-alloc-kb={alloc} {gc} {memory}");
    }

    /// <summary>
    /// The <paramref name="count"/> weeks (week starts) within half a year of <paramref name="today"/> with the most
    /// single-day tasks (each can become a card), busiest first; ties go to the week nearest today.
    /// </summary>
    public static IReadOnlyList<(DateOnly Week, int DayTasks)> BusiestWeeks(ITaskReader store, DateOnly today, DayOfWeek weekStart, int count)
    {
        ArgumentNullException.ThrowIfNull(store);
        var current = DateMath.WeekStart(today, weekStart);
        var weeks = new List<(DateOnly Week, int DayTasks, int Distance)>();
        for (var offset = -26; offset <= 26; offset++)
        {
            var start = current.AddDays(7 * offset);
            var tasks = 0;
            for (var d = 0; d < 7; d++)
            {
                tasks += store.OnDate(start.AddDays(d)).Count;
            }

            weeks.Add((start, tasks, Math.Abs(offset)));
        }

        return [.. weeks.OrderByDescending(w => w.DayTasks).ThenBy(w => w.Distance).Take(count).Select(w => (w.Week, w.DayTasks))];
    }

    /// <summary>Task cards and span bars currently shown (effectively visible) in the window.</summary>
    private static (int Cards, int Bars) CountWeekItems(MainWindow window)
    {
        int cards = 0, bars = 0;
        foreach (var visual in window.GetVisualDescendants())
        {
            if (visual is TaskCard { IsEffectivelyVisible: true })
            {
                cards++;
            }
            else if (visual is SpanBarButton { IsEffectivelyVisible: true })
            {
                bars++;
            }
        }

        return (cards, bars);
    }
}
