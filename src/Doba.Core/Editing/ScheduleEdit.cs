using Doba.Core.Model;
using Doba.Core.Projections;
using Doba.Core.Time;

namespace Doba.Core.Editing;

/// <summary>What a schedule drag changes: the start (move, the block body) or the length (resize, the bottom edge).</summary>
public enum ScheduleDragMode
{
    Move,
    Resize,
}

/// <summary>A start and an effective duration in minutes.</summary>
public readonly record struct ScheduleSlot(int Start, int Duration)
{
    /// <summary>End of the visible block: start + max(duration, 30).</summary>
    public int VisualEnd => Start + Math.Max(Duration, ScheduleColumns.MinVisibleMinutes);
}

/// <summary>
/// The schedule's pure editing math, mirroring the prototype's pointer (<c>sbCompute</c>, <c>sbAuto</c>, <c>sbEnd</c>)
/// and keyboard (<c>sbKey</c>) handlers: snapping to the absolute 15-minute grid (5 with Alt), the 07:00 / 23:00 edges,
/// the 15-minute minimum length, the autoscroll speed, the texts, and how a result is written to a task.
/// <para>Rounding is JavaScript's <c>Math.round</c> (<c>Floor(x + 0.5)</c>). The nested min/max are kept exactly as the
/// prototype has them, never <see cref="Math.Clamp(int, int, int)"/>: their bounds cross for a task longer than 16 h
/// or a resize starting after 22:45, and the outer bound then wins.</para>
/// </summary>
public static class ScheduleEdit
{
    /// <summary>Snap step in minutes.</summary>
    public const int Step = 15;

    /// <summary>Snap step with Alt held.</summary>
    public const int FineStep = 5;

    /// <summary>Shortest length a resize or a Shift+arrow leaves.</summary>
    public const int MinDuration = 15;

    /// <summary>Earliest start: 07:00.</summary>
    public const int FirstMinute = ScheduleModel.FirstHour * 60;

    /// <summary>Latest end: 23:00.</summary>
    public const int LastMinute = ScheduleModel.LastHour * 60;

    /// <summary>A move starts after 4 px of pointer travel (DIP; the larger of |dx| and |dy|).</summary>
    public const double MoveThreshold = 4;

    /// <summary>A resize starts after 2 px.</summary>
    public const double ResizeThreshold = 2;

    /// <summary>Autoscroll zone at the top and bottom edge of the scroller.</summary>
    public const double AutoScrollZone = 40;

    /// <summary>Autoscroll speed bounds, px per frame.</summary>
    public const int AutoScrollMin = 2, AutoScrollMax = 14;

    /// <summary>JavaScript's Math.round.</summary>
    private static int Round(double x) => (int)Math.Floor(x + 0.5);

    /// <summary>The click / drag threshold: true once the pointer has travelled far enough for <paramref name="mode"/>.</summary>
    public static bool PastThreshold(ScheduleDragMode mode, double dx, double dy) =>
        Math.Max(Math.Abs(dx), Math.Abs(dy)) >= (mode == ScheduleDragMode.Resize ? ResizeThreshold : MoveThreshold);

    /// <summary>
    /// Where a drag lands (prototype <c>sbCompute</c>). <paramref name="deltaPixels"/> is the vertical travel in grid
    /// pixels (pointer delta plus the scroller's scroll since the press), at <paramref name="hourHeight"/> px per hour.
    /// Move: start = round((s0 + dm) / step) · step within [07:00, 23:00 − d0]; resize: the end the same way within
    /// [s0 + 15, 23:00].
    /// </summary>
    public static ScheduleSlot Drag(ScheduleDragMode mode, int start0, int duration0, double deltaPixels, bool fine,
        double hourHeight = ScheduleLayout.DesignHourHeight)
    {
        var dm = deltaPixels / hourHeight * 60;
        var step = fine ? FineStep : Step;
        if (mode == ScheduleDragMode.Move)
        {
            var ns = Math.Max(FirstMinute, Math.Min(LastMinute - duration0, Round((start0 + dm) / step) * step));
            return new ScheduleSlot(ns, duration0);
        }

        var end = Math.Max(start0 + MinDuration, Math.Min(LastMinute, Round((start0 + duration0 + dm) / step) * step));
        return new ScheduleSlot(start0, end - start0);
    }

    /// <summary>
    /// One arrow-key step (prototype <c>sbKey</c>): ↑/↓ moves the start to the previous / next grid line (15 min, 5 with
    /// Alt — from 09:37 ↓ goes to 09:45, ↑ to 09:30); Shift moves the end instead (length at least 15 min, end at most
    /// 23:00). Returns null when nothing changes (at an edge).
    /// </summary>
    /// <param name="down">↓ (later) or ↑ (earlier).</param>
    public static ScheduleSlot? Key(int start, int duration, bool down, bool shift, bool fine)
    {
        var step = fine ? FineStep : Step;
        int To(int v) => down
            ? (int)Math.Floor((double)v / step) * step + step
            : (int)Math.Ceiling((double)v / step) * step - step;

        var ns = start;
        var nd = duration;
        if (shift)
        {
            nd = Math.Max(MinDuration, Math.Min(LastMinute - start, To(start + duration) - start));
        }
        else
        {
            ns = Math.Max(FirstMinute, Math.Min(LastMinute - duration, To(start)));
        }

        return ns == start && nd == duration ? null : new ScheduleSlot(ns, nd);
    }

    /// <summary>Which time the chip in the hour column shows: the new start (move) or the new end (resize).</summary>
    public static int ChipMinutes(ScheduleDragMode mode, ScheduleSlot slot) =>
        mode == ScheduleDragMode.Move ? slot.Start : slot.Start + slot.Duration;

    /// <summary>
    /// A block at <paramref name="start"/> lies under the dragged copy at <paramref name="candidate"/> (both by their
    /// visual length), so it dims to 0.55.
    /// </summary>
    public static bool UnderCopy(int start, int duration, ScheduleSlot candidate) =>
        start < candidate.VisualEnd && candidate.Start < start + Math.Max(duration, ScheduleColumns.MinVisibleMinutes);

    /// <summary>
    /// Autoscroll speed in px per frame (prototype <c>sbAuto</c>) for a pointer at <paramref name="y"/> over a scroller
    /// spanning [<paramref name="top"/>, <paramref name="bottom"/>]: inside the 40 px zone at an edge, 2..14 in proportion
    /// to the depth (14 at and beyond the edge), negative at the top; 0 elsewhere. <paramref name="scale"/> scales the
    /// zone (the prototype's CSS zoom; 1 in DIP).
    /// </summary>
    public static int AutoScrollSpeed(double y, double top, double bottom, double scale = 1)
    {
        var z = AutoScrollZone * scale;
        if (y < top + z)
        {
            return -Math.Max(AutoScrollMin, Round(AutoScrollMax * Math.Min(1, (top + z - y) / z)));
        }

        if (y > bottom - z)
        {
            return Math.Max(AutoScrollMin, Round(AutoScrollMax * Math.Min(1, (y - bottom + z) / z)));
        }

        return 0;
    }

    /// <summary>"1h 45m", "1h", "45m" (prototype <c>durLabel</c>).</summary>
    public static string DurationLabel(int minutes)
    {
        var h = minutes / 60;
        var r = minutes % 60;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return string.Concat(
            h != 0 ? h.ToString(inv) + "h" : string.Empty,
            h != 0 && r != 0 ? " " : string.Empty,
            r != 0 ? r.ToString(inv) + "m" : string.Empty);
    }

    /// <summary>"09:30 – 11:15 · 1h 45m": the range of the dragged copy or the block being resized.</summary>
    public static string ActiveRange(int start, int duration) =>
        string.Concat(DateMath.Hhmm(start), " – ", DateMath.Hhmm(start + duration), " · ", DurationLabel(duration));

    /// <summary>"Moved Title, 11:15 to 12:15" / "Resized Title, 09:30 to 11:15" (the live region after a change).</summary>
    public static string ChangedMessage(ScheduleDragMode mode, string title, ScheduleSlot slot) =>
        string.Concat(mode == ScheduleDragMode.Move ? "Moved " : "Resized ", title, ", ", DateMath.Hhmm(slot.Start), " to ",
            DateMath.Hhmm(slot.Start + slot.Duration));

    /// <summary>The live region after Esc or another cancel.</summary>
    public const string CancelledMessage = "Move cancelled";

    /// <summary>The effective duration the schedule draws: 0 (unspecified) → 30.</summary>
    public static int EffectiveDuration(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.DurationMinutes > 0 ? task.DurationMinutes : ScheduleColumns.MinVisibleMinutes;
    }

    /// <summary>
    /// The task after a drop or a key step at <paramref name="slot"/>, or null when it lands on the slot it started from
    /// (nothing to write). A move changes only the time, so an unspecified duration (0) stays unspecified; a resize writes
    /// the real length.
    /// </summary>
    public static TaskItem? Apply(TaskItem task, ScheduleDragMode mode, ScheduleSlot slot)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentOutOfRangeException.ThrowIfNegative(slot.Start);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot.Start, 24 * 60);
        if (task.Time is not { } time)
        {
            return null;
        }

        if (slot.Start == DateMath.Minutes(time) && slot.Duration == EffectiveDuration(task))
        {
            return null;
        }

        var moved = task with { Time = new TimeOnly(slot.Start / 60, slot.Start % 60) };
        return mode == ScheduleDragMode.Move ? moved : moved with { DurationMinutes = slot.Duration };
    }
}
