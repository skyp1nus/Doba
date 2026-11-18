using Doba.App.Diagnostics;

namespace Doba.App.Tests.Performance;

/// <summary>The perf flags (<c>--perf-idle-seconds</c>, <c>--perf-scenario</c>, <c>--perf-rounds</c>) and the frame
/// statistics the scenario prints.</summary>
public sealed class PerfFlagsTests
{
    [Fact]
    public void Plain_perf_keeps_its_old_meaning()
    {
        var flags = StartupFlags.Parse(["--perf", "--exit-after-first-frame"]);

        Assert.True(flags.Perf);
        Assert.True(flags.ExitAfterFirstFrame);
        Assert.Equal(0, flags.PerfIdleSeconds);
        Assert.Null(flags.PerfScenario);
        Assert.True(flags.ExitsAfterPerf);
    }

    [Fact]
    public void Perf_alone_does_not_end_the_app()
    {
        var flags = StartupFlags.Parse(["--perf", "--demo"]);

        Assert.True(flags.Perf);
        Assert.False(flags.ExitsAfterPerf);
    }

    [Fact]
    public void Idle_seconds_imply_perf_and_an_exit()
    {
        var flags = StartupFlags.Parse(["--demo", "--perf-idle-seconds", "5"]);

        Assert.True(flags.Perf);
        Assert.Equal(5, flags.PerfIdleSeconds);
        Assert.True(flags.ExitsAfterPerf);
        Assert.True(flags.Demo);
    }

    [Fact]
    public void The_week_scenario_implies_perf_and_an_exit_and_takes_rounds()
    {
        var flags = StartupFlags.Parse(["--PERF-SCENARIO", "Week", "--perf-rounds", "7", "--data", @"D:\x"]);

        Assert.True(flags.Perf);
        Assert.Equal(StartupFlags.WeekScenario, flags.PerfScenario);
        Assert.Equal(7, flags.PerfRounds);
        Assert.Equal(@"D:\x", flags.DataFolder);
        Assert.True(flags.ExitsAfterPerf);
    }

    [Theory]
    [InlineData("--perf-idle-seconds", "0")]
    [InlineData("--perf-idle-seconds", "-3")]
    [InlineData("--perf-idle-seconds", "601")]
    [InlineData("--perf-idle-seconds", "five")]
    [InlineData("--perf-idle-seconds", "2.5")]
    [InlineData("--perf-scenario", "month")]
    [InlineData("--perf-rounds", "0")]
    [InlineData("--perf-rounds", "101")]
    public void Invalid_values_are_ignored(string flag, string value)
    {
        var flags = StartupFlags.Parse([flag, value, "--demo"]);

        Assert.Equal(StartupFlags.None with { Demo = true }, flags);
    }

    [Theory]
    [InlineData("--perf-idle-seconds")]
    [InlineData("--perf-scenario")]
    [InlineData("--perf-rounds")]
    public void A_value_flag_at_the_end_is_ignored(string flag) =>
        Assert.Equal(StartupFlags.None, StartupFlags.Parse([flag]));

    [Fact]
    public void A_value_is_consumed_and_not_read_as_a_flag()
    {
        // "--demo" here is the (invalid) idle value, not the demo flag.
        var flags = StartupFlags.Parse(["--perf-idle-seconds", "--demo"]);

        Assert.False(flags.Demo);
        Assert.Equal(0, flags.PerfIdleSeconds);
    }

    [Fact]
    public void Frame_stats_count_intervals_against_60_Hz_frames()
    {
        // Intervals: 10, 10, 20, 40, 60.
        double[] timestamps = [0, 10, 20, 40, 80, 140];

        var stats = FrameStats.FromTimestamps(timestamps);

        Assert.Equal(6, stats.Frames);
        Assert.Equal(20, stats.P50);
        Assert.Equal(60, stats.P95);
        Assert.Equal(60, stats.Max);
        Assert.Equal(3, stats.Over16);
        Assert.Equal(2, stats.Over33);
        Assert.Equal(1, stats.Over50);
        Assert.Equal("ui-frames=6 ui-p50-ms=20.0 ui-p95-ms=60.0 ui-max-ms=60.0 ui-over-16.7ms=3 ui-over-33.3ms=2 ui-over-50ms=1", stats.Format("ui"));
    }

    [Fact]
    public void Frame_stats_of_fewer_than_two_frames_are_empty()
    {
        var stats = FrameStats.FromTimestamps([5]);

        Assert.Equal(1, stats.Frames);
        Assert.True(double.IsNaN(stats.P50));
        Assert.Contains("x-p50-ms=n/a", stats.Format("x"), StringComparison.Ordinal);
    }

    [Fact]
    public void Frames_between_takes_the_half_open_window() =>
        Assert.Equal(new double[] { 2, 3 }, FrameMeter.Between([1, 2, 3, 4], 2, 4));

    [Fact]
    public void Probe_marks_record_only_when_enabled()
    {
        PerfProbe.Reset();
        try
        {
            PerfProbe.Mark("ignored");
            Assert.Empty(PerfProbe.Marks);

            PerfProbe.Enable();
            PerfProbe.Mark("a");
            PerfProbe.MarkAt("b", 12.34);

            Assert.Equal(new[] { "a", "b" }, PerfProbe.Marks.Select(m => m.Name));
            Assert.Equal(12.34, PerfProbe.At("b"));
            Assert.True(double.IsNaN(PerfProbe.At("missing")));
            Assert.StartsWith("doba-perf marks a=", PerfProbe.FormatMarks(), StringComparison.Ordinal);
            Assert.EndsWith(" b=12.3", PerfProbe.FormatMarks(), StringComparison.Ordinal);
            // The process started before Main, so the measured span is positive (or n/a where unavailable).
            Assert.True(double.IsNaN(PerfProbe.ProcessToMainMs) || PerfProbe.ProcessToMainMs >= 0);
        }
        finally
        {
            PerfProbe.Reset();
        }
    }
}
