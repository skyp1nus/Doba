using Doba.App.Diagnostics;

namespace Doba.App.Tests.Performance;

/// <summary>What the perf scenarios compute from recorded frames: frames inside fade windows (overlay scenario) and what
/// the long frames had in them (week scenario).</summary>
public sealed class FrameAnalysisTests
{
    [Fact]
    public void Intervals_ending_in_a_window_include_the_first_frame_after_it_opens()
    {
        // Intervals: 10 (ends 10), 30 (ends 40), 5 (45), 55 (100), 10 (110).
        double[] frames = [0, 10, 40, 45, 100, 110];

        // A frame exactly at a window's start (110 for the third window) came before the work: not counted for it.
        var inside = FrameStats.IntervalsEndingIn(frames, [(20, 45), (95, 105), (110, 200)]);

        Assert.Equal(new double[] { 30, 5, 55 }, inside);
    }

    [Fact]
    public void Stats_from_intervals_match_stats_from_timestamps()
    {
        double[] frames = [0, 10, 20, 40, 80, 140];

        var fromIntervals = FrameStats.FromIntervals([60, 10, 40, 20, 10], 6);

        Assert.Equal(FrameStats.FromTimestamps(frames), fromIntervals);
        Assert.Equal(FrameStats.Empty with { Frames = 3 }, FrameStats.FromIntervals([], 3));
    }

    [Fact]
    public void Long_frames_are_attributed_to_collections_and_step_starts()
    {
        // Intervals: 10, 40 (GC), 40 (step at 55), 10, 50 (GC and step at 120), 40 (neither; ends at 180, outside).
        double[] frames = [0, 10, 50, 90, 100, 150, 190];
        int[] gcs = [0, 0, 1, 1, 1, 2, 2];

        var found = LongFrames.Find(frames, gcs, [55, 120], from: 0, to: 180, threshold: 33.3);

        Assert.Equal(new LongFrames(3, 2, 2), found);
        Assert.Equal("ui-long=3 ui-long-gc=2 ui-long-step=2", found.Format("ui"));
    }

    [Fact]
    public void Long_frames_need_one_gc_count_per_frame() =>
        Assert.Throws<ArgumentException>(() => LongFrames.Find([0, 50], [0], [], 0, 100, 33.3));
}
