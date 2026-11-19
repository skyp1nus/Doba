using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Screens.Today;
using Doba.App.Tests.DesignSystem;
using Doba.App.Tests.Support;
using Doba.Core.Navigation;
using static Doba.App.Tests.Screens.Inbox.ListScreenKit;

namespace Doba.App.Tests.Screens.Today;

/// <summary>Layout of the schedule blocks' title and time range (shared by Today and Day).</summary>
public class ScheduleBlockLayoutTests
{
    private static (TodayView View, Window Window) Open(ScreenHarness harness)
    {
        var vm = new TodayViewModel(harness.Services);
        vm.Show(new NavState(AppView.Today, harness.App.DayClock.Today));
        var view = new TodayView { DataContext = vm };
        return (view, Host(view));
    }

    private static Button Block(TodayView view, string range) =>
        view.Schedule.BlockList.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is ScheduleBlockViewModel vm && vm.Range == range);

    private static Rect BoundsIn(Control part, Visual block) =>
        new(part.TranslatePoint(default, block)!.Value, part.Bounds.Size);

    [Fact]
    public void Range_offsets_land_on_the_rows_nearest_the_reference()
    {
        // Two-line block: CSS 7 + 17.5 + 2 = 26.5 → row 26 = title top 7 + 19.
        Assert.Equal(19, ScheduleBlockViewModel.RangeOffsetFor(69, compact: false));
        // Compact 33 px (45 min): CSS (33 − 14.7) / 2 = 9.15 → row 9 = title top 8 + 1.
        Assert.Equal(1, ScheduleBlockViewModel.RangeOffsetFor(33, compact: true));
        // Compact 24 px (30 min): 4.65 → row 5 = title top 3 + 2.
        Assert.Equal(2, ScheduleBlockViewModel.RangeOffsetFor(24, compact: true));
    }

    [AvaloniaFact]
    public void A_trimmed_compact_title_pushes_the_range_to_the_right_padding()
    {
        using var harness = new ScreenHarness();
        var (view, window) = Open(harness);

        var plan = Block(view, "22:00 – 22:45");
        var title = Part<StrikeText>(plan, "PART_BlockTitle");
        var range = Part<TextBlock>(plan, "PART_Range");
        Assert.Contains(title.TextLayout.TextLines, l => l.HasCollapsed); // trimmed with an ellipsis

        var r = BoundsIn(range, plan);
        var t = BoundsIn(title, plan);
        Assert.Equal(plan.Bounds.Width - 10, r.Right, 3); // 1 px ring + 9 px padding: flush with the content edge
        Assert.Equal(t.Right + ScheduleBlockViewModel.CompactGap, r.X, 3);
        Assert.Equal(t.Y + 1, r.Y, 3); // 33 px block
        window.Close();
    }

    [AvaloniaFact]
    public void An_untrimmed_compact_title_keeps_the_range_right_after_it()
    {
        using var harness = new ScreenHarness();
        var (view, window) = Open(harness);

        var weekly = Block(view, "21:00 – 21:30");
        var title = Part<StrikeText>(weekly, "PART_BlockTitle");
        var range = Part<TextBlock>(weekly, "PART_Range");
        Assert.DoesNotContain(title.TextLayout.TextLines, l => l.HasCollapsed);

        var r = BoundsIn(range, weekly);
        var t = BoundsIn(title, weekly);
        Assert.Equal(t.Right + ScheduleBlockViewModel.CompactGap, r.X, 3);
        Assert.True(r.Right < weekly.Bounds.Width - 10);
        Assert.Equal(t.Y + 2, r.Y, 3); // 24 px block
        window.Close();
    }

    [AvaloniaFact]
    public void A_two_line_block_puts_the_range_19_px_under_the_title()
    {
        using var harness = new ScreenHarness();
        var (view, window) = Open(harness);

        var meal = Block(view, "12:00 – 13:00");
        var t = BoundsIn(Part<StrikeText>(meal, "PART_BlockTitle"), meal);
        var r = BoundsIn(Part<TextBlock>(meal, "PART_Range"), meal);
        Assert.Equal(7, t.Y, 3);
        Assert.Equal(26, r.Y, 3);
        Assert.Equal(t.X, r.X, 3);
        window.Close();
    }

    [AvaloniaFact]
    public void Block_titles_strike_with_StrikeText_bound_to_done()
    {
        using var harness = new ScreenHarness();
        var (view, window) = Open(harness);

        foreach (var block in view.Schedule.BlockList.GetVisualDescendants().OfType<Button>()
                     .Where(b => b.DataContext is ScheduleBlockViewModel))
        {
            var vm = (ScheduleBlockViewModel)block.DataContext!;
            var title = Part<StrikeText>(block, "PART_BlockTitle");
            Assert.Equal(vm.Row.Done, title.Struck);
            Assert.Null(title.TextDecorations); // the crisp line is StrikeText's own, not a font-metric decoration
        }

        // Completing a block fades its strike in (the same StrikeText, no rebuild).
        var plan = Block(view, "22:00 – 22:45");
        var planTitle = Part<StrikeText>(plan, "PART_BlockTitle");
        Assert.False(planTitle.Struck);
        ((ScheduleBlockViewModel)plan.DataContext!).Row.ToggleCommand.Execute(null);
        Settle(window);
        Assert.True(planTitle.Struck);
        Assert.Same(planTitle, Part<StrikeText>(plan, "PART_BlockTitle"));
        Assert.True(Pump.Until(() => planTitle.StrokeColor == Pixels.Resource("Color.Text.Muted"), TimeSpan.FromSeconds(2)));
        window.Close();
    }
}
