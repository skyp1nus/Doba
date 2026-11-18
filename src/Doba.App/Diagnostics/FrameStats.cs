using System.Globalization;
using System.Text;

namespace Doba.App.Diagnostics;

/// <summary>Statistics of the intervals between consecutive frame timestamps (milliseconds).</summary>
/// <param name="Frames">Timestamps recorded.</param>
/// <param name="P50">Median interval (on an idle-free run: the display's refresh interval).</param>
/// <param name="P95">95th percentile interval (nearest rank).</param>
/// <param name="Max">Longest interval.</param>
/// <param name="Over16">Intervals longer than one 60 Hz frame (16.7 ms).</param>
/// <param name="Over33">Intervals longer than two 60 Hz frames (33.3 ms).</param>
/// <param name="Over50">Intervals longer than three 60 Hz frames (50 ms): a stall anyone would see.</param>
internal sealed record FrameStats(int Frames, double P50, double P95, double Max, int Over16, int Over33, int Over50)
{
    /// <summary>One 60 Hz frame, ms.</summary>
    public const double Frame60 = 1000.0 / 60;

    public static FrameStats Empty { get; } = new(0, double.NaN, double.NaN, double.NaN, 0, 0, 0);

    /// <summary>From ascending timestamps (ms); fewer than two give <see cref="Empty"/> with the frame count.</summary>
    public static FrameStats FromTimestamps(IReadOnlyList<double> timestamps)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        if (timestamps.Count < 2)
        {
            return Empty with { Frames = timestamps.Count };
        }

        var intervals = new double[timestamps.Count - 1];
        for (var i = 1; i < timestamps.Count; i++)
        {
            intervals[i - 1] = timestamps[i] - timestamps[i - 1];
        }

        Array.Sort(intervals);
        int over16 = 0, over33 = 0, over50 = 0;
        foreach (var d in intervals)
        {
            over16 += d > Frame60 ? 1 : 0;
            over33 += d > 2 * Frame60 ? 1 : 0;
            over50 += d > 3 * Frame60 ? 1 : 0;
        }

        return new FrameStats(timestamps.Count, Percentile(intervals, 0.50), Percentile(intervals, 0.95), intervals[^1],
            over16, over33, over50);
    }

    /// <summary>Nearest-rank percentile of an ascending list (NaN when empty).</summary>
    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        if (sorted.Count == 0)
        {
            return double.NaN;
        }

        var rank = (int)Math.Ceiling(p * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }

    /// <summary><c>prefix-frames=… prefix-p50-ms=… prefix-p95-ms=… prefix-max-ms=… prefix-over-16.7ms=… prefix-over-33.3ms=… prefix-over-50ms=…</c></summary>
    public string Format(string prefix)
    {
        var sb = new StringBuilder();
        sb.Append(prefix).Append("-frames=").Append(Frames.ToString(CultureInfo.InvariantCulture));
        sb.Append(' ').Append(prefix).Append("-p50-ms=").Append(PerfProbe.Ms(P50));
        sb.Append(' ').Append(prefix).Append("-p95-ms=").Append(PerfProbe.Ms(P95));
        sb.Append(' ').Append(prefix).Append("-max-ms=").Append(PerfProbe.Ms(Max));
        sb.Append(' ').Append(prefix).Append("-over-16.7ms=").Append(Over16.ToString(CultureInfo.InvariantCulture));
        sb.Append(' ').Append(prefix).Append("-over-33.3ms=").Append(Over33.ToString(CultureInfo.InvariantCulture));
        sb.Append(' ').Append(prefix).Append("-over-50ms=").Append(Over50.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
