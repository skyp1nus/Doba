using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Screens.Inbox;
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
