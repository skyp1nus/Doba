using System.Globalization;

namespace Doba.App.Diagnostics;

/// <summary>
/// What the long frames of a run had in them: how many intervals were longer than the threshold, how many of those
/// contained a garbage collection (the GC count went up between the two frames; a blocking GC stops every managed
/// thread, the render thread too) and how many contained the start of a scenario step (a navigation's synchronous work:
/// re-projection, the swap, a view built on first visit). A long frame can have both or neither (layout, rendering).
/// </summary>
/// <param name="Count">Intervals longer than the threshold.</param>
/// <param name="WithGc">Of those, intervals with at least one GC.</param>
/// <param name="WithStep">Of those, intervals in which a step started.</param>
internal readonly record struct LongFrames(int Count, int WithGc, int WithStep)
{
    /// <summary>
    /// Long intervals among <paramref name="times"/> (ascending, ms) whose later frame is in [<paramref name="from"/>,
    /// <paramref name="to"/>). <paramref name="gcCounts"/>[i] is the GC count read at frame i (same length as
    /// <paramref name="times"/>); <paramref name="stepStarts"/> are the step start times (ms, any order).
    /// </summary>
    public static LongFrames Find(IReadOnlyList<double> times, IReadOnlyList<int> gcCounts, IReadOnlyList<double> stepStarts,
        double from, double to, double threshold)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(gcCounts);
        ArgumentNullException.ThrowIfNull(stepStarts);
        if (gcCounts.Count != times.Count)
        {
            throw new ArgumentException("One GC count per frame is required.", nameof(gcCounts));
        }

        int count = 0, withGc = 0, withStep = 0;
        for (var i = 1; i < times.Count; i++)
        {
            var (start, end) = (times[i - 1], times[i]);
            if (end < from || end >= to || end - start <= threshold)
            {
                continue;
            }

            count++;
            withGc += gcCounts[i] > gcCounts[i - 1] ? 1 : 0;
            foreach (var s in stepStarts)
            {
                if (s > start && s <= end)
                {
                    withStep++;
                    break;
                }
            }
        }

        return new LongFrames(count, withGc, withStep);
    }

    /// <summary><c>prefix-long=… prefix-long-gc=… prefix-long-step=…</c></summary>
    public string Format(string prefix) => string.Create(CultureInfo.InvariantCulture,
        $"{prefix}-long={Count} {prefix}-long-gc={WithGc} {prefix}-long-step={WithStep}");
}
