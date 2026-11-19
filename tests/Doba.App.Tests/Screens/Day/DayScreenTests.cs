using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Motion;
using Doba.App.Screens.Day;
using Doba.App.Tests.DesignSystem;
using Doba.App.Tests.Support;
using Doba.App.ViewModels;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using static Doba.App.Tests.Screens.Inbox.ListScreenKit;

namespace Doba.App.Tests.Screens.Day;

public class DayScreenTests
{
    /// <summary>A day of the demo data with no tasks and no spans (inside the seed's hand-written gap, 26 Oct – 6 Dec).</summary>
    private static readonly DateOnly s_emptyDay = new(2026, 10, 28);

    /// <summary>Covered by "Portfolio site redesign" (11–14 Nov) and three single-day tasks.</summary>
    private static readonly DateOnly s_spanDay = new(2026, 11, 12);

    private static (DayViewModel Vm, DayView View) Open(ScreenHarness harness, DateOnly? anchor = null)
    {
        var vm = new DayViewModel(harness.Services);
        vm.Show(new NavState(AppView.Day, anchor ?? harness.App.DayClock.Today));
        return (vm, new DayView { DataContext = vm });
    }

    private static DayModel Project(ScreenHarness h, DateOnly date) => Projector.Day(h.App.Store,
        new ProjectionContext(h.App.DayClock.Today, h.App.DayClock.Now, h.App.Settings.Current.WeekStart, h.FilterTag), date);

    private static List<DayCard> Cards(Visual root) =>
        [.. root.GetVisualDescendants().OfType<DayCard>().Where(c => c.IsEffectivelyVisible)];

    private static List<DaySpanRow> SpanRows(Visual root) =>
        [.. root.GetVisualDescendants().OfType<DaySpanRow>().Where(c => c.IsEffectivelyVisible)];

    private static DayCard CardOf(Visual root, TaskRowViewModel row) => Cards(root).Single(c => ReferenceEquals(c.DataContext, row));

    private static TaskItem NewTask(DateOnly date, string title, Priority priority = Priority.None, string? tag = null, TimeOnly? time = null, DateOnly? end = null) =>
        new(0, title, string.Empty, date, end, time, time is null ? 0 : 30, priority, tag, false);

    [AvaloniaFact]
    public void Spans_show_day_k_of_n_before_the_cards()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness, s_spanDay);
        var window = Host(view);
        var model = Project(harness, s_spanDay);

        Assert.NotEmpty(model.Spans);
        Assert.Equal(model.Spans.Select(s => s.Id), vm.Spans.Select(s => s.Id));
        var portfolio = vm.Spans.Single(s => s.Title == "Portfolio site redesign");
        Assert.Equal("Day 2 of 4", portfolio.Meta);

        var rows = SpanRows(view);
        Assert.Equal(vm.Spans.Count, rows.Count);
        Assert.Equal("Day 2 of 4", rows.Single(r => ReferenceEquals(r.DataContext, portfolio)).MetaText.Text);

        // Spans sit above the first card, the card list above "Add task".
        var firstCard = Cards(view)[0];
        Assert.True(rows[^1].TranslatePoint(default, view)!.Value.Y < firstCard.TranslatePoint(default, view)!.Value.Y);
        Assert.True(firstCard.TranslatePoint(default, view)!.Value.Y < view.AddTask.TranslatePoint(default, view)!.Value.Y);
        Assert.Equal(38, rows[0].Bounds.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void Cards_follow_the_projection_order_and_the_progress_text()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var model = Project(harness, harness.App.DayClock.Today);

        Assert.Equal(model.Items.Select(r => r.Id), vm.Items.Select(r => r.Id));
        Assert.Equal(model.Items.Select(r => r.Id), Cards(view).Select(c => ((TaskRowViewModel)c.DataContext!).Id));
        Assert.Empty(vm.Spans);
        Assert.Empty(SpanRows(view));
        Assert.Equal("5 / 10 done", vm.Progress);
        Assert.Equal("5 / 10 done", view.ProgressText.Text);
        Assert.False(view.EmptyText.IsVisible);

        // By time: the untimed task comes last.
        Assert.Equal("Morning run along the river", vm.Items[0].Title);
        Assert.Equal("Water the plants and repot the ficus", vm.Items[^1].Title);

        // The design's card heights (docs/design/screens/day.png): 62 px, 64 with a priority button; gap 8.
        var cards = Cards(view);
        Assert.Equal(62, cards[0].Bounds.Height);
        Assert.Equal(64, cards[^1].Bounds.Height);
        Assert.Equal(8, cards[1].TranslatePoint(default, view)!.Value.Y - cards[0].TranslatePoint(default, view)!.Value.Y - 62);

        // The "TASKS · N / M done" row is lifted 1 px (its text drew 1 px low) while the first card stays 20 + 8 below
        // the top of the list, as in the reference (header text ink at 151, first card at 176).
        var header = view.ProgressText.GetVisualParent<Grid>()!;
        Assert.Equal(-1, header.TranslatePoint(default, view.List)!.Value.Y, 0.5);
        Assert.Equal(20 + 8, cards[0].TranslatePoint(default, view.List)!.Value.Y, 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void Meta_row_shows_priority_name_tag_and_time_only_when_present()
    {
        using var harness = new ScreenHarness();
        var store = harness.App.Store;
        var bare = store.Add(NewTask(s_emptyDay, "Bare task"));
        var high = store.Add(NewTask(s_emptyDay, "High only", Priority.High));
        var tagged = store.Add(NewTask(s_emptyDay, "Tag and time", tag: "work", time: new TimeOnly(9, 30)));
        var (_, view) = Open(harness, s_emptyDay);
        var window = Host(view);

        DayCard Card(long id) => Cards(view).Single(c => ((TaskRowViewModel)c.DataContext!).Id == id);

        Assert.False(Card(bare.Id).MetaRow.IsVisible);

        var highCard = Card(high.Id);
        Assert.True(highCard.MetaRow.IsVisible);
        Assert.True(highCard.PriorityButton.IsVisible);
        Assert.Equal("High", highCard.PriorityText.Text);
        Assert.False(highCard.MetaRow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Tag").IsVisible);

        var taggedCard = Card(tagged.Id);
        Assert.True(taggedCard.MetaRow.IsVisible);
        Assert.False(taggedCard.PriorityButton.IsVisible);
        var texts = taggedCard.MetaRow.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Equal(["#work", "09:30"], texts);

        // Cycling the bare task's priority brings its meta row in place (same card).
        var bareCard = Card(bare.Id);
        harness.Actions.CyclePriority(bare.Id);
        Settle(window);
        Assert.Same(bareCard, Card(bare.Id));
        Assert.True(bareCard.MetaRow.IsVisible);
        Assert.Equal("Low", bareCard.PriorityText.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Empty_day_shows_Nothing_here_and_no_progress()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness, s_emptyDay);
        var window = Host(view);

        Assert.True(vm.IsEmpty);
        Assert.True(view.EmptyText.IsEffectivelyVisible);
        Assert.Equal("Nothing here", view.EmptyText.Text);
        Assert.Equal(string.Empty, vm.Progress);
        Assert.Empty(Cards(view));
        Assert.True(view.AddTask.IsEffectivelyVisible);
        Assert.False(vm.Schedule.HasNow);
        window.Close();
    }

    [AvaloniaFact]
    public void Span_only_day_is_not_empty_and_has_no_progress()
    {
        using var harness = new ScreenHarness();
        var day = new DateOnly(2026, 10, 31);
        harness.App.Store.Add(NewTask(new DateOnly(2026, 10, 30), "Weekend away", end: day));
        var (vm, view) = Open(harness, day);
        var window = Host(view);

        Assert.False(vm.IsEmpty);
        Assert.False(view.EmptyText.IsVisible);
        Assert.Equal(string.Empty, vm.Progress);
        Assert.Equal(string.Empty, view.ProgressText.Text);
        Assert.Equal("Day 2 of 2", Assert.Single(vm.Spans).Meta);
        Assert.Empty(Cards(view));
        window.Close();
    }

    [AvaloniaFact]
    public void Now_line_is_shown_only_for_today()
    {
        using var harness = new ScreenHarness();
        var (today, todayView) = Open(harness);
        var w1 = Host(todayView);
        Assert.True(today.Schedule.HasNow);
        Assert.True(todayView.Schedule.NowLine.IsEffectivelyVisible);
        Assert.Equal("22:26", today.Schedule.NowLabel);
        Assert.Equal(741, today.Schedule.NowTop);
        w1.Close();

        var (other, otherView) = Open(harness, s_spanDay);
        var w2 = Host(otherView);
        Assert.False(other.Schedule.HasNow);
        Assert.False(otherView.Schedule.NowLine.IsEffectivelyVisible);

        // A minute tick does not bring it in on another day.
        Clock(harness).Advance(TimeSpan.FromMinutes(1));
        harness.App.DayClock.Check();
        Settle(w2);
        Assert.False(other.Schedule.HasNow);
        w2.Close();
    }

    [AvaloniaFact]
    public void Minute_tick_moves_only_the_now_line()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var items = vm.Items.ToList();
        var blocks = vm.Schedule.Blocks.ToList();
        var refreshes = vm.Refreshes;
        var before = view.Schedule.NowLine.Bounds.Y;

        Clock(harness).Advance(TimeSpan.FromMinutes(1));
        harness.App.DayClock.Check();
        Settle(window);

        Assert.Equal("22:27", vm.Schedule.NowLabel);
        Assert.Equal(742, vm.Schedule.NowTop);
        Assert.Equal(before + 1, view.Schedule.NowLine.Bounds.Y);
        Assert.Equal(refreshes, vm.Refreshes); // no re-projection
        Assert.Equal(items, vm.Items);
        Assert.Equal(blocks, vm.Schedule.Blocks);
        window.Close();
    }

    [AvaloniaFact]
    public void Checkbox_priority_title_and_block_call_the_task_actions()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var store = harness.App.Store;
        var open = vm.Items.Single(r => r.Title == "Water the plants and repot the ficus");
        var card = CardOf(view, open);

        card.GetVisualDescendants().OfType<TaskCheckBox>().Single().Command!.Execute(null);
        Settle(window);
        Assert.True(store.Get(open.Id)!.Done);

        var before = store.Get(open.Id)!.Priority;
        card.PriorityButton.Command!.Execute(null);
        Settle(window);
        Assert.Equal(before.Next(), store.Get(open.Id)!.Priority);
        Assert.Null(ToolTip.GetTip(card.PriorityButton)); // the design has no tooltip on the Day priority button

        card.TitleButton.Command!.Execute(null);
        Assert.Equal([open.Id], harness.Commands.Edits);

        // A schedule block toggles its task, as in the prototype.
        var plan = vm.Schedule.Blocks.Single(b => b.Row.Title.StartsWith("Plan next week", StringComparison.Ordinal));
        var planDone = store.Get(plan.Id)!.Done;
        view.Schedule.BlockList.GetVisualDescendants().OfType<Button>()
            .Single(b => ReferenceEquals(b.DataContext, plan)).Command!.Execute(null);
        Settle(window);
        Assert.Equal(!planDone, store.Get(plan.Id)!.Done);
        window.Close();
    }

    [AvaloniaFact]
    public void Span_click_toggles_the_span()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness, s_spanDay);
        var window = Host(view);
        var span = vm.Spans.Single(s => s.Title == "Portfolio site redesign");
        var row = SpanRows(view).Single(r => ReferenceEquals(r.DataContext, span));
        Assert.False(span.Done);

        row.Bar.Command!.Execute(null);
        Settle(window);

        Assert.True(harness.App.Store.Get(span.Id)!.Done);
        Assert.Same(span, vm.Spans.Single(s => s.Id == span.Id));
        Assert.Same(row, SpanRows(view).Single(r => ReferenceEquals(r.DataContext, span)));
        Assert.Contains("done", row.Bar.Classes);
        window.Close();
    }

    [AvaloniaFact]
    public void Add_task_opens_a_new_task_for_the_shown_day()
    {
        using var harness = new ScreenHarness();
        var (_, view) = Open(harness, s_spanDay);
        var window = Host(view);

        view.AddTask.Button.Command!.Execute(null);

        Assert.Equal([((DateOnly?)s_spanDay, true)], harness.Commands.NewTasks);

        // "Add task" is the last thing in the list.
        Assert.Same(view.AddTask, view.List.Children[^1]);
        window.Close();
    }

    [AvaloniaFact]
    public void Completing_a_task_keeps_its_row_view_models_and_controls()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var row = vm.Items.Single(r => r.Title.StartsWith("Plan next week", StringComparison.Ordinal));
        var card = CardOf(view, row);
        var block = vm.Schedule.Blocks.Single(b => b.Id == row.Id);
        var items = vm.Items.ToList();

        row.ToggleCommand.Execute(null);
        Settle(window);

        Assert.True(row.Done);
        Assert.Equal(items, vm.Items); // same instances, same order
        Assert.Same(card, CardOf(view, row));
        Assert.Contains("done", card.TitleButton.Classes);
        Assert.Same(block, vm.Schedule.Blocks.Single(b => b.Id == row.Id));
        Assert.True(block.Row.Done);
        Assert.Equal("6 / 10 done", vm.Progress);
        Assert.Equal("6 / 10 done", view.ProgressText.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Tag_filter_dims_other_tags()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);

        harness.FilterTag = "work";
        vm.RefreshIfActive();
        Settle(window);

        var cards = Cards(view);
        Assert.NotEmpty(cards);
        foreach (var card in cards)
        {
            var task = (TaskRowViewModel)card.DataContext!;
            Assert.Equal(task.TagLabel != "#work", card.Surface.Classes.Contains("dimmed"));
        }

        Assert.Contains(cards, c => !c.Surface.Classes.Contains("dimmed"));
        Assert.Contains(cards, c => c.Surface.Classes.Contains("dimmed"));
        window.Close();
    }

    [AvaloniaFact]
    public void Tag_filter_dims_spans_of_other_tags()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness, s_spanDay);
        var window = Host(view);

        harness.FilterTag = "work";
        vm.RefreshIfActive();
        Settle(window);

        var row = SpanRows(view).Single(r => ((TaskRowViewModel)r.DataContext!).Title == "Portfolio site redesign"); // #personal
        Assert.Contains("dimmed", row.Bar.Classes);
        window.Close();
    }

    [AvaloniaFact]
    public void A_task_created_through_the_dialog_plays_the_entrance()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var existing = vm.Items.ToList();

        // What the dialog does: add, then mark the new task for the entrance.
        var created = harness.App.Store.Add(NewTask(harness.App.DayClock.Today, "Fresh task", time: new TimeOnly(23, 0)));
        harness.Actions.MarkCreated(created.Id);
        Settle(window);

        Assert.Null(harness.Actions.PendingEntranceId); // consumed by the new card
        var row = vm.Items.Single(r => r.Id == created.Id);
        var card = CardOf(view, row);
        Assert.True(Animator.IsAnimating(card));
        Assert.Equal(Doba.App.Motion.Motion.NewRowY, Animator.Running(card)!.From.Y);
        Assert.All(existing, r => Assert.Contains(r, vm.Items)); // the others stay
        Assert.All(existing, r => Assert.False(Animator.IsAnimating(CardOf(view, r))));
        window.Close();
    }

    [AvaloniaFact]
    public void A_long_title_wraps_inside_the_680_px_list()
    {
        using var harness = new ScreenHarness();
        var title = string.Join(' ', Enumerable.Repeat("Prepare the quarterly report draft", 4));
        var task = harness.App.Store.Add(NewTask(s_emptyDay, title));
        var (vm, view) = Open(harness, s_emptyDay);
        var window = Host(view);

        // max-width 680, left-aligned: exactly 680 px in the 1315 px content area.
        var list = view.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_List");
        Assert.Equal(680, list.Bounds.Width);
        Assert.Equal(680, view.AddTask.Bounds.Width);

        var card = CardOf(view, vm.Items.Single(r => r.Id == task.Id));
        var text = card.GetVisualDescendants().OfType<StrikeText>().Single();
        Assert.True(text.TextLayout.TextLines.Count >= 2, $"{text.TextLayout.TextLines.Count} line(s)");
        Assert.Equal(title, text.Text);                       // wrapped, never trimmed
        var lines = text.TextLayout.TextLines.Count;
        Assert.Equal(22 + (lines * 18), card.Bounds.Height); // padding 11 + 11, one 18 px line each (no meta row)
        Assert.True(card.Bounds.Width <= 680);
        window.Close();
    }

    [AvaloniaFact]
    public void At_1100x700_the_schedule_scrolls_opens_on_the_now_line_and_reaches_every_hour()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        // Content area of a 1100×700 window (the 1600×1000 one is 1315×832).
        var window = ScreenHarness.Host(view, ScreenHarness.ContentWidth - 500, ScreenHarness.ContentHeight - 300);
        Settle(window);
        Assert.True(Pump.Until(() => view.ScheduleScroller.Offset.Y > 0, TimeSpan.FromSeconds(2)));

        var scroller = view.ScheduleScroller;
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
        Assert.True(InView(scroller, view.Schedule.NowLine)); // today at 22:26: opened on the now-line

        // From the top, the evening is out of view and can be scrolled to: the 22:00 hour row and its block.
        scroller.Offset = default;
        Settle(window);
        var hours = view.Schedule.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "PART_Hours");
        var hour22 = hours.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "22:00");
        Assert.False(InView(scroller, hour22));
        hour22.BringIntoView();
        Settle(window);
        Assert.True(InView(scroller, hour22));
        var plan = view.Schedule.BlockList.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is ScheduleBlockViewModel { Range: "22:00 – 22:45" });
        plan.BringIntoView();
        Settle(window);
        Assert.True(InView(scroller, plan));
        Assert.Equal(vm.Schedule.Blocks.Count, view.Schedule.BlockList.GetVisualDescendants().OfType<Button>().Count());

        // Another day (the cached view is reused): no now-line, so it opens on that day's earliest block (20:00, below
        // the first screenful).
        harness.App.Store.Add(NewTask(s_emptyDay, "Evening call", time: new TimeOnly(20, 0)));
        scroller.Offset = default;
        vm.Show(new NavState(AppView.Day, s_emptyDay));
        Settle(window);
        Assert.True(Pump.Until(() => view.Schedule.FirstBlock is { } b && InView(scroller, b), TimeSpan.FromSeconds(2)));
        Assert.True(scroller.Offset.Y > 0);
        Assert.False(view.Schedule.NowLine.IsVisible);
        window.Close();

        static bool InView(ScrollViewer scroller, Control element)
        {
            var top = element.TranslatePoint(default, scroller)!.Value.Y;
            return top >= 0 && top + element.Bounds.Height <= scroller.Viewport.Height;
        }
    }

    [AvaloniaFact]
    public void At_1600x1000_the_schedule_does_not_scroll()
    {
        using var harness = new ScreenHarness();
        var (_, view) = Open(harness);
        var window = Host(view);
        Settle(window);
        Assert.Equal(0, view.ScheduleScroller.Offset.Y);
        Assert.True(view.ScheduleScroller.Extent.Height <= view.ScheduleScroller.Viewport.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void Day_content_screenshot()
    {
        using var harness = new ScreenHarness();
        var (_, view) = Open(harness);
        var window = Host(view);
        var path = Screenshots.Capture(window, "day-content");
        Assert.True(new FileInfo(path).Length > 1000);
        window.Close();
    }
}
