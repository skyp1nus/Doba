namespace Doba.Core.Projections;

/// <summary>One timed task as the overlap layout sees it.</summary>
/// <param name="Id">Task id (the final tie-breaker of the order).</param>
/// <param name="Start">Minutes since midnight.</param>
/// <param name="DurationMinutes">Real duration; the layout occupies at least <see cref="ScheduleColumns.MinVisibleMinutes"/>.</param>
public readonly record struct ScheduleSpan(long Id, int Start, int DurationMinutes)
{
    /// <summary>The visual end: start + max(duration, 30) — a block is never shorter than 24 px ≈ 30 min.</summary>
    public int End => Start + Math.Max(DurationMinutes, ScheduleColumns.MinVisibleMinutes);
}

/// <summary>Where a block goes in its overlap cluster.</summary>
/// <param name="Column">0-based column.</param>
/// <param name="Span">Columns the block covers (it widens over free columns to its right).</param>
/// <param name="Columns">Columns in the cluster (1..max).</param>
public readonly record struct ColumnPlacement(int Column, int Span, int Columns)
{
    /// <summary>A block folded into its cluster's "+N" overflow (not drawn).</summary>
    public static ColumnPlacement Hidden { get; } = new(-1, 0, 0);

    /// <summary>True for <see cref="Hidden"/>.</summary>
    public bool IsHidden => Span == 0;
}

/// <summary>The "+N" stand-in of a cluster that needs more than the maximum number of columns.</summary>
/// <param name="Column">The last column (max − 1).</param>
/// <param name="Columns">The maximum.</param>
/// <param name="Start">Earliest start of the hidden blocks.</param>
/// <param name="End">Latest visual end of the hidden blocks.</param>
/// <param name="Ids">The hidden blocks in layout order (start, longer first, id).</param>
public sealed record ColumnOverflow(int Column, int Columns, int Start, int End, IReadOnlyList<long> Ids);

/// <summary>Result of <see cref="ScheduleColumns.Layout"/>.</summary>
/// <param name="Placements">Every input id → its placement (<see cref="ColumnPlacement.IsHidden"/> under a "+N").</param>
/// <param name="Overflows">One per cluster wider than the maximum, in time order.</param>
public sealed record ColumnLayout(IReadOnlyDictionary<long, ColumnPlacement> Placements, IReadOnlyList<ColumnOverflow> Overflows);

/// <summary>
/// The schedule's overlap layout, mirroring the prototype's <c>layoutDay</c>: blocks that overlap (touching edges do not)
/// form clusters; each block takes the first free column of its cluster; a cluster with more than
/// <c>maxColumns</c> columns folds every block of column max − 1 and beyond into one overflow; a visible block widens
/// over the free columns to its right (the overflow counts as occupied). "Done" plays no part, so marking a task done
/// never moves blocks.
/// </summary>
public static class ScheduleColumns
{
    /// <summary>The design's maximum: three columns.</summary>
    public const int MaxColumns = 3;

    /// <summary>A block occupies at least 30 min (its 24 px minimum height).</summary>
    public const int MinVisibleMinutes = 30;

    /// <summary>The prototype's sort: start ascending, then longer (visual length) first, then id.</summary>
    public static int Compare(ScheduleSpan a, ScheduleSpan b)
    {
        var c = a.Start.CompareTo(b.Start);
        if (c != 0)
        {
            return c;
        }

        c = (b.End - b.Start).CompareTo(a.End - a.Start);
        return c != 0 ? c : a.Id.CompareTo(b.Id);
    }

    public static ColumnLayout Layout(IReadOnlyList<ScheduleSpan> items, int maxColumns = MaxColumns)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxColumns, 1);

        var sorted = items.ToArray();
        Array.Sort(sorted, Compare);

        var placements = new Dictionary<long, ColumnPlacement>(sorted.Length);
        var overflows = new List<ColumnOverflow>();
        var cluster = new List<Placed>();
        var colEnds = new List<int>();
        var clusterEnd = int.MinValue;
        foreach (var it in sorted)
        {
            if (cluster.Count > 0 && it.Start >= clusterEnd)
            {
                Close(cluster, colEnds.Count, maxColumns, placements, overflows);
                cluster.Clear();
                colEnds.Clear();
            }

            if (cluster.Count == 0)
            {
                clusterEnd = it.End;
            }

            var c = 0;
            while (c < colEnds.Count && colEnds[c] > it.Start)
            {
                c++;
            }

            if (c == colEnds.Count)
            {
                colEnds.Add(it.End);
            }
            else
            {
                colEnds[c] = it.End;
            }

            clusterEnd = Math.Max(clusterEnd, it.End);
            cluster.Add(new Placed(it.Id, it.Start, it.End, c));
        }

        if (cluster.Count > 0)
        {
            Close(cluster, colEnds.Count, maxColumns, placements, overflows);
        }

        return new ColumnLayout(placements, overflows);
    }

    private static void Close(List<Placed> cluster, int columns, int maxColumns, Dictionary<long, ColumnPlacement> placements,
        List<ColumnOverflow> overflows)
    {
        var n = columns;
        var hiddenFrom = int.MaxValue; // first hidden column; none without an overflow
        Placed? ov = null;
        if (n > maxColumns)
        {
            n = maxColumns;
            hiddenFrom = maxColumns - 1;
            var start = int.MaxValue;
            var end = int.MinValue;
            var ids = new List<long>();
            foreach (var x in cluster)
            {
                if (x.Column >= hiddenFrom)
                {
                    start = Math.Min(start, x.Start);
                    end = Math.Max(end, x.End);
                    ids.Add(x.Id);
                    placements[x.Id] = ColumnPlacement.Hidden;
                }
            }

            ov = new Placed(0, start, end, hiddenFrom);
            overflows.Add(new ColumnOverflow(hiddenFrom, n, start, end, ids));
        }

        foreach (var x in cluster)
        {
            if (x.Column >= hiddenFrom)
            {
                continue;
            }

            var span = 1;
            while (x.Column + span < n && !Busy(cluster, hiddenFrom, ov, x.Column + span, x.Start, x.End))
            {
                span++;
            }

            placements[x.Id] = new ColumnPlacement(x.Column, span, n);
        }
    }

    /// <summary>Column <paramref name="c"/> holds a visible block or the overflow somewhere in [s, e).</summary>
    private static bool Busy(List<Placed> cluster, int hiddenFrom, Placed? ov, int c, int s, int e)
    {
        foreach (var o in cluster)
        {
            if (o.Column < hiddenFrom && o.Column == c && o.Start < e && s < o.End)
            {
                return true;
            }
        }

        return ov is { } v && v.Column == c && v.Start < e && s < v.End;
    }

    private readonly record struct Placed(long Id, int Start, int End, int Column);
}
