using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Screens.Today;
using Doba.App.Tests.DesignSystem;
using Doba.App.Tests.Support;
using Doba.Core.Navigation;
using static Doba.App.Tests.Screens.Inbox.ListScreenKit;

namespace Doba.App.Tests.Screens.Today;

/// <summary>
/// Layout inside a schedule block (shared by Today and Day): checkbox, title and time per content tier, the checkbox's hit
/// area and the grip, measured against the prototype's CSS (1 px border, padding 7 10 0 / 0 10 centred, gap 8, the time
/// indented 20 under a tall title).
/// </summary>
public class ScheduleBlockLayoutTests
{
    private static (TodayView View, Window Window) Open(ScreenHarness harness)
    {
        var vm = new TodayViewModel(harness.Services);
        vm.Show(new NavState(AppView.Today, harness.App.DayClock.Today));
        var view = new TodayView { DataContext = vm };
        return (view, Host(view));
    }

    private static Panel Block(TodayView view, string range) =>
        view.Schedule.Blocks().Single(b => b.DataContext is ScheduleBlockViewModel vm && vm.Range == range);

    private static Rect BoundsIn(Control part, Visual block) =>
        new(part.TranslatePoint(default, block)!.Value, part.Bounds.Size);

    [Fact]
    public void Content_padding_and_time_offsets_follow_the_prototype()
    {
        // Tall: padding 7 10 0 inside the border; the time line at CSS 1 + 7 + 17.5 + 2 = 27.5 → row 27 = title top 8 + 19.
        Assert.Equal(new Thickness(10, 7, 10, 0), ScheduleBlockViewModel.ContentPaddingFor(45, tall: true));
        Assert.Equal(19, ScheduleBlockViewModel.MetaOffsetFor(45, tall: true));
        Assert.Equal(19, ScheduleBlockViewModel.MetaOffsetFor(141, tall: true));
        // Short 33 px (45 min): title line at 1 + (31 − 17.5) / 2 = 7.75 → 8; time at 1 + (31 − 14.7) / 2 = 9.15 → 9.
        Assert.Equal(new Thickness(10, 7, 10, 0), ScheduleBlockViewModel.ContentPaddingFor(33, tall: false));
        Assert.Equal(1, ScheduleBlockViewModel.MetaOffsetFor(33, tall: false));
        // Short 24 px (30 min): title at 3.25 → 3, time at 4.65 → 5.
        Assert.Equal(new Thickness(10, 2, 10, 0), ScheduleBlockViewModel.ContentPaddingFor(24, tall: false));
        Assert.Equal(2, ScheduleBlockViewModel.MetaOffsetFor(24, tall: false));
    }

    [AvaloniaFact]
    public void A_trimmed_short_title_pushes_the_time_to_the_right_padding()
    {
        using var harness = new ScreenHarness();
        var (view, window) = Open(harness);

        var plan = Block(view, "22:00 – 22:45"); // 302 × 33, "Plan next week and set top 3 priorities"
        var box = Part<TaskCheckBox>(plan, "PART_Box");
        var title = Part<StrikeText>(plan, "PART_BlockTitle");
        var meta = Part<TextBlock>(plan, "PART_Meta");
        Assert.Contains(title.TextLayout.TextLines, l => l.HasCollapsed); // trimmed with an ellipsis
        Assert.Equal("22:00 – 22:45", meta.Text);

        var b = BoundsIn(box, plan);
        var t = BoundsIn(title, plan);
        var r = BoundsIn(meta, plan);
        Assert.Equal(new Rect(11, 11, 12, 12), b);                       // border 1 + padding 10; centred on the title line
        Assert.Equal(b.Right + ScheduleBlockViewModel.Gap, t.X, 3);         // gap 8
        Assert.Equal(8, t.Y, 3);
        Assert.Equal(plan.Bounds.Width - 11, r.Right, 3);                  // flush with the right padding
        Assert.Equal(t.Right + ScheduleBlockViewModel.Gap, r.X, 3);
        Assert.Equal(t.Y + 1, r.Y, 3);                                     // 33 px block
        window.Close();
    }

    [AvaloniaFact]
    public void An_untrimmed_short_title_keeps_the_time_right_after_it()
    {
        using var harness = new ScreenHarness();
        var (view, window) = Open(harness);

        var weekly = Block(view, "21:00 – 21:30"); // 302 × 24
        var title = Part<StrikeText>(weekly, "PART_BlockTitle");
        var meta = Part<TextBlock>(weekly, "PART_Meta");
        Assert.DoesNotContain(title.TextLayout.TextLines, l => l.HasCollapsed);

        var b = BoundsIn(Part<TaskCheckBox>(weekly, "PART_Box"), weekly);
        var t = BoundsIn(title, weekly);
        var r = BoundsIn(meta, weekly);
        Assert.Equal(new Rect(11, 6, 12, 12), b);                          // 3.25 + 2.75 = 6, as Chromium centres it
        Assert.Equal(31, t.X, 3);
        Assert.Equal(3, t.Y, 3);
        Assert.Equal(t.Right + ScheduleBlockViewModel.Gap, r.X, 3);
        Assert.True(r.Right < weekly.Bounds.Width - 11);
        Assert.Equal(t.Y + 2, r.Y, 3);                                     // 24 px block
        window.Close();
    }

    [AvaloniaFact]
    public void A_tall_block_puts_the_time_under_the_title_indented_past_the_checkbox()
    {
        using var harness = new ScreenHarness();
        var (view, window) = Open(harness);

        var meal = Block(view, "12:00 – 13:00"); // 149 × 45, two columns
        var b = BoundsIn(Part<TaskCheckBox>(meal, "PART_Box"), meal);
        var t = BoundsIn(Part<StrikeText>(meal, "PART_BlockTitle"), meal);
        var r = BoundsIn(Part<TextBlock>(meal, "PART_Meta"), meal);
        Assert.Equal(new Rect(11, 11, 12, 12), b);
        Assert.Equal(new Point(31, 8), t.Position);
        Assert.Equal(new Point(31, 27), r.Position);                       // padding-left 20 under the title, 27.5 → 27
        Assert.Equal("12:00 – 13:00", Part<TextBlock>(meal, "PART_Meta").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Each_block_shows_the_time_its_size_leaves_room_for()
    {
        using var harness = new ScreenHarness();
        var (view, window) = Open(harness);

        // 15 November (the prototype's sbVis tiers): width ≥ 220 the range; tall 140–219 the range; tall < 140 the start;
        // short 120–219 the start; short < 120 nothing.
        (string Range, string? Meta)[] expected =
        [
            ("08:00 – 08:45", "08:00 – 08:45"),   // 302 × 33
            ("09:30 – 10:30", "09:30 – 10:30"),   // 302 × 45
            ("12:00 – 13:00", "12:00 – 13:00"),   // 149 × 45
            ("12:15 – 12:45", "12:15"),           // 149 × 24
            ("14:00 – 15:30", "14:00"),           // 98 × 69
            ("14:30 – 15:15", null),              // 98 × 33
            ("15:00 – 16:00", "15:00"),           // 98 × 45
            ("21:00 – 21:30", "21:00 – 21:30"),   // 302 × 24
            ("22:00 – 22:45", "22:00 – 22:45"),   // 302 × 33
        ];
        Assert.Equal(expected.Length, view.Schedule.Blocks().Count());
        foreach (var (range, text) in expected)
        {
            var block = Block(view, range);
            var meta = Part<TextBlock>(block, "PART_Meta");
            Assert.Equal(text is not null, meta.IsEffectivelyVisible);
            if (text is not null)
            {
                Assert.Equal(text, meta.Text);
            }

            // The title is always there, the checkbox too.
            Assert.True(Part<StrikeText>(block, "PART_BlockTitle").IsEffectivelyVisible);
            Assert.True(Part<TaskCheckBox>(block, "PART_Box").IsEffectivelyVisible);
        }

        window.Close();
    }

    [AvaloniaFact]
    public void The_checkbox_hit_area_open_button_and_grip_sit_where_the_prototype_puts_them()
    {
        using var harness = new ScreenHarness();
        var (view, window) = Open(harness);

        // (range, block height, chkTop, grip height): tall → chkTop 4; short → round((h − 26) / 2); grip 11, 8 under 32 px.
        (string Range, double H, double ChkTop, double Grip)[] cases =
        [
            ("09:30 – 10:30", 45, 4, 11),
            ("22:00 – 22:45", 33, 4, 11),
            ("21:00 – 21:30", 24, -1, 8),
        ];
        foreach (var (range, h, chkTop, grip) in cases)
        {
            var block = Block(view, range);
            Assert.Equal(h, block.Bounds.Height);
            var check = BoundsIn(Part<ToggleButton>(block, "PART_Check"), block);
            Assert.Equal(new Rect(4, chkTop + 1, 24, 24), check);           // left 3, top chkTop inside the border
            var open = BoundsIn(Part<Button>(block, "PART_Open"), block);
            Assert.Equal(new Rect(1, 1, block.Bounds.Width - 2, h - 2), open); // inset 0 inside the border
            var g = BoundsIn(Part<Border>(block, "PART_Grip"), block);
            Assert.Equal(new Rect(1, h + 2 - grip, block.Bounds.Width - 2, grip), g); // bottom −3 inside the border
            var bar = BoundsIn(Part<Border>(block, "PART_GripBar"), block);
            Assert.Equal(new Size(18, 3), bar.Size);
            Assert.Equal(block.Bounds.Width / 2, bar.Center.X, 3);
            Assert.Equal(0, Part<Border>(block, "PART_Grip").Opacity);     // shown on hover only
        }

        window.Close();
    }

    [AvaloniaFact]
    public void Block_titles_strike_with_StrikeText_bound_to_done()
    {
        using var harness = new ScreenHarness();
        var (view, window) = Open(harness);

        foreach (var block in view.Schedule.Blocks())
        {
            var vm = (ScheduleBlockViewModel)block.DataContext!;
            var title = Part<StrikeText>(block, "PART_BlockTitle");
            Assert.Equal(vm.Row.Done, title.Struck);
            Assert.Equal(vm.Row.Done, Part<TaskCheckBox>(block, "PART_Box").IsChecked);
            Assert.Null(title.TextDecorations); // the crisp line is StrikeText's own, not a font-metric decoration

            // Open: accent 10 % fill, accent 25 % border, Text.Primary title; done: Bg.BlockDone, Border.Default, Text.Muted.
            var frame = Part<Border>(block, "PART_Frame");
            Assert.Equal(new Thickness(1), frame.BorderThickness);
            Assert.Equal(Pixels.Resource(vm.Row.Done ? "Brush.Bg.BlockDone" : "Brush.Accent10"), ColorOf(frame.Background));
            Assert.Equal(Pixels.Resource(vm.Row.Done ? "Brush.Border.Default" : "Brush.Accent25"), ColorOf(frame.BorderBrush));
            Assert.Equal(Pixels.Resource(vm.Row.Done ? "Color.Text.Muted" : "Color.Text.Primary"), ColorOf(title.Foreground));
            Assert.Equal(Pixels.Resource(vm.Row.Done ? "Color.Border.Check" : "Brush.Accent70"), ColorOf(Part<Border>(block, "PART_GripBar").Background));
        }

        // Completing a block fades its strike in (the same StrikeText, no rebuild) and fills its checkbox.
        var plan = Block(view, "22:00 – 22:45");
        var planTitle = Part<StrikeText>(plan, "PART_BlockTitle");
        Assert.False(planTitle.Struck);
        ((ScheduleBlockViewModel)plan.DataContext!).Row.ToggleCommand.Execute(null);
        Settle(window);
        Assert.True(planTitle.Struck);
        Assert.Same(plan, Block(view, "22:00 – 22:45"));
        Assert.Same(planTitle, Part<StrikeText>(plan, "PART_BlockTitle"));
        Assert.True(Part<TaskCheckBox>(plan, "PART_Box").IsChecked);
        Assert.Contains("done", plan.Classes);
        Assert.True(Pump.Until(() => planTitle.StrokeColor == Pixels.Resource("Color.Text.Muted"), TimeSpan.FromSeconds(2)));
        Assert.True(Pump.Until(() => ColorOf(planTitle.Foreground) == Pixels.Resource("Color.Text.Muted"), TimeSpan.FromSeconds(2)));
        window.Close();
    }

    private static Avalonia.Media.Color ColorOf(Avalonia.Media.IBrush? brush) =>
        brush is Avalonia.Media.ISolidColorBrush s ? s.Color : default;
}
