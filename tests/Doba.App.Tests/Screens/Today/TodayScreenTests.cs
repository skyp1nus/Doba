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
        Assert.Equal(model.Progress, vm.Progress);
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
        var block = vm.Schedule.Blocks.Single(b => b.Id == row.Id);
        var progress = vm.Progress;

        row.ToggleCommand.Execute(null);
        Settle(window);

        Assert.True(row.Done);
        Assert.DoesNotContain(vm.Groups, g => g.Key == "sched");
        Assert.Contains(row, vm.Groups.Single(g => g.Key == "done").Items);
        Assert.Same(block, vm.Schedule.Blocks.Single(b => b.Id == row.Id)); // block updated in place
        Assert.True(block.Row.Done);
        Assert.True(vm.Progress > progress);
        Assert.Equal(Project(harness).Summary, vm.Summary);
        Assert.Equal(Project(harness).Groups.Single(g => g.Key == "done").Items.Select(r => r.Id),
            vm.Groups.Single(g => g.Key == "done").Items.Select(r => r.Id));
        Assert.Contains(Rows(view), r => ReferenceEquals(r.DataContext, row) && r.Classes.Contains("done"));
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
        Assert.Equal(0, row.StrikeBrush.Color.A); // transparent while open

        rowVm.Update(task with { Done = true });
        Assert.True(row.StrikeBrush.Color.A < 255); // transition started from transparent, not snapped
        Assert.True(Pump.Until(() => row.StrikeBrush.Color.A == 255, TimeSpan.FromSeconds(2)));
        Assert.Equal(Pixels.Resource("Color.Text.Muted"), row.StrikeBrush.Color);

        // Re-pointing the row at another (open) task shows its state at once.
        row.DataContext = new TaskRowViewModel(task with { Id = 999 }, harness.Actions, harness.Commands);
        Assert.Equal(0, row.StrikeBrush.Color.A);
        window.Close();
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
