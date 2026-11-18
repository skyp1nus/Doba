using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Motion;
using Doba.App.Screens.Inbox;
using Doba.App.Tests.DesignSystem;
using Doba.App.Tests.Support;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using Doba.Core.Time;
using static Doba.App.Tests.Screens.Inbox.ListScreenKit;

namespace Doba.App.Tests.Screens.Inbox;

public class InboxScreenTests
{
    private static (InboxViewModel Vm, InboxView View) Open(ScreenHarness harness)
    {
        var vm = new InboxViewModel(harness.Services);
        vm.Show(new NavState(AppView.Inbox, harness.App.DayClock.Today));
        return (vm, new InboxView { DataContext = vm });
    }

    private static ProjectionContext Context(ScreenHarness h) =>
        new(h.App.DayClock.Today, h.App.DayClock.Now, h.App.Settings.Current.WeekStart, h.FilterTag);

    [AvaloniaFact]
    public void Groups_and_rows_follow_the_projection()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var model = Projector.Inbox(harness.App.Store, Context(harness));

        Assert.Equal(model.Left.Select(g => g.Key), vm.Left.Select(g => g.Key));
        Assert.Equal(model.Right.Select(g => g.Key), vm.Right.Select(g => g.Key));
        Assert.Equal(model.Left.Select(g => g.Items.Count), vm.Left.Select(g => g.Items.Count));
        Assert.Equal(model.Right.Select(g => g.Items.Count), vm.Right.Select(g => g.Items.Count));
        Assert.Equal(model.ScheduledCount, vm.ScheduledCount);

        // Every group has a section; the left column fits, so each of its rows is realised.
        var sections = view.GetVisualDescendants().OfType<TaskGroupList>().ToList();
        Assert.Equal(vm.Left.Count + vm.Right.Count, sections.Count);
        var leftRows = vm.Left.SelectMany(g => g.Items).ToHashSet();
        Assert.Equal(leftRows.Count, Rows(view).Count(r => leftRows.Contains((ViewModels.TaskRowViewModel)r.DataContext!)));
        Assert.Contains(Rows(view), r => r.DataContext is ViewModels.TaskRowViewModel { TagLabel: "#personal" });
        window.Close();
    }

    [AvaloniaFact]
    public void Completing_a_task_keeps_its_row_view_model_and_moves_it_to_Done()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var asap = vm.Left.First(g => g.Key == "p-asap");
        var row = asap.Items[0];
        var asapCount = asap.Items.Count;

        row.ToggleCommand.Execute(null);
        Settle(window);

        Assert.True(row.Done);
        Assert.Equal(asapCount - 1, vm.Left.FirstOrDefault(g => g.Key == "p-asap")?.Items.Count ?? 0);
        var done = vm.Left.Single(g => g.Key == "done");
        Assert.Contains(row, done.Items); // the same instance, not a rebuilt one
        Assert.Equal(Projector.Inbox(harness.App.Store, Context(harness)).Left.Single(g => g.Key == "done").Items.Select(r => r.Id),
            done.Items.Select(r => r.Id));
        Assert.Contains(Rows(view), r => ReferenceEquals(r.DataContext, row) && r.Classes.Contains("done"));
        window.Close();
    }

    [AvaloniaFact]
    public void Tag_filter_dims_rows_of_other_tags()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var groupsBefore = vm.Left.Concat(vm.Right).ToList();

        harness.FilterTag = "work";
        vm.RefreshIfActive();
        Settle(window);

        Assert.Equal(groupsBefore, vm.Left.Concat(vm.Right)); // same group instances
        var rows = Rows(view);
        Assert.Contains(rows, r => r.DataContext is ViewModels.TaskRowViewModel { TagLabel: "#work" });
        foreach (var r in rows)
        {
            var task = (ViewModels.TaskRowViewModel)r.DataContext!;
            var dimmed = task.TagLabel != "#work";
            Assert.Equal(dimmed, task.Dimmed);
            Assert.Equal(dimmed, r.Classes.Contains("dimmed"));
        }

        window.Close();
    }

    [AvaloniaFact]
    public void Add_task_opens_an_undated_new_task()
    {
        using var harness = new ScreenHarness();
        var (_, view) = Open(harness);
        var window = Host(view);
        var add = view.GetVisualDescendants().OfType<AddTaskButton>().Single();

        add.Button.Command!.Execute(null);

        Assert.Equal([((DateOnly?)null, true)], harness.Commands.NewTasks);
        window.Close();
    }

    [AvaloniaFact]
    public void Group_links_zoom_to_the_day_or_the_month_from_the_click()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var sections = view.GetVisualDescendants().OfType<TaskGroupList>().ToList();
        var today = harness.App.DayClock.Today;

        var todayGroup = sections.Single(s => s.DataContext is ViewModels.TaskGroupViewModel { Key: var k } && k == DateMath.Iso(today));
        todayGroup.LinkButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        var later = sections.Single(s => s.DataContext is ViewModels.TaskGroupViewModel { Key: "later" });
        later.LinkButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        Assert.Equal(2, harness.Commands.Zooms.Count);
        var (view1, anchor1, origin1) = harness.Commands.Zooms[0];
        Assert.Equal(AppView.Day, view1);
        Assert.Equal(today, anchor1);
        Assert.NotNull(origin1);
        Assert.InRange(origin1.Value.X, 0.5, 1); // right column
        Assert.InRange(origin1.Value.Y, 0, 1);

        var laterGroup = vm.Right.Single(g => g.Key == "later");
        Assert.Equal((AppView.Month, laterGroup.Link!.Anchor), (harness.Commands.Zooms[1].View, harness.Commands.Zooms[1].Anchor));
        window.Close();
    }

    [AvaloniaFact]
    public void Thousands_of_undated_tasks_realise_only_the_visible_rows()
    {
        using var harness = new ScreenHarness();
        for (var i = 0; i < 5000; i++)
        {
            harness.App.Store.Add(new TaskItem(0, "Undated task " + i, string.Empty, null, null, null, 0, Priority.None, null, false));
        }

        var (vm, view) = Open(harness);
        var window = Host(view);

        Assert.True(vm.Left.Single(g => g.Key == "p-none").Items.Count >= 5000);
        var realised = Rows(view).Count;
        Assert.InRange(realised, 10, 199);

        // Scrolling far down realises the rows there instead (still a bounded number).
        view.Scroller.Offset = new Avalonia.Vector(0, 100_000);
        Settle(window);
        var after = Rows(view);
        Assert.InRange(after.Count, 10, 199);
        Assert.Contains(after, r => r.DataContext is ViewModels.TaskRowViewModel { Title: var t } && t.StartsWith("Undated task ", StringComparison.Ordinal)
            && int.Parse(t["Undated task ".Length..], System.Globalization.CultureInfo.InvariantCulture) > 1000);
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_title_opens_the_task_for_editing()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var row = vm.Left.First(g => g.Key == "p-asap").Items[0];

        Click(window, Part<Avalonia.Controls.Button>(RowOf(view, row), "PART_Title"));

        Assert.Equal([row.Id], harness.Commands.Edits);
        Assert.False(row.Done); // the title does not toggle
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_the_priority_icon_cycles_the_priority_and_keeps_the_row_view_model()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var row = vm.Left.First(g => g.Key == "p-asap").Items[0];
        Assert.Equal(Priority.Asap, row.Priority);

        Click(window, Part<Avalonia.Controls.Button>(RowOf(view, row), "PART_Priority"));

        // ASAP → none: the task moves to "No priority" as the same view-model, showing the store's new priority.
        Assert.Equal(Priority.None, harness.App.Store.Get(row.Id)!.Priority);
        Assert.Equal(Priority.None, row.Priority);
        Assert.Contains(row, vm.Left.Single(g => g.Key == "p-none").Items);
        Assert.Empty(harness.Commands.Edits);
        Assert.Equal(Shape(Projector.Inbox(harness.App.Store, Context(harness)).Left), Shape(vm.Left));
        window.Close();
    }

    [AvaloniaFact]
    public void Space_on_a_focused_row_completes_the_task_instead_of_clicking_the_button()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var row = vm.Left.First(g => g.Key == "p-asap").Items[0];
        var control = RowOf(view, row);

        Assert.True(Part<Avalonia.Controls.Button>(control, "PART_Title").Focus(Avalonia.Input.NavigationMethod.Tab));
        PressSpace(window);

        Assert.True(row.Done);
        Assert.True(harness.App.Store.Get(row.Id)!.Done);
        Assert.Empty(harness.Commands.Edits); // the title button did not click

        // From the priority button too: Space toggles (back to open) and the priority stays.
        var priority = row.Priority;
        Assert.True(Part<Avalonia.Controls.Button>(RowOf(view, row), "PART_Priority").Focus(Avalonia.Input.NavigationMethod.Tab));
        PressSpace(window);
        Assert.False(row.Done);
        Assert.Equal(priority, harness.App.Store.Get(row.Id)!.Priority);
        window.Close();
    }

    [AvaloniaFact]
    public void Midnight_while_open_reprojects_the_day_groups()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);
        var tomorrow = harness.App.DayClock.Today.AddDays(1);
        var tomorrowGroup = vm.Right.Single(g => g.Key == DateMath.Iso(tomorrow));
        Assert.Equal("Tomorrow", tomorrowGroup.Label);
        var openToday = vm.Right.Single(g => g.Key == DateMath.Iso(harness.App.DayClock.Today)).Items.Where(r => !r.Done).ToList();
        Assert.NotEmpty(openToday);

        Clock(harness).Advance(TimeSpan.FromHours(2)); // 22:26 → 00:26 the next day
        harness.App.DayClock.Check();
        Settle(window);

        Assert.Equal(tomorrow, harness.App.DayClock.Today);
        var model = Projector.Inbox(harness.App.Store, Context(harness));
        Assert.Equal(Shape(model.Left), Shape(vm.Left));
        Assert.Equal(Shape(model.Right), Shape(vm.Right));
        Assert.Same(tomorrowGroup, vm.Right.Single(g => g.Key == DateMath.Iso(tomorrow))); // updated in place
        Assert.Equal("Today", tomorrowGroup.Label);
        Assert.Equal(GroupTone.Accent, tomorrowGroup.Tone);
        Assert.DoesNotContain(vm.Right, g => g.Key == DateMath.Iso(tomorrow.AddDays(-1))); // yesterday's group is gone …
        var overdue = vm.Right.Single(g => g.Tone == GroupTone.Warning).Items;
        Assert.All(openToday, r => Assert.Contains(r, overdue)); // … its open tasks are overdue (same view-models)
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
        var added = harness.App.Store.Add(new TaskItem(0, "Added while hidden", string.Empty, null, null, null, 0, Priority.Low, null, false));
        harness.App.Store.ToggleDone(vm.Left.First(g => g.Key == "p-asap").Items[0].Id);
        Assert.Equal(refreshes, vm.Refreshes); // hidden screens do not re-project

        vm.Show(new NavState(AppView.Inbox, harness.App.DayClock.Today));
        Settle(window);

        var model = Projector.Inbox(harness.App.Store, Context(harness));
        Assert.Equal(Shape(model.Left), Shape(vm.Left));
        Assert.Equal(Shape(model.Right), Shape(vm.Right));
        Assert.Contains(Rows(view), r => r.DataContext is ViewModels.TaskRowViewModel { Id: var id } && id == added.Id);
        window.Close();
    }

    [AvaloniaFact]
    public void A_pending_entrance_from_another_screen_does_not_play_later()
    {
        using var harness = new ScreenHarness();
        var (vm, view) = Open(harness);
        var window = Host(view);

        // Hidden (another screen is active, nothing there consumes it): created, then marked as the dialog does.
        vm.Hide();
        var created = harness.App.Store.Add(new TaskItem(0, "Created on Year", string.Empty, null, null, null, 0, Priority.None, null, false));
        harness.Actions.MarkCreated(created.Id);
        Assert.True(Pump.Until(() => harness.Actions.PendingEntranceId is null, TimeSpan.FromSeconds(3))); // expired

        vm.Show(new NavState(AppView.Inbox, harness.App.DayClock.Today));
        Settle(window);
        var row = Rows(view).Single(r => r.DataContext is ViewModels.TaskRowViewModel { Id: var id } && id == created.Id);
        Assert.False(Animator.IsAnimating(row));
        window.Close();
    }

    [AvaloniaFact]
    public void A_pending_entrance_left_before_the_first_show_does_not_play()
    {
        using var harness = new ScreenHarness();
        var created = harness.App.Store.Add(new TaskItem(0, "Created before Inbox opened", string.Empty, null, null, null, 0, Priority.None, null, false));
        harness.Actions.MarkCreated(created.Id);

        var (_, view) = Open(harness);
        Assert.Null(harness.Actions.PendingEntranceId); // the first projection drops it
        var window = Host(view);
        var row = Rows(view).Single(r => r.DataContext is ViewModels.TaskRowViewModel { Id: var id } && id == created.Id);
        Assert.False(Animator.IsAnimating(row));
        window.Close();
    }

    [AvaloniaFact]
    public void A_new_row_outside_the_viewport_does_not_keep_its_entrance()
    {
        using var harness = new ScreenHarness();
        for (var i = 0; i < 300; i++)
        {
            harness.App.Store.Add(new TaskItem(0, "Undated task " + i, string.Empty, null, null, null, 0, Priority.None, null, false));
        }

        var (vm, view) = Open(harness);
        var window = Host(view);

        // Sorted last in "No priority", far below the viewport: never realised, so never consumed.
        var created = harness.App.Store.Add(new TaskItem(0, "zzz new", string.Empty, null, null, null, 0, Priority.None, null, false));
        harness.Actions.MarkCreated(created.Id);
        Settle(window);
        Assert.DoesNotContain(Rows(view), r => r.DataContext is ViewModels.TaskRowViewModel { Id: var id } && id == created.Id);
        Assert.True(Pump.Until(() => harness.Actions.PendingEntranceId is null, TimeSpan.FromSeconds(3)));

        // Scrolled to later: the row shows without the entrance.
        var group = vm.Left.Single(g => g.Key == "p-none");
        var index = group.Items.Select(r => r.Id).ToList().IndexOf(created.Id);
        var section = view.GetVisualDescendants().OfType<TaskGroupList>().Single(s => ReferenceEquals(s.DataContext, group));
        var top = section.TranslatePoint(default, (Avalonia.Visual)view.Scroller.Content!)!.Value.Y;
        view.Scroller.Offset = new Avalonia.Vector(0, top + (index * 42) - 100);
        Settle(window);
        var row = Rows(view).SingleOrDefault(r => r.DataContext is ViewModels.TaskRowViewModel { Id: var id } && id == created.Id);
        Assert.NotNull(row);
        Assert.False(Animator.IsAnimating(row));
        Assert.Contains(vm.Left.Single(g => g.Key == "p-none").Items, r => r.Id == created.Id);
        window.Close();
    }

    [AvaloniaFact]
    public void Inbox_content_screenshot()
    {
        using var harness = new ScreenHarness();
        var (_, view) = Open(harness);
        var window = Host(view);
        var path = Screenshots.Capture(window, "inbox-content");
        Assert.True(new FileInfo(path).Length > 1000);
        window.Close();
    }
}
