using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Motion;
using Doba.App.Tests.DesignSystem;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Controls;

/// <summary>
/// The time wheel: geometry, snap and clamp, every input path, and the commit rule ("write the value only after the
/// wheel has been still for 120 ms") driven deterministically: the settle timer runs on a <see cref="ManualClock"/>, and
/// running scroll animations are completed explicitly (or are instant with OS animations off).
/// </summary>
public class TimeWheelTests
{
    private static readonly string[] Hours = [.. Enumerable.Range(0, 24).Select(h => h.ToString("00", System.Globalization.CultureInfo.InvariantCulture))];
    private static readonly string[] Minutes = [.. Enumerable.Range(0, 12).Select(m => (m * 5).ToString("00", System.Globalization.CultureInfo.InvariantCulture))];

    private const double Row = 34;
    private const double Centre = 85; // 170 / 2
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(AppMotion.WheelSettle);
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private sealed class Scene : IDisposable
    {
        private readonly bool _animations = AppMotion.SystemAnimationsEnabled;

        public Scene(int selected = 14, string[]? items = null, bool animations = true)
        {
            AppMotion.SystemAnimationsEnabled = animations;
            Wheel = new TimeWheel
            {
                Items = items ?? Hours,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                SettleClock = Clock,
            };
            Wheel.ScrollToIndex(selected, animate: false);
            Wheel.Committed += (_, i) => Commits.Add(i);
            Window = new Window { Width = 200, Height = 200, Content = Wheel };
            Window.Show();
            Window.UpdateLayout();
        }

        public ManualClock Clock { get; } = new();

        public TimeWheel Wheel { get; }

        public Window Window { get; }

        public List<int> Commits { get; } = [];

        public StackPanel Strip => Wheel.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_Items");

        public double StripY => ((TranslateTransform)Strip.RenderTransform!).Y;

        /// <summary>The row whose centre sits on the wheel's centre line at rest.</summary>
        public int CentredRow()
        {
            var rows = Strip.Children.OfType<Border>().ToList();
            for (var i = 0; i < rows.Count; i++)
            {
                var centre = rows[i].TranslatePoint(new Point(0, rows[i].Bounds.Height / 2), Wheel);
                if (centre is { } p && Math.Abs(p.Y - Centre) < 0.01)
                {
                    return i;
                }
            }

            return -1;
        }

        public TextBlock Label(int index) => (TextBlock)Strip.Children.OfType<Border>().ElementAt(index).Child!;

        /// <summary>Finishes the running scroll animation (as if its time had passed).</summary>
        public void FinishScroll() => Animator.Running(Strip)?.Complete();

        public static Point RowCentre(int rowsFromCentre) => new(35, Centre + (rowsFromCentre * Row));

        public void Notch(double delta) => Window.MouseWheel(new Point(35, Centre), new Vector(0, delta));

        public void Key(Key key)
        {
            Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        }

        public void Dispose()
        {
            Window.Close();
            AppMotion.SystemAnimationsEnabled = _animations;
        }
    }

    [AvaloniaFact]
    public void Geometry_matches_the_design_and_the_selected_row_is_centred()
    {
        using var s = new Scene(selected: 0);
        Assert.Equal(70, s.Wheel.Bounds.Width);
        Assert.Equal(170, s.Wheel.Bounds.Height);
        Assert.All(s.Strip.Children, row => Assert.Equal(Row, row.Bounds.Height));
        Assert.Equal(24, s.Strip.Children.Count);

        foreach (var index in new[] { 0, 11, 23 })
        {
            s.Wheel.ScrollToIndex(index, animate: false);
            s.Window.UpdateLayout();
            Assert.Equal(index, s.Wheel.SelectedIndex);
            Assert.Equal(index, s.CentredRow());
            Assert.Equal(68 - (index * Row), s.StripY, 3); // 68 px "padding" puts the first and last rows in the centre
        }

        // The band is one row high, centred, 7 % white with radius 8.
        var band = s.Wheel.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Band");
        Assert.Equal(new Rect(0, 68, 70, 34), band.Bounds);
        Assert.Equal(Pixels.Resource("Color.Overlay.Edge"), ((ISolidColorBrush)band.Background!).Color);
        Assert.Equal(8, band.CornerRadius.TopLeft);
    }

    [AvaloniaFact]
    public void Selected_number_is_emphasised_and_the_others_are_muted()
    {
        using var s = new Scene(selected: 14);
        var selected = s.Label(14);
        var other = s.Label(13);
        Assert.Equal(19, selected.FontSize);
        Assert.Equal(FontWeight.SemiBold, selected.FontWeight);
        Assert.Equal(Pixels.Resource("Color.Text.Primary"), ((ISolidColorBrush)selected.Foreground!).Color);
        Assert.Equal(15, other.FontSize);
        Assert.Equal(FontWeight.Normal, other.FontWeight);
        Assert.Equal(Pixels.Resource("Color.Text.Muted"), ((ISolidColorBrush)other.Foreground!).Color);
    }

    [AvaloniaFact]
    public void Emphasis_moves_on_commit_with_a_render_thread_scale_and_a_colour_fade()
    {
        using var s = new Scene(selected: 14);
        s.Notch(-1);
        s.FinishScroll();
        Assert.Equal(19, s.Label(14).FontSize); // still the committed value while settling
        s.Clock.Advance(Settle);

        var grown = Animator.Running(s.Strip.Children[15]);
        var shrunk = Animator.Running(s.Strip.Children[14]);
        Assert.NotNull(grown);
        Assert.NotNull(shrunk);
        Assert.Equal(15.0 / 19, grown.From.ScaleX, 4); // laid out at 19 px once, drawn from 15 px size → 1
        Assert.Equal(19.0 / 15, shrunk.From.ScaleX, 4);
        Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.WheelText), grown.Duration);
        Assert.Null(s.Strip.Children[15].RenderTransform); // no layout-side transform, nothing animates per frame on the UI thread
        Assert.Equal(19, s.Label(15).FontSize);
        Assert.Equal(FontWeight.SemiBold, s.Label(15).FontWeight);
        Assert.NotNull(s.Label(15).Transitions); // colour: `color 0.2s ease`
        Assert.Equal(15, s.Label(14).FontSize);
    }

    [AvaloniaFact]
    public void Wheel_notch_moves_one_row_and_commits_once_after_the_settle_time()
    {
        using var s = new Scene(selected: 14);
        s.Notch(-1); // wheel down = next row
        Assert.Equal(15, s.Wheel.TargetIndex);
        Assert.Equal(68 - (15 * Row), s.StripY, 3); // resting place moved one row; the FLIP animation runs on top
        Assert.True(Animator.IsAnimating(s.Strip));
        Assert.Equal(14, s.Wheel.SelectedIndex); // nothing changes while scrolling

        s.Clock.Advance(Settle * 3);
        Assert.Empty(s.Commits); // the settle timer starts only when the scroll has stopped

        s.FinishScroll();
        s.Clock.Advance(Settle - Tick);
        Assert.Empty(s.Commits);
        Assert.Equal(14, s.Wheel.SelectedIndex);

        s.Clock.Advance(Tick);
        Assert.Equal([15], s.Commits);
        Assert.Equal(15, s.Wheel.SelectedIndex);
        Assert.Equal(15, s.CentredRow());

        s.Clock.Advance(Settle * 10);
        Assert.Single(s.Commits);

        s.Notch(1); // wheel up = previous row
        s.FinishScroll();
        s.Clock.Advance(Settle);
        Assert.Equal([15, 14], s.Commits);
    }

    [AvaloniaFact]
    public void A_burst_of_notches_commits_once_with_the_final_index()
    {
        using var s = new Scene(selected: 14);
        s.Notch(-1);
        s.Notch(-1);
        s.Notch(-1);
        Assert.Equal(17, s.Wheel.TargetIndex);
        s.FinishScroll();
        s.Clock.Advance(Settle - Tick);

        s.Notch(-1); // still within the settle window: the timer restarts
        s.FinishScroll();
        s.Clock.Advance(Settle - Tick);
        Assert.Empty(s.Commits);
        Assert.Equal(14, s.Wheel.SelectedIndex);

        s.Clock.Advance(Tick);
        Assert.Equal([18], s.Commits);
        Assert.Equal(18, s.Wheel.SelectedIndex);
    }

    [AvaloniaFact]
    public void Touchpad_fractions_accumulate_to_whole_rows()
    {
        using var s = new Scene(selected: 5, animations: false);
        s.Notch(-0.4);
        s.Notch(-0.4);
        Assert.Equal(5, s.Wheel.TargetIndex);
        s.Notch(-0.4);
        Assert.Equal(6, s.Wheel.TargetIndex);
    }

    [AvaloniaFact]
    public void Both_ends_clamp_and_a_gesture_that_does_not_move_commits_nothing()
    {
        using var s = new Scene(selected: 1, animations: false);
        s.Notch(5);
        Assert.Equal(0, s.Wheel.TargetIndex);
        s.Clock.Advance(Settle);
        Assert.Equal([0], s.Commits);

        s.Notch(1); // already at the top
        s.Clock.Advance(Settle);
        Assert.Equal([0], s.Commits);
        Assert.Equal(0, s.CentredRow());

        s.Wheel.ScrollToIndex(22, animate: false);
        s.Notch(-10);
        s.Clock.Advance(Settle);
        Assert.Equal([0, 23], s.Commits);
        Assert.Equal(23, s.CentredRow());
        s.Notch(-1);
        s.Clock.Advance(Settle);
        Assert.Equal([0, 23], s.Commits);
    }

    [AvaloniaFact]
    public void Notches_against_an_end_while_scrolling_do_not_restart_the_scroll()
    {
        using var s = new Scene(selected: 22);
        s.Notch(-1);
        var running = Animator.Running(s.Strip);
        Assert.NotNull(running);
        s.Notch(-1);
        Assert.Same(running, Animator.Running(s.Strip));
        s.FinishScroll();
        s.Clock.Advance(Settle);
        Assert.Equal([23], s.Commits);
    }

    [AvaloniaFact]
    public void Click_on_a_row_scrolls_to_it_and_commits()
    {
        using var s = new Scene(selected: 14);
        s.Window.MouseDown(Scene.RowCentre(2), MouseButton.Left);
        s.Window.MouseUp(Scene.RowCentre(2), MouseButton.Left);
        Assert.Equal(16, s.Wheel.TargetIndex);
        Assert.True(Animator.IsAnimating(s.Strip)); // smooth scroll
        Assert.Equal(14, s.Wheel.SelectedIndex);

        s.FinishScroll();
        s.Clock.Advance(Settle);
        Assert.Equal([16], s.Commits);
        Assert.Equal(16, s.CentredRow());

        // A row above the band, then a click on the centred row (no movement, no commit).
        s.Window.MouseDown(Scene.RowCentre(-1), MouseButton.Left);
        s.Window.MouseUp(Scene.RowCentre(-1), MouseButton.Left);
        s.FinishScroll();
        s.Clock.Advance(Settle);
        Assert.Equal([16, 15], s.Commits);

        s.Window.MouseDown(Scene.RowCentre(0), MouseButton.Left);
        s.Window.MouseUp(Scene.RowCentre(0), MouseButton.Left);
        s.Clock.Advance(Settle);
        Assert.Equal([16, 15], s.Commits);
    }

    [AvaloniaFact]
    public void Drag_follows_the_pointer_snaps_on_release_and_commits_only_after_release()
    {
        using var s = new Scene(selected: 14);
        var start = Scene.RowCentre(0);
        s.Window.MouseDown(start, MouseButton.Left);
        s.Window.MouseMove(start - new Point(0, 30));
        s.Window.MouseMove(start - new Point(0, 75)); // 75 px up ≈ 2.2 rows further down the list
        Assert.Equal(68 - (14 * Row) - 75, s.StripY, 3); // the strip follows the pointer exactly
        Assert.False(Animator.IsAnimating(s.Strip));

        s.Clock.Advance(Settle * 5); // holding still with the button down never commits
        Assert.Empty(s.Commits);
        Assert.Equal(14, s.Wheel.SelectedIndex);

        s.Window.MouseUp(start - new Point(0, 75), MouseButton.Left);
        Assert.Equal(16, s.Wheel.TargetIndex); // round(offset / 34): snap to the nearest row
        s.FinishScroll();
        s.Clock.Advance(Settle);
        Assert.Equal([16], s.Commits);
        Assert.Equal(16, s.CentredRow());

        // Dragging past the end clamps.
        s.Window.MouseDown(start, MouseButton.Left);
        s.Window.MouseMove(start + new Point(0, 1000));
        Assert.Equal(68, s.StripY, 3);
        s.Window.MouseUp(start + new Point(0, 1000), MouseButton.Left);
        s.FinishScroll();
        s.Clock.Advance(Settle);
        Assert.Equal([16, 0], s.Commits);
    }

    [AvaloniaFact]
    public void Keyboard_moves_the_wheel_when_focused()
    {
        using var s = new Scene(selected: 14, animations: false);
        Assert.True(s.Wheel.Focusable);
        Assert.True(s.Wheel.Focus(NavigationMethod.Tab));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.Single(s.Window.GetVisualDescendants().OfType<FocusRing>()); // visible keyboard focus

        s.Key(Key.Down);
        Assert.Equal(15, s.Wheel.TargetIndex);
        s.Key(Key.Down);
        s.Key(Key.Up);
        s.Key(Key.Down);
        Assert.Equal(16, s.Wheel.TargetIndex);
        s.Clock.Advance(Settle);
        Assert.Equal([16], s.Commits);

        s.Key(Key.PageDown);
        Assert.Equal(21, s.Wheel.TargetIndex); // one screen = 5 rows
        s.Key(Key.PageDown);
        Assert.Equal(23, s.Wheel.TargetIndex); // clamped
        s.Key(Key.PageUp);
        Assert.Equal(18, s.Wheel.TargetIndex);
        s.Key(Key.Home);
        Assert.Equal(0, s.Wheel.TargetIndex);
        s.Clock.Advance(Settle);
        s.Key(Key.End);
        Assert.Equal(23, s.Wheel.TargetIndex);
        s.Clock.Advance(Settle);
        Assert.Equal([16, 0, 23], s.Commits);
        Assert.Equal(23, s.CentredRow());
    }

    [AvaloniaFact]
    public void ScrollToIndex_raises_nothing_and_cancels_a_pending_user_commit()
    {
        using var s = new Scene(selected: 14);
        var changes = new List<int>();
        s.Wheel.PropertyChanged += (_, e) =>
        {
            if (e.Property == TimeWheel.SelectedIndexProperty)
            {
                changes.Add((int)e.NewValue!);
            }
        };

        s.Wheel.ScrollToIndex(3, animate: false);
        Assert.Equal(3, s.Wheel.SelectedIndex);
        Assert.Equal(3, s.CentredRow());
        Assert.False(Animator.IsAnimating(s.Strip));
        Assert.Equal(0, s.Clock.ActiveTimers);
        s.Clock.Advance(Settle * 2);
        Assert.Empty(s.Commits);
        Assert.Equal([3], changes);

        s.Notch(-1);
        s.FinishScroll();
        Assert.True(s.Wheel.CommitPending);
        s.Wheel.ScrollToIndex(9, animate: false);
        Assert.False(s.Wheel.CommitPending);
        s.Clock.Advance(Settle * 2);
        Assert.Empty(s.Commits);
        Assert.Equal(9, s.Wheel.SelectedIndex);

        // Animated programmatic scroll: smooth, SelectedIndex set at once, still no commit.
        s.Wheel.ScrollToIndex(12, animate: true);
        Assert.Equal(12, s.Wheel.SelectedIndex);
        Assert.True(Animator.IsAnimating(s.Strip));
        s.FinishScroll();
        s.Clock.Advance(Settle * 2);
        Assert.Empty(s.Commits);
        Assert.Equal(12, s.CentredRow());
    }

    [AvaloniaFact]
    public void Setting_SelectedIndex_positions_instantly_without_a_commit()
    {
        using var s = new Scene(selected: 14);
        s.Wheel.SelectedIndex = 7;
        Assert.Equal(7, s.CentredRow());
        Assert.False(Animator.IsAnimating(s.Strip));
        s.Clock.Advance(Settle);
        Assert.Empty(s.Commits);
        Assert.Equal(19, s.Label(7).FontSize);
        Assert.Equal(15, s.Label(14).FontSize);
    }

    [AvaloniaFact]
    public void Animations_off_moves_instantly_and_still_waits_for_stillness()
    {
        using var s = new Scene(selected: 14, animations: false);
        s.Notch(-1);
        Assert.False(Animator.IsAnimating(s.Strip));
        Assert.Equal(15, s.CentredRow()); // already there
        Assert.Empty(s.Commits);

        s.Window.MouseDown(Scene.RowCentre(-2), MouseButton.Left);
        s.Window.MouseUp(Scene.RowCentre(-2), MouseButton.Left);
        Assert.False(Animator.IsAnimating(s.Strip));
        Assert.Equal(13, s.CentredRow());

        s.Clock.Advance(Settle - Tick);
        Assert.Empty(s.Commits);
        s.Clock.Advance(Tick);
        Assert.Equal([13], s.Commits);
        Assert.False(Animator.IsAnimating(s.Strip.Children[13]));
    }

    [AvaloniaFact]
    public void Items_change_keeps_a_valid_index()
    {
        using var s = new Scene(selected: 20);
        s.Wheel.Items = Minutes;
        s.Window.UpdateLayout();
        Assert.Equal(12, s.Strip.Children.Count);
        Assert.Equal(11, s.Wheel.SelectedIndex);
        Assert.Equal(11, s.CentredRow());
        Assert.Equal(19, s.Label(11).FontSize);

        s.Wheel.SelectedIndex = 40;
        Assert.Equal(11, s.Wheel.SelectedIndex);
        s.Wheel.SelectedIndex = -3;
        Assert.Equal(0, s.Wheel.SelectedIndex);
        Assert.Equal(0, s.CentredRow());

        s.Wheel.Items = [];
        s.Window.UpdateLayout();
        Assert.Empty(s.Strip.Children);
        s.Notch(-1); // nothing to scroll: no crash, no commit
        s.Clock.Advance(Settle);
        Assert.Empty(s.Commits);
    }

    [AvaloniaFact]
    public void Production_clock_settles_on_its_own_on_the_ui_thread()
    {
        using var s = new Scene(selected: 14);
        s.Wheel.SettleClock = TimeProvider.System; // thread-pool timer, posted back to the dispatcher
        var thread = Environment.CurrentManagedThreadId;
        var commitThread = -1;
        s.Wheel.Committed += (_, _) => commitThread = Environment.CurrentManagedThreadId;

        s.Notch(-1);
        s.Notch(-1);
        Assert.True(Pump.Until(() => s.Commits.Count > 0, TimeSpan.FromSeconds(5)));
        Assert.Equal([16], s.Commits);
        Assert.Equal(thread, commitThread);
        Assert.False(Pump.Until(() => s.Commits.Count > 1, TimeSpan.FromMilliseconds(AppMotion.WheelSettle * 3)));
    }

    [AvaloniaFact]
    public void A_pending_commit_is_flushed_when_the_wheel_leaves_the_tree()
    {
        var s = new Scene(selected: 14);
        s.Notch(-1);
        s.FinishScroll();
        s.Window.Content = null; // popover closed before the 120 ms passed
        Assert.Equal([15], s.Commits);
        Assert.Equal(15, s.Wheel.SelectedIndex);
        s.Clock.Advance(Settle);
        Assert.Single(s.Commits);
        s.Dispose();
    }

    /// <summary>
    /// The wheel area of the Time popover (design lines 731-743): two wheels (hours at 14, minutes at 00) on the popover
    /// background, the shared band behind both and the dialog's ":" between — compare with the wheel area of
    /// docs/design/screens/dialog-time.png.
    /// </summary>
    [AvaloniaFact]
    public void Timewheel_content_renders_to_png()
    {
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false;
        try
        {
            var hours = new TimeWheel { Items = Hours, ShowsBand = false };
            var minutes = new TimeWheel { Items = Minutes, ShowsBand = false };
            hours.ScrollToIndex(14, animate: false);
            minutes.ScrollToIndex(0, animate: false);

            var colon = new TextBlock
            {
                Text = ":",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
                FontWeight = FontWeight.SemiBold,
            };
            colon.Bind(TextBlock.FontSizeProperty, colon.GetResourceObservable("FontSize.18"));
            colon.Bind(TextBlock.ForegroundProperty, colon.GetResourceObservable("Brush.Text.Primary"));

            var band = new Border { Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("TimeWheelBand")! };
            band.Bind(Layoutable.MarginProperty, band.GetResourceObservable("Space.TimeWheel.Band"));

            var area = new Panel
            {
                Height = 170,
                Margin = new Thickness(10),
                Children =
                {
                    band,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 2,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { hours, colon, minutes },
                    },
                },
            };
            var popover = new Border { Width = 248, Child = area, VerticalAlignment = VerticalAlignment.Top };
            popover.Bind(Border.BackgroundProperty, popover.GetResourceObservable("Brush.Bg.Popover"));

            var window = new Window { Width = 248, Height = 190, Content = popover };
            window.Show();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            var path = Screenshots.Capture(window, "timewheel-content");
            Assert.True(new FileInfo(path).Length > 1_000);

            // Band behind the selected numbers, popover around it, the edges faded to the popover colour.
            using var frame = window.CaptureRenderedFrame()!;
            var popoverColour = Pixels.Resource("Color.Bg.Popover");
            Assert.True(Pixels.Near(popoverColour, Pixels.At(frame, 5, 95), 2));
            var bandPixel = Pixels.At(frame, 20, 95);
            Assert.True(bandPixel.R > popoverColour.R + 8, $"band {bandPixel} vs popover {popoverColour}");
            // The fade mask: the edge rows "12" and "16" are much dimmer than their neighbours "13" and "15".
            var hoursLeft = (int)hours.TranslatePoint(default, window)!.Value.X;
            var minutesLeft = (int)minutes.TranslatePoint(default, window)!.Value.X;
            var top = (int)hours.TranslatePoint(default, window)!.Value.Y;
            int Brightest(int row) => Brightness(frame, hoursLeft, top + (row * 34), 70, 34);
            Assert.True(Brightest(0) * 2 < Brightest(1), $"row 12 {Brightest(0)} vs 13 {Brightest(1)}");
            Assert.True(Brightest(4) * 2 < Brightest(3), $"row 16 {Brightest(4)} vs 15 {Brightest(3)}");

            // The mask spans the whole column even when the first row is selected (nothing drawn above it): "00" is as
            // bright as "14" and "10" at the bottom edge is faded like "16".
            int BrightestMinute(int row) => Brightness(frame, minutesLeft, top + (row * 34), 70, 34);
            Assert.InRange(BrightestMinute(2), Brightest(2) - 6, Brightest(2) + 6);
            Assert.True(BrightestMinute(4) * 2 < BrightestMinute(3), $"row 10 {BrightestMinute(4)} vs 05 {BrightestMinute(3)}");
            window.Close();
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
        }
    }

    /// <summary>The highest red channel in the rectangle, minus the popover's (text brightness above the background).</summary>
    private static int Brightness(Avalonia.Media.Imaging.WriteableBitmap frame, int x, int y, int width, int height)
    {
        var background = Pixels.Resource("Color.Bg.Popover").R;
        var max = 0;
        for (var py = y; py < y + height; py++)
        {
            for (var px = x; px < x + width; px++)
            {
                max = Math.Max(max, Pixels.At(frame, px, py).R - background);
            }
        }

        return max;
    }
}

/// <summary>A <see cref="TimeProvider"/> whose time moves only through <see cref="Advance"/>; due timers fire inline.</summary>
internal sealed class ManualClock : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private TimeSpan _now;

    public int ActiveTimers => _timers.Count(t => t.DueAt is not null);

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        var end = _now + by;
        while (_timers.Where(t => t.DueAt <= end).OrderBy(t => t.DueAt).FirstOrDefault() is { } next)
        {
            _now = next.DueAt!.Value;
            next.Fire();
        }

        _now = end;
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public TimeSpan? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
            return true;
        }

        public void Fire()
        {
            DueAt = _period == Timeout.InfiniteTimeSpan || _period <= TimeSpan.Zero ? null : DueAt + _period;
            callback(state);
        }

        public void Dispose()
        {
            DueAt = null;
            clock._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
