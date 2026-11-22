using Doba.Core.Editing;
using Doba.Core.Model;

namespace Doba.Core.Tests;

/// <summary>
/// The schedule's drag, resize and arrow-key math (<see cref="ScheduleEdit"/>): 48 px per hour, so 12 px per 15-minute
/// step and 4 px per 5-minute (Alt) step. The golden tests run the same tables through the prototype's JavaScript.
/// </summary>
public class ScheduleEditTests
{
    private const int NineThirty = 9 * 60 + 30;
    private const int NineThirtySeven = 9 * 60 + 37;
    private const ScheduleDragMode Move = ScheduleDragMode.Move;
    private const ScheduleDragMode Resize = ScheduleDragMode.Resize;

    private static ScheduleSlot Slot(int start, int dur) => new(start, dur);

    // ---------- threshold ----------

    [Theory]
    [InlineData(Move, 3.9, 0, false)]
    [InlineData(Move, 0, 3.9, false)]
    [InlineData(Move, 3, 3, false)] // the larger of |dx| and |dy|, not the distance
    [InlineData(Move, 4, 0, true)]
    [InlineData(Move, 0, -4, true)]
    [InlineData(Move, -2, 4.5, true)]
    [InlineData(Resize, 1.9, 1.9, false)]
    [InlineData(Resize, 0, 2, true)]
    [InlineData(Resize, -2, 0, true)]
    public void Click_or_drag_threshold(ScheduleDragMode mode, double dx, double dy, bool drag)
    {
        Assert.Equal(drag, ScheduleEdit.PastThreshold(mode, dx, dy));
    }

    // ---------- move ----------

    [Theory]
    [InlineData(0, NineThirty)]
    [InlineData(5.9, NineThirty)]          // 7.4 min: still 09:30
    [InlineData(6, NineThirty + 15)]       // exactly half a step rounds up (Math.round), not to even
    [InlineData(12, NineThirty + 15)]
    [InlineData(-6, NineThirty)]           // 37.5 steps → 38 = 09:30
    [InlineData(-6.1, NineThirty - 15)]
    [InlineData(-12, NineThirty - 15)]
    [InlineData(18, NineThirty + 30)]      // 22.5 min → 39.5 steps → 40 = 10:00
    [InlineData(100, NineThirty + 120)]    // 125 min → 11:35 → 11:30
    public void A_move_snaps_to_15_minutes(double dy, int start)
    {
        Assert.Equal(Slot(start, 60), ScheduleEdit.Drag(Move, NineThirty, 60, dy, fine: false));
    }

    [Theory]
    [InlineData(0, NineThirty)]
    [InlineData(1.9, NineThirty)]
    [InlineData(2, NineThirty + 5)]  // 2.5 min = half a 5-minute step
    [InlineData(4, NineThirty + 5)]
    [InlineData(-4, NineThirty - 5)]
    [InlineData(12, NineThirty + 15)]
    public void With_Alt_a_move_snaps_to_5_minutes(double dy, int start)
    {
        Assert.Equal(Slot(start, 60), ScheduleEdit.Drag(Move, NineThirty, 60, dy, fine: true));
    }

    [Fact]
    public void The_grid_is_absolute_not_relative_to_the_old_time()
    {
        // From 09:37 the copy snaps to :30 / :45 at once, not to 09:37 ± 15.
        Assert.Equal(Slot(NineThirty, 30), ScheduleEdit.Drag(Move, NineThirtySeven, 30, 0, fine: false));
        Assert.Equal(Slot(NineThirty + 15, 30), ScheduleEdit.Drag(Move, NineThirtySeven, 30, 12, fine: false));
        Assert.Equal(Slot(NineThirty + 5, 30), ScheduleEdit.Drag(Move, NineThirtySeven, 30, 0, fine: true));
    }

    [Fact]
    public void A_move_stops_at_07_00_and_23_00()
    {
        Assert.Equal(Slot(7 * 60, 60), ScheduleEdit.Drag(Move, 7 * 60, 60, -50, fine: false));
        Assert.Equal(Slot(7 * 60, 60), ScheduleEdit.Drag(Move, NineThirty, 60, -1000, fine: false));
        Assert.Equal(Slot(22 * 60, 60), ScheduleEdit.Drag(Move, 22 * 60, 60, 200, fine: false));   // ends at 23:00
        Assert.Equal(Slot(22 * 60 + 30, 30), ScheduleEdit.Drag(Move, NineThirty, 30, 1000, fine: true));
        Assert.Equal(Slot(22 * 60 + 45, 15), ScheduleEdit.Drag(Move, NineThirty, 15, 1000, fine: false));
    }

    [Theory]
    [InlineData(960, 0)]
    [InlineData(960, 500)]
    [InlineData(960, -500)]
    [InlineData(1020, 0)]    // 17 h: the bounds cross (07:00 > 23:00 − 17 h); 07:00 wins, nothing throws
    [InlineData(1020, 300)]
    public void A_task_of_16_hours_or_more_stays_at_07_00(int dur, double dy)
    {
        Assert.Equal(Slot(7 * 60, dur), ScheduleEdit.Drag(Move, 7 * 60, dur, dy, fine: false));
    }

    [Fact]
    public void The_hour_height_scales_the_travel()
    {
        Assert.Equal(Slot(NineThirty + 15, 60), ScheduleEdit.Drag(Move, NineThirty, 60, 24, fine: false, hourHeight: 96));
        Assert.Equal(Slot(NineThirty, 60), ScheduleEdit.Drag(Move, NineThirty, 60, 11.9, fine: false, hourHeight: 96));
    }

    // ---------- resize ----------

    [Theory]
    [InlineData(0, 60)]
    [InlineData(6, 75)]       // half a step rounds up
    [InlineData(12, 75)]
    [InlineData(36, 105)]     // 09:30 – 11:15
    [InlineData(-12, 45)]
    [InlineData(-36, 15)]     // the minimum
    [InlineData(-1000, 15)]
    [InlineData(1000, 810)]   // ends at 23:00
    public void A_resize_moves_the_end_in_15_minute_steps(double dy, int dur)
    {
        Assert.Equal(Slot(NineThirty, dur), ScheduleEdit.Drag(Resize, NineThirty, 60, dy, fine: false));
    }

    [Fact]
    public void With_Alt_a_resize_moves_the_end_in_5_minute_steps()
    {
        Assert.Equal(Slot(NineThirty, 65), ScheduleEdit.Drag(Resize, NineThirty, 60, 4, fine: true));
        Assert.Equal(Slot(NineThirty, 15), ScheduleEdit.Drag(Resize, NineThirty, 60, -100, fine: true));
        // An end off the grid (09:37 + 30 = 10:07) snaps to the absolute grid.
        Assert.Equal(Slot(NineThirtySeven, 23), ScheduleEdit.Drag(Resize, NineThirtySeven, 30, 0, fine: false));
        Assert.Equal(Slot(NineThirtySeven, 28), ScheduleEdit.Drag(Resize, NineThirtySeven, 30, 0, fine: true));
    }

    [Fact]
    public void A_resize_after_22_45_keeps_the_15_minute_minimum_past_23_00()
    {
        // The minimum (start + 15) wins over the 23:00 edge, as in the prototype: 22:50 → 23:05.
        Assert.Equal(Slot(22 * 60 + 50, 15), ScheduleEdit.Drag(Resize, 22 * 60 + 50, 30, 0, fine: false));
        Assert.Equal(Slot(22 * 60 + 50, 15), ScheduleEdit.Drag(Resize, 22 * 60 + 50, 30, 500, fine: false));
        Assert.Equal(Slot(22 * 60 + 45, 15), ScheduleEdit.Drag(Resize, 22 * 60 + 45, 15, 100, fine: false));
    }

    // ---------- keyboard ----------

    [Theory]
    [InlineData(NineThirtySeven, true, false, NineThirty + 15)] // ↓ to the next grid line
    [InlineData(NineThirtySeven, false, false, NineThirty)]     // ↑ to the previous one
    [InlineData(NineThirtySeven, true, true, NineThirty + 10)]  // Alt: 5-minute lines
    [InlineData(NineThirtySeven, false, true, NineThirty + 5)]
    [InlineData(NineThirty, true, false, NineThirty + 15)]
    [InlineData(NineThirty, false, false, NineThirty - 15)]
    [InlineData(NineThirty, true, true, NineThirty + 5)]
    public void Arrows_move_the_start_to_the_next_grid_line(int start, bool down, bool alt, int expected)
    {
        Assert.Equal(Slot(expected, 60), ScheduleEdit.Key(start, 60, down, shift: false, fine: alt));
    }

    [Fact]
    public void Arrows_stop_at_the_edges()
    {
        Assert.Null(ScheduleEdit.Key(7 * 60, 60, down: false, shift: false, fine: false));
        Assert.Equal(Slot(7 * 60, 60), ScheduleEdit.Key(7 * 60 + 5, 60, down: false, shift: false, fine: false));
        Assert.Null(ScheduleEdit.Key(22 * 60, 60, down: true, shift: false, fine: false));
        Assert.Equal(Slot(22 * 60, 60), ScheduleEdit.Key(21 * 60 + 50, 60, down: true, shift: false, fine: false));
        Assert.Null(ScheduleEdit.Key(7 * 60, 960, down: true, shift: false, fine: false));   // 16 h: nowhere to go
        Assert.Null(ScheduleEdit.Key(7 * 60, 1020, down: false, shift: false, fine: false)); // crossed bounds, no throw
    }

    [Fact]
    public void Shift_and_arrows_change_the_length()
    {
        Assert.Equal(Slot(NineThirty, 75), ScheduleEdit.Key(NineThirty, 60, down: true, shift: true, fine: false));
        Assert.Equal(Slot(NineThirty, 45), ScheduleEdit.Key(NineThirty, 60, down: false, shift: true, fine: false));
        Assert.Equal(Slot(NineThirty, 65), ScheduleEdit.Key(NineThirty, 60, down: true, shift: true, fine: true));
        // The end moves to the grid: 09:37 + 30 = 10:07 → 10:15 (38 min) or 10:00 (23 min).
        Assert.Equal(Slot(NineThirtySeven, 38), ScheduleEdit.Key(NineThirtySeven, 30, down: true, shift: true, fine: false));
        Assert.Equal(Slot(NineThirtySeven, 23), ScheduleEdit.Key(NineThirtySeven, 30, down: false, shift: true, fine: false));
    }

    [Fact]
    public void Shift_and_arrows_keep_15_minutes_and_23_00()
    {
        Assert.Null(ScheduleEdit.Key(NineThirty, 15, down: false, shift: true, fine: false));
        Assert.Null(ScheduleEdit.Key(22 * 60, 60, down: true, shift: true, fine: false));
        Assert.Equal(Slot(22 * 60, 60), ScheduleEdit.Key(22 * 60, 45, down: true, shift: true, fine: false));
        // Starting after 22:45 the minimum wins over 23:00 (as in the prototype).
        Assert.Equal(Slot(22 * 60 + 50, 15), ScheduleEdit.Key(22 * 60 + 50, 30, down: false, shift: true, fine: false));
        Assert.Null(ScheduleEdit.Key(22 * 60 + 50, 15, down: true, shift: true, fine: false));
    }

    [Fact]
    public void The_chip_shows_the_start_of_a_move_and_the_end_of_a_resize()
    {
        Assert.Equal(11 * 60 + 15, ScheduleEdit.ChipMinutes(Move, Slot(11 * 60 + 15, 60)));
        Assert.Equal(11 * 60 + 15, ScheduleEdit.ChipMinutes(Resize, Slot(NineThirty, 105)));
    }

    // ---------- the copy, autoscroll and texts ----------

    [Fact]
    public void Blocks_under_the_copy_by_visual_length()
    {
        var copy = Slot(10 * 60, 60);
        Assert.True(ScheduleEdit.UnderCopy(10 * 60 + 30, 30, copy));
        Assert.True(ScheduleEdit.UnderCopy(9 * 60 + 45, 15, copy));  // 15 min drawn as 30: 09:45–10:15
        Assert.False(ScheduleEdit.UnderCopy(9 * 60 + 30, 30, copy)); // ends as the copy starts
        Assert.False(ScheduleEdit.UnderCopy(11 * 60, 30, copy));     // starts as the copy ends
        Assert.True(ScheduleEdit.UnderCopy(10 * 60 + 20, 0, Slot(10 * 60, 15))); // the copy also occupies 30
        Assert.Equal(10 * 60 + 30, Slot(10 * 60, 15).VisualEnd);
    }

    [Theory]
    [InlineData(350, 0)]
    [InlineData(140, 0)]       // the zone ends 40 px in
    [InlineData(139.9, -2)]    // just inside: the 2 px minimum
    [InlineData(120, -7)]      // half way: 7
    [InlineData(100, -14)]     // at the edge: 14
    [InlineData(50, -14)]      // beyond it: still 14
    [InlineData(460, 0)]
    [InlineData(460.1, 2)]
    [InlineData(480, 7)]
    [InlineData(500, 14)]
    [InlineData(600, 14)]
    public void Autoscroll_speed_grows_with_the_depth_into_the_40_px_zone(double y, int speed)
    {
        Assert.Equal(speed, ScheduleEdit.AutoScrollSpeed(y, 100, 500));
    }

    [Fact]
    public void Autoscroll_zone_scales()
    {
        Assert.Equal(0, ScheduleEdit.AutoScrollSpeed(160, 100, 500, scale: 1.5));
        Assert.Equal(-7, ScheduleEdit.AutoScrollSpeed(130, 100, 500, scale: 1.5));
        Assert.Equal(-5, ScheduleEdit.AutoScrollSpeed(139, 100, 500, scale: 1.5)); // inside 60 px, outside 40: 21/60 · 14 = 4.9
    }

    [Theory]
    [InlineData(15, "15m")]
    [InlineData(45, "45m")]
    [InlineData(60, "1h")]
    [InlineData(105, "1h 45m")]
    [InlineData(120, "2h")]
    [InlineData(960, "16h")]
    [InlineData(1005, "16h 45m")]
    [InlineData(0, "")]
    public void Duration_labels(int minutes, string label)
    {
        Assert.Equal(label, ScheduleEdit.DurationLabel(minutes));
    }

    [Fact]
    public void Texts_of_a_drag()
    {
        Assert.Equal("09:30 – 11:15 · 1h 45m", ScheduleEdit.ActiveRange(NineThirty, 105));
        Assert.Equal("Moved Write the weekly update, 11:15 to 12:15", ScheduleEdit.ChangedMessage(Move, "Write the weekly update", Slot(11 * 60 + 15, 60)));
        Assert.Equal("Resized Write the weekly update, 09:30 to 11:15", ScheduleEdit.ChangedMessage(Resize, "Write the weekly update", Slot(NineThirty, 105)));
        Assert.Equal("Move cancelled", ScheduleEdit.CancelledMessage);
    }

    // ---------- writing the result ----------

    private static TaskItem Task(int start, int dur) =>
        new(5, "Write the weekly update", "notes", new DateOnly(2026, 11, 15), null, new TimeOnly(start / 60, start % 60), dur, Priority.High, "work", false);

    [Fact]
    public void A_move_changes_only_the_time()
    {
        var t = Task(NineThirty, 60);
        Assert.Equal(t with { Time = new TimeOnly(11, 15) }, ScheduleEdit.Apply(t, Move, Slot(11 * 60 + 15, 60)));
    }

    [Fact]
    public void A_move_keeps_an_unspecified_duration_unspecified()
    {
        var t = Task(NineThirty, 0);
        Assert.Equal(30, ScheduleEdit.EffectiveDuration(t));
        var moved = ScheduleEdit.Apply(t, Move, Slot(10 * 60, 30));
        Assert.Equal(t with { Time = new TimeOnly(10, 0) }, moved);
        Assert.Equal(0, moved!.DurationMinutes);
    }

    [Fact]
    public void A_resize_writes_the_real_length()
    {
        var t = Task(NineThirty, 0);
        Assert.Equal(t with { DurationMinutes = 45 }, ScheduleEdit.Apply(t, Resize, Slot(NineThirty, 45)));
        Assert.Equal(Task(NineThirty, 105), ScheduleEdit.Apply(Task(NineThirty, 60), Resize, Slot(NineThirty, 105)));
    }

    [Fact]
    public void Dropping_on_the_same_slot_writes_nothing()
    {
        Assert.Null(ScheduleEdit.Apply(Task(NineThirty, 60), Move, Slot(NineThirty, 60)));
        Assert.Null(ScheduleEdit.Apply(Task(NineThirty, 60), Resize, Slot(NineThirty, 60)));
        Assert.Null(ScheduleEdit.Apply(Task(NineThirty, 0), Resize, Slot(NineThirty, 30))); // 0 is drawn as 30
        Assert.Null(ScheduleEdit.Apply(Task(NineThirty, 60) with { Time = null }, Move, Slot(10 * 60, 60)));
    }

    [Fact]
    public void A_slot_outside_the_day_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleEdit.Apply(Task(NineThirty, 60), Move, Slot(24 * 60, 60)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleEdit.Apply(Task(NineThirty, 60), Move, Slot(-15, 60)));
        Assert.Throws<ArgumentNullException>(() => ScheduleEdit.Apply(null!, Move, Slot(NineThirty, 60)));
    }
}
