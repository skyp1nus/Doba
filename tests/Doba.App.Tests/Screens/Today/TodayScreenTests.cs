using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Motion;
using Doba.App.Screens.Today;
using Doba.App.Tests.DesignSystem;
using Doba.App.Tests.Support;
using Doba.App.ViewModels;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using static Doba.App.Tests.Screens.Inbox.ListScreenKit;

namespace Doba.App.Tests.Screens.Today;

public class TodayScreenTests
{
    private static (TodayViewModel Vm, TodayView View) Open(ScreenHarness harness)
    {
        var vm = new TodayViewModel(harness.Services);
        vm.Show(new NavState(AppView.Today, harness.App.DayClock.Today));
        return (vm, new TodayView { DataContext = vm });
    }

    private static TodayModel Project(ScreenHarness h) => Projector.Today(h.App.Store,
        new ProjectionContext(h.App.DayClock.Today, h.App.DayClock.Now, h.App.Settings.Current.WeekStart, h.FilterTag));

    [AvaloniaFact]
    public void Groups_rows_and_blocks_follow_the_projection()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var model = Project(harness);

        Assert.Equal(model.Groups.Select(g => g.Key), vm.Groups.Select(g => g.Key));
        Assert.Equal(model.Groups.Select(g => g.Items.Count), vm.Groups.Select(g => g.Items.Count));
        Assert.Equal(model.Summary, vm.Summary);
        Assert.Equal(model.Progress, vm.Progress); // the projection's whole percent, copied
        Assert.Equal(0.5, vm.Progress); // 5 of 10 done
        Assert.DoesNotContain(model.Schedule.Blocks, b => b.IsHidden); // 15 November: at most three at once
        Assert.Equal(model.Schedule.Blocks.Select(b => b.Row.Id), vm.Schedule.Blocks.Select(b => b.Id));

        // The whole list fits: one row control per row, one section per group, one button per block.
        Assert.Equal(vm.Groups.Sum(g => g.Items.Count), Rows(view).Count);
        Assert.Equal(vm.Groups.Count, view.GetVisualDescendants().OfType<TaskGroupList>().Count());
        Assert.Equal(vm.Schedule.Blocks.Count, view.Schedule.BlockList.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Count());

        // Geometry at 48 px/hour: 22:00 → top 721; 45 min → 33 px (compact); now 22:26 → 741.
        var plan = vm.Schedule.Blocks.Single(b => b.Range == "22:00 – 22:45");
        Assert.Equal(721, plan.Top);
        Assert.Equal(33, plan.Height);
        Assert.True(plan.IsCompact);
        Assert.Equal(741, vm.Schedule.NowTop);
        Assert.Equal("22:26", vm.Schedule.NowLabel);

        // Overdue single-day rows offer "→ Today"; the span does not.
        var overdue = vm.Groups.Single(g => g.Key == "over");
        Assert.Contains(overdue.Items, r => r.CanMoveToToday);
        Assert.Contains(overdue.Items, r => !r.CanMoveToToday && r.IsMultiDay);
        window.Close();
    }

    [AvaloniaFact]
    public void Completing_a_task_keeps_its_view_models_and_moves_the_row_to_Done()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var row = vm.Groups.Single(g => g.Key == "sched").Items[0];
        var scheduled = vm.Groups.Single(g => g.Key == "sched").Items.Count;
        var block = vm.Schedule.Blocks.Single(b => b.Id == row.Id);
        var progress = vm.Progress;

        row.ToggleCommand.Execute(null);
        Settle(window);

        Assert.True(row.Done);
        Assert.DoesNotContain(row, vm.Groups.Single(g => g.Key == "sched").Items);
        Assert.Equal(scheduled - 1, vm.Groups.Single(g => g.Key == "sched").Items.Count);
        Assert.Contains(row, vm.Groups.Single(g => g.Key == "done").Items);
        Assert.Same(block, vm.Schedule.Blocks.Single(b => b.Id == row.Id)); // block updated in place
        Assert.True(block.Row.Done);
        Assert.True(vm.Progress > progress);
        Assert.Equal(Project(harness).Summary, vm.Summary);
        Assert.Equal(Project(harness).Groups.Single(g => g.Key == "done").Items.Select(r => r.Id),
            vm.Groups.Single(g => g.Key == "done").Items.Select(r => r.Id));
        Assert.Contains(Rows(view), r => ReferenceEquals(r.DataContext, row) && r.Classes.Contains("done"));

        // Completing the remaining scheduled rows removes the group.
        foreach (var other in vm.Groups.Single(g => g.Key == "sched").Items.ToList())
        {
            other.ToggleCommand.Execute(null);
            Settle(window);
        }

        Assert.DoesNotContain(vm.Groups, g => g.Key == "sched");
        window.Close();
    }

    [AvaloniaFact]
    public void Row_state_changes_animate_the_strike_colour_but_recycling_does_not()
    {
        using var harness = new ScreenHarness();
        var task = Project(harness).Groups.Single(g => g.Key == "any").Items[0];
        var rowVm = new TaskRowViewModel(task, harness.Actions, harness.Commands);
        var row = new TaskListRow { DataContext = rowVm };
        var window = ScreenHarness.Host(row, 700, 60);
        Settle(window);
        var title = row.TitleText!;
        Assert.False(title.Struck);
        Assert.Equal(0, title.StrokeColor.A); // transparent while open

        rowVm.Update(task with { Done = true });
        Assert.True(title.Struck);
        Assert.True(title.StrokeColor.A < 255); // transition started from transparent, not snapped
        Assert.True(Pump.Until(() => title.StrokeColor.A == 255, TimeSpan.FromSeconds(2)));
        Assert.Equal(Pixels.Resource("Color.Text.Muted"), title.StrokeColor);

        // Re-pointing the row at another (open) task shows its state at once.
        row.DataContext = new TaskRowViewModel(task with { Id = 999 }, harness.Actions, harness.Commands);
        Assert.False(row.TitleText!.Struck);
        Assert.Equal(0, row.TitleText.StrokeColor.A);

        // … and at a done task too (no fade in from transparent).
        row.DataContext = new TaskRowViewModel(task with { Id = 998, Done = true }, harness.Actions, harness.Commands);
        Assert.True(row.TitleText!.Struck);
        Assert.Equal(Pixels.Resource("Color.Text.Muted"), row.TitleText.StrokeColor);

        // Re-pointing at a task in the same state keeps the title (no rebuild).
        var kept = row.TitleText;
        row.DataContext = new TaskRowViewModel(task with { Id = 997, Done = true }, harness.Actions, harness.Commands);
        Settle(window);
        Assert.Same(kept, row.TitleText);
        window.Close();
    }

    [AvaloniaFact]
    public void A_recycled_row_shows_the_new_tasks_dim_checkbox_and_title_at_once()
    {
        using var harness = new ScreenHarness();
        var task = Project(harness).Groups.Single(g => g.Key == "any").Items[0];
        var dimmedDone = new TaskRowViewModel(task with { Done = true, Dimmed = true }, harness.Actions, harness.Commands);
        var row = new TaskListRow { DataContext = dimmedDone };
        var window = ScreenHarness.Host(row, 700, 60);
        Settle(window);
        Assert.True(Pump.Until(() => row.Opacity < 1, TimeSpan.FromSeconds(2))); // rested dimmed
        var box = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Box");
        var presenter = row.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
            .Single(p => p.Name == "PART_ContentPresenter" && p.TemplatedParent is Button { Name: "PART_Title" });
        var open = Pixels.Resource("Color.Text.Primary");
        Assert.True(Pump.Until(() => ColorOf(presenter.Foreground) == Pixels.Resource("Color.Text.Muted"), TimeSpan.FromSeconds(2)));
        Assert.True(Pump.Until(() => ColorOf(box.Background) == Pixels.Resource("Brush.Accent"), TimeSpan.FromSeconds(2)));

        // Re-pointed at an open, undimmed task: every state is final at once (no 300 ms dim fade, no 250 ms checkbox
        // fade, no title colour fade, no strike fade).
        var openRow = new TaskRowViewModel(task with { Id = 999 }, harness.Actions, harness.Commands);
        row.DataContext = openRow;
        Assert.Equal(1, row.Opacity);
        Assert.Equal(0, ColorOf(box.Background).A);
        Assert.Equal(open, ColorOf(presenter.Foreground));
        Assert.Equal(0, row.TitleText!.StrokeColor.A);

        // … and back at a dimmed, done task.
        row.DataContext = dimmedDone;
        Assert.True(Application.Current!.TryGetResource("Opacity.Dimmed", null, out var dim));
        Assert.Equal((double)dim!, row.Opacity, 3);
        Assert.Equal(Pixels.Resource("Brush.Accent"), ColorOf(box.Background));
        Assert.Equal(Pixels.Resource("Color.Text.Muted"), row.TitleText!.StrokeColor);

        // Changes of the shown task still animate.
        dimmedDone.Update(dimmedDone.Row with { Dimmed = false });
        Assert.True(row.Opacity < 1);
        Assert.True(Pump.Until(() => row.Opacity == 1, TimeSpan.FromSeconds(2)));
        window.Close();

        static Avalonia.Media.Color ColorOf(Avalonia.Media.IBrush? brush) =>
            brush is Avalonia.Media.ISolidColorBrush s ? s.Color : default;
    }

    [AvaloniaFact]
    public void Move_to_today_moves_the_task_and_plays_the_entrance()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var row = vm.Groups.Single(g => g.Key == "over").Items.First(r => r.CanMoveToToday);
        var move = RowOf(view, row).GetVisualDescendants().OfType<Avalonia.Controls.Button>().Single(b => b.Name == "PART_Move");
        Assert.True(move.IsEffectivelyVisible);

        move.Command!.Execute(null);
        Assert.Equal(row.Id, harness.Actions.PendingEntranceId);
        Settle(window);

        Assert.Equal(harness.App.DayClock.Today, harness.App.Store.Get(row.Id)!.Date);
        Assert.False(row.CanMoveToToday);
        Assert.Contains(row, vm.Groups.Single(g => g.Key == "any").Items); // same view-model, now under Anytime
        Assert.Null(harness.Actions.PendingEntranceId); // consumed by the new row
        var control = RowOf(view, row);
        Assert.True(Animator.IsAnimating(control));
        Assert.Equal(Doba.App.Motion.Motion.NewRowY, Animator.Running(control)!.From.Y);
        window.Close();
    }

    [AvaloniaFact]
    public void Add_task_opens_a_new_task_for_today()
    {
        using var harness = new ScreenHarness();
        var (_, view) = Open(harness);
        var window = Host(view);

        view.GetVisualDescendants().OfType<AddTaskButton>().Single().Button.Command!.Execute(null);

        Assert.Equal([((DateOnly?)harness.App.DayClock.Today, true)], harness.Commands.NewTasks);
        window.Close();
    }

    [AvaloniaFact]
    public void Minute_tick_moves_only_the_now_line()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var blocks = vm.Schedule.Blocks.ToList();
        var groups = vm.Groups.ToList();
        var refreshes = vm.Refreshes;
        var before = view.Schedule.NowLine.Bounds.Y;

        Clock(harness).Advance(TimeSpan.FromMinutes(1));
        harness.App.DayClock.Check();
        Settle(window);

        Assert.Equal("22:27", vm.Schedule.NowLabel);
        Assert.Equal(742, vm.Schedule.NowTop);
        Assert.Equal(before + 1, view.Schedule.NowLine.Bounds.Y);
        Assert.Equal(refreshes, vm.Refreshes); // no re-projection
        Assert.Equal(blocks, vm.Schedule.Blocks);
        Assert.Equal(groups, vm.Groups);
        window.Close();
    }

    [AvaloniaFact]
    public void Tag_filter_dims_rows_and_blocks()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);

        harness.FilterTag = "work";
        vm.RefreshIfActive();
        Settle(window);

        foreach (var r in Rows(view))
        {
            var task = (TaskRowViewModel)r.DataContext!;
            Assert.Equal(task.TagLabel != "#work", r.Classes.Contains("dimmed"));
        }

        var blockButtons = view.Schedule.BlockList.GetVisualDescendants().OfType<Avalonia.Controls.Button>().ToList();
        Assert.Contains(blockButtons, b => b.Classes.Contains("dimmed"));
        Assert.Contains(blockButtons, b => !b.Classes.Contains("dimmed"));
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_title_opens_the_task_and_the_priority_icon_cycles_it_in_place()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var group = vm.Groups.Single(g => g.Key == "any");
        var row = group.Items[0];
        var before = row.Priority;

        Click(window, Part<Avalonia.Controls.Button>(RowOf(view, row), "PART_Title"));
        Assert.Equal([row.Id], harness.Commands.Edits);

        Click(window, Part<Avalonia.Controls.Button>(RowOf(view, row), "PART_Priority"));
        var after = harness.App.Store.Get(row.Id)!.Priority;
        Assert.NotEqual(before, after);
        Assert.Equal(after, row.Priority);
        Assert.Same(row, vm.Groups.Single(g => g.Key == "any").Items.Single(r => r.Id == row.Id)); // same view-model
        Assert.Equal([row.Id], harness.Commands.Edits); // the priority click opened nothing
        Assert.False(row.Done);
        window.Close();
    }

    [AvaloniaFact]
    public void Space_on_a_focused_title_completes_the_task()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var row = vm.Groups.Single(g => g.Key == "any").Items[0];

        Assert.True(Part<Avalonia.Controls.Button>(RowOf(view, row), "PART_Title").Focus(Avalonia.Input.NavigationMethod.Tab));
        PressSpace(window);

        Assert.True(harness.App.Store.Get(row.Id)!.Done);
        Assert.Contains(row, vm.Groups.Single(g => g.Key == "done").Items);
        Assert.Empty(harness.Commands.Edits);
        window.Close();
    }

    [AvaloniaFact]
    public void Midnight_while_open_shows_the_new_day()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var openAnytime = vm.Groups.Single(g => g.Key == "any").Items.ToList();

        Clock(harness).Advance(TimeSpan.FromHours(2)); // 22:26 → 00:26 the next day
        harness.App.DayClock.Check();
        Settle(window);

        var model = Project(harness);
        Assert.Equal(Shape(model.Groups), Shape(vm.Groups));
        Assert.Equal(model.Summary, vm.Summary);
        // 16 November: four at 10:00–10:20 → two blocks fold into "+2" and are not drawn.
        Assert.Equal(model.Schedule.Blocks.Where(b => !b.IsHidden).Select(b => b.Row.Id), vm.Schedule.Blocks.Select(b => b.Id));
        Assert.Equal(2, model.Schedule.Blocks.Count(b => b.IsHidden));
        Assert.All(openAnytime, r => Assert.Contains(r, vm.Groups.Single(g => g.Key == "over").Items)); // yesterday's open task is overdue
        Assert.False(vm.Schedule.HasNow); // 00:26 is off the 07–23 grid
        Assert.False(view.Schedule.NowLine.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void A_store_change_while_hidden_shows_on_the_next_show()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var refreshes = vm.Refreshes;

        vm.Hide();
        var row = vm.Groups.Single(g => g.Key == "any").Items[0];
        harness.App.Store.ToggleDone(row.Id);
        Assert.Equal(refreshes, vm.Refreshes);
        Assert.False(row.Done); // not re-projected while hidden

        vm.Show(new NavState(AppView.Today, harness.App.DayClock.Today));
        Settle(window);

        var model = Project(harness);
        Assert.Equal(Shape(model.Groups), Shape(vm.Groups));
        Assert.Equal(model.Summary, vm.Summary);
        Assert.True(row.Done);
        Assert.Contains(Rows(view), r => ReferenceEquals(r.DataContext, row) && r.Classes.Contains("done"));
        window.Close();
    }

    [AvaloniaFact]
    public void The_now_line_hides_off_the_07_to_23_grid()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        Assert.True(vm.Schedule.HasNow);

        Clock(harness).Advance(TimeSpan.FromMinutes(64)); // 22:26 → 23:30, same day: the minute tick path
        harness.App.DayClock.Check();
        Settle(window);
        Assert.False(vm.Schedule.HasNow);
        Assert.False(view.Schedule.NowLine.IsVisible);

        // The rule itself: 07:00 and 23:00 are on the grid, 02:00 and 23:30 are not.
        var schedule = new ScheduleViewModel(harness.Actions, harness.Commands);
        schedule.SetNow(7 * 60);
        Assert.True(schedule.HasNow);
        Assert.Equal(0, schedule.NowTop);
        schedule.SetNow(23 * 60);
        Assert.True(schedule.HasNow);
        schedule.SetNow(2 * 60);
        Assert.False(schedule.HasNow);
        schedule.SetNow((23 * 60) + 30);
        Assert.False(schedule.HasNow);
        window.Close();
    }

    [AvaloniaFact]
    public void At_1100x700_the_schedule_scrolls_and_opens_on_the_now_line()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        // Content area of a 1100×700 window (the 1600×1000 one is 1315×832).
        var window = ScreenHarness.Host(view, ScreenHarness.ContentWidth - 500, ScreenHarness.ContentHeight - 300);
        Settle(window);
        Assert.True(Pump.Until(() => view.ScheduleScroller.Offset.Y > 0, TimeSpan.FromSeconds(2)));

        var scroller = view.ScheduleScroller;
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
        Assert.True(InView(scroller, view.Schedule.NowLine)); // opened on the now-line (22:26)

        // Every block can be reached: scroll to the top, then bring the 22:00 block back.
        scroller.Offset = default;
        Settle(window);
        var plan = view.Schedule.BlockList.GetVisualDescendants().OfType<Avalonia.Controls.Button>()
            .Single(b => b.DataContext is ScheduleBlockViewModel { Range: "22:00 – 22:45" });
        Assert.False(InView(scroller, plan));
        plan.BringIntoView();
        Settle(window);
        Assert.True(InView(scroller, plan));
        Click(window, plan);
        Assert.True(harness.App.Store.Get(((ScheduleBlockViewModel)plan.DataContext!).Id)!.Done);
        Assert.Equal(vm.Schedule.Blocks.Count, view.Schedule.BlockList.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Count());
        window.Close();

        static bool InView(Avalonia.Controls.ScrollViewer scroller, Avalonia.Controls.Control element)
        {
            var top = element.TranslatePoint(default, scroller)!.Value.Y;
            return top >= 0 && top + element.Bounds.Height <= scroller.Viewport.Height;
        }
    }

    [AvaloniaFact]
    public void Today_content_screenshot()
    {
        using var harness = new ScreenHarness();
        var (_, view) = Open(harness);
        var window = Host(view);
        var path = Screenshots.Capture(window, "today-content");
        Assert.True(new FileInfo(path).Length > 1000);
        window.Close();
    }
}
