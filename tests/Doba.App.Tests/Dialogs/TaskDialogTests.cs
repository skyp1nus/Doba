using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Dialogs;
using Doba.App.Dialogs.Popovers;
using Doba.App.Shell;
using Doba.App.Tests.Controls;
using Doba.App.Tests.Support;
using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Time;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Dialogs;

/// <summary>
/// The New / Edit task dialog in the real window: headings and footer per mode, defaults through the shell, the
/// save/close sequencing (store written only in OnClosed, after the exit), Esc routing, the four popovers and every
/// action in them mapped to its Core rule, focus and keyboard, and the small-window rule for popovers.
/// </summary>
public class TaskDialogTests
{
    private static readonly DateOnly Today = DialogKit.Today;

    /// <summary>Holds the overlay exit so a test can look at the store before <c>OnClosed</c> runs.</summary>
    private sealed class HeldOverlayTransitions : IOverlayTransitions, IDisposable
    {
        private readonly IOverlayTransitions _previous = OverlayHost.OverlayTransitions;

        public HeldOverlayTransitions() => OverlayHost.OverlayTransitions = this;

        public Action? Pending { get; private set; }

        public void Open(Control shade, Control panel)
        {
        }

        public void Close(Control shade, Control panel, Action done) => Pending = done;

        public void Release()
        {
            var pending = Pending ?? throw new InvalidOperationException("No exit is running.");
            Pending = null;
            pending();
        }

        public void Dispose() => OverlayHost.OverlayTransitions = _previous;
    }

    // ---- Modes, labels, defaults -----------------------------------------------------------------------------------

    [AvaloniaFact]
    public void New_task_shows_its_heading_footer_and_pill_labels()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        shell.Vm.NewTask();
        var view = Assert.IsType<TaskDialogView>(shell.Window.Overlays.OverlayView);
        var vm = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        shell.Settle();

        Assert.Equal("NEW TASK", DialogKit.Part<TextBlock>(view, "PART_Heading").Text);
        Assert.Equal("Today", DialogKit.Part<TextBlock>(view, "PART_Where").Text);
        Assert.False(DialogKit.Part<Button>(view, "PART_Delete").IsVisible);
        Assert.Equal("↵ create · Esc close", DialogKit.Part<TextBlock>(view, "PART_Hint").Text);
        Assert.Equal("Create task", vm.CommitLabel);
        Assert.Contains(DialogKit.Part<Button>(view, "PART_Commit").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Create task");

        Assert.Equal(["Today", "Time", "Priority", "Tag"], new[] { vm.DateLabel, vm.TimeLabel, vm.PriorityLabel, vm.TagLabel });
        Assert.Contains(DialogKit.Dialog.Pill(view.Anchors[0]).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Today");
        var pills = view.Anchors.Select(a => (Pill)a.Children[0]).ToList();
        Assert.Equal([true, false, false, false], pills.Select(p => p.HasValue));
        Assert.All(pills, p => Assert.Equal(false, p.IsChecked));
        Assert.True(view.Anchors[1].IsVisible);                                   // dated single day: the Time pill exists
    }

    [AvaloniaFact]
    public void Edit_task_shows_Edit_heading_Save_and_Delete()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var task = shell.App.Store.All.First(t => t is { Date: not null, End: null, Time: not null, Tag: not null });
        shell.Vm.EditTask(task.Id);
        shell.Settle();
        var view = Assert.IsType<TaskDialogView>(shell.Window.Overlays.OverlayView);
        var vm = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);

        Assert.Equal("EDIT TASK", DialogKit.Part<TextBlock>(view, "PART_Heading").Text);
        Assert.Equal(TaskDialog.WhereLabel(vm.Draft, Today), DialogKit.Part<TextBlock>(view, "PART_Where").Text);
        Assert.True(DialogKit.Part<Button>(view, "PART_Delete").IsVisible);
        Assert.Equal("↵ save · Esc close", DialogKit.Part<TextBlock>(view, "PART_Hint").Text);
        Assert.Equal("Save", vm.CommitLabel);
        Assert.Equal(task.Title, view.TitleBox.Text);
        Assert.Equal(TaskDialog.TimePillLabel(vm.Draft), vm.TimeLabel);
        Assert.Equal(task.Tag, vm.TagLabel);
        Assert.True(((Pill)view.Anchors[1].Children[0]).HasValue);
        Assert.True(((Pill)view.Anchors[3].Children[0]).HasValue);
    }

    [AvaloniaTheory]
    [InlineData(AppView.Inbox, "Inbox")]
    [InlineData(AppView.Week, "Today")]
    [InlineData(AppView.Day, "Fri 20 Nov")]
    public void New_task_takes_its_date_from_the_view_and_its_priority_from_settings(AppView view, string where)
    {
        using var shell = ShellFixture.Open(view, new DateOnly(2026, 11, 20), new AppSettings { DefaultPriority = Priority.Medium });
        shell.Vm.NewTask();
        shell.Settle();
        var vm = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        var dialog = Assert.IsType<TaskDialogView>(shell.Window.Overlays.OverlayView);

        Assert.Equal(TaskDialog.NewDraft(view, new DateOnly(2026, 11, 20), Today, Priority.Medium), vm.Draft);
        Assert.Equal(where, DialogKit.Part<TextBlock>(dialog, "PART_Where").Text);
        Assert.Equal("Medium", vm.PriorityLabel);
        Assert.True(((Pill)dialog.Anchors[2].Children[0]).HasValue);
        Assert.Equal(view != AppView.Inbox, dialog.Anchors[1].IsVisible);        // undated: no Time pill
    }

    [AvaloniaFact]
    public void Focus_lands_in_the_title_on_open()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        shell.Key(Key.N);
        var view = Assert.IsType<TaskDialogView>(shell.Window.Overlays.OverlayView);
        Assert.True(view.TitleBox.IsFocused);
    }

    // ---- Save / close sequencing -----------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Create_looks_disabled_until_there_is_a_title_and_then_only_focuses_the_title()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, TaskDialog.NewDraft(Today, Today, Priority.None));
        var commit = dialog.Part<Button>("PART_Commit");
        Assert.Contains("inactive", commit.Classes);
        Assert.True(commit.IsEnabled);

        dialog.View.NotesBox.Focus();
        shell.Click(commit);
        Assert.Same(dialog.Vm, shell.Vm.Overlay);                                  // nothing happens …
        Assert.True(dialog.View.TitleBox.IsFocused);                               // … except focus goes to the title

        var count = shell.App.Store.All.Count;
        shell.Window.KeyTextInput("   ");
        Assert.Contains("inactive", commit.Classes);                          // a blank title does not count
        shell.Key(Key.Enter);
        Assert.Same(dialog.Vm, shell.Vm.Overlay);

        shell.Window.KeyTextInput("Water the plants");
        shell.Settle();
        Assert.DoesNotContain("inactive", commit.Classes);
        Assert.True(dialog.Vm.CanSave);
        Assert.Equal(count, shell.App.Store.All.Count);
    }

    [AvaloniaFact]
    public void Enter_saves_only_after_the_exit_and_marks_the_new_row()
    {
        using var held = new HeldOverlayTransitions();
        using var shell = ShellFixture.Open(AppView.Week);
        shell.Key(Key.N);
        var vm = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        var before = shell.App.Store.All.Count;

        shell.Window.KeyTextInput("Water the plants");
        shell.Key(Key.Enter);
        Assert.NotNull(held.Pending);                                              // closing …
        Assert.Same(vm, shell.Vm.Overlay);
        Assert.Equal(before, shell.App.Store.All.Count);                          // … and nothing written yet

        held.Release();
        Assert.Null(shell.Vm.Overlay);
        Assert.Equal(before + 1, shell.App.Store.All.Count);
        var created = shell.App.Store.All.Single(t => t.Title == "Water the plants");
        Assert.Equal(TaskDialog.ToTask(vm.Draft, null) with { Id = created.Id }, created);
        Assert.Equal(Today, created.Date);
        Assert.Equal(created.Id, shell.Vm.Screens.Actions.PendingEntranceId);
    }

    [AvaloniaFact]
    public void The_first_close_decision_wins_and_edits_during_the_exit_are_not_saved()
    {
        using var held = new HeldOverlayTransitions();
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, TaskDialog.NewDraft(Today, Today, Priority.None) with { Title = "Kept" });
        var before = shell.App.Store.All.Count;
        shell.Key(Key.Enter);
        dialog.View.TitleBox.Text = "Changed while closing";
        shell.Click(dialog.Part<Button>("PART_Cancel"));                           // too late to cancel
        held.Release();
        Assert.Equal(before + 1, shell.App.Store.All.Count);
        Assert.Contains(shell.App.Store.All, t => t.Title == "Kept");

        dialog = DialogKit.Open(shell, TaskDialog.NewDraft(Today, Today, Priority.None) with { Title = "Dropped" });
        shell.Click(dialog.Part<Button>("PART_Cancel"));
        shell.Key(Key.Enter);                                                      // Enter after Cancel: still cancelled
        held.Release();
        Assert.Null(shell.Vm.Overlay);
        Assert.DoesNotContain(shell.App.Store.All, t => t.Title == "Dropped");
        dialog.Vm.OnClosed();                                                      // a second OnClosed writes nothing
        Assert.Equal(before + 1, shell.App.Store.All.Count);
    }

    [AvaloniaFact]
    public void Closing_the_window_during_the_exit_still_saves_and_an_open_dialog_writes_nothing()
    {
        using var held = new HeldOverlayTransitions();
        var shell = ShellFixture.Open(AppView.Week);
        try
        {
            var before = shell.App.Store.All.ToList();
            var dialog = DialogKit.Open(shell, TaskDialog.NewDraft(Today, Today, Priority.None) with { Title = "Saved at close" });
            shell.Key(Key.Enter);
            Assert.NotNull(held.Pending);                                          // the exit is still running …
            Assert.Equal(before, shell.App.Store.All);

            shell.Window.Close();                                                  // … when the window goes away
            Assert.Null(shell.Vm.Overlay);
            Assert.Single(shell.App.Store.All, t => t.Title == "Saved at close");
            held.Release();                                                        // the late exit callback: no second save
            Assert.Equal(before.Count + 1, shell.App.Store.All.Count);
            dialog.Vm.OnClosed();
            Assert.Equal(before.Count + 1, shell.App.Store.All.Count);
        }
        finally
        {
            shell.Dispose();
        }

        // A dialog that is merely open (no Save) when the window closes is closed without writing anything.
        var second = ShellFixture.Open(AppView.Week);
        try
        {
            var before = second.App.Store.All.ToList();
            DialogKit.Open(second, TaskDialog.NewDraft(Today, Today, Priority.None) with { Title = "Never saved" });
            second.Window.Close();
            Assert.Null(second.Vm.Overlay);
            Assert.Equal(before, second.App.Store.All);
        }
        finally
        {
            second.Dispose();
        }
    }

    [AvaloniaFact]
    public void Save_keeps_a_wheel_change_that_has_not_settled_yet()
    {
        using var held = new HeldOverlayTransitions();
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        dialog.ClickPill(dialog.TimeAnchor);
        var popover = Assert.IsType<TimePopover>(DialogKit.Dialog.OpenPopover(dialog.TimeAnchor));
        var clock = new ManualClock();
        popover.HourWheel.SettleClock = clock;
        popover.MinuteWheel.SettleClock = clock;
        var before = dialog.Vm.Draft;

        Assert.True(popover.HourWheel.Focus(NavigationMethod.Tab));
        shell.Key(Key.Down);                                                       // 14 → 15, not still for 120 ms yet
        Assert.True(popover.HourWheel.CommitPending);
        Assert.Equal(before, dialog.Vm.Draft);
        dialog.Vm.CommitCommand.Execute(null);                                     // Enter / Create right away

        Assert.False(dialog.TimeAnchor.IsShowing);
        Assert.Equal(TaskDialog.WheelIndices(new TimeOnly(15, 0)).HourIndex, popover.HourWheel.SelectedIndex);
        Assert.Equal(TaskDialog.TimePillLabel(dialog.Vm.Draft), dialog.Vm.TimeLabel);
        Assert.Equal(TaskDialog.RangeLabel(dialog.Vm.Draft), dialog.Vm.RangeLabel);
        held.Release();
        var created = Assert.Single(shell.App.Store.All, t => t.Title == before.Title);
        Assert.Equal(new TimeOnly(15, 0), created.Time);
        Assert.Equal(TaskDialog.ToTask(TaskDialog.CommitWheel(before, hourColumn: true, 15), null) with { Id = created.Id }, created);
    }

    [AvaloniaFact]
    public void Primary_button_saves_the_same_way()
    {
        using var held = new HeldOverlayTransitions();
        using var shell = ShellFixture.Open(AppView.Inbox);
        var dialog = DialogKit.Open(shell, TaskDialog.NewDraft(null, Today, Priority.None) with { Title = "Read" });
        var before = shell.App.Store.All.Count;
        shell.Click(dialog.Part<Button>("PART_Commit"));
        Assert.Equal(before, shell.App.Store.All.Count);
        held.Release();
        Assert.Contains(shell.App.Store.All, t => t is { Title: "Read", Date: null });
    }

    [AvaloniaFact]
    public void Editing_keeps_the_id_and_the_done_state()
    {
        using var held = new HeldOverlayTransitions();
        using var shell = ShellFixture.Open(AppView.Week);
        var task = shell.App.Store.All.First(t => t.Done && t.Date is not null);
        shell.Vm.EditTask(task.Id);
        shell.Settle();
        var vm = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        var view = Assert.IsType<TaskDialogView>(shell.Window.Overlays.OverlayView);
        view.TitleBox.Text = "Renamed";
        shell.Settle();
        Assert.Equal("Renamed", vm.Draft.Title);

        shell.Key(Key.Enter);
        Assert.Equal(task, shell.App.Store.Get(task.Id));                          // untouched until the exit ends
        held.Release();
        var saved = shell.App.Store.Get(task.Id)!;
        Assert.Equal((task.Id, "Renamed", true), (saved.Id, saved.Title, saved.Done));
        Assert.Equal(TaskDialog.ToTask(TaskDialog.EditDraft(task, Today) with { Title = "Renamed" }, task), saved);
        Assert.NotEqual(task.Id, shell.Vm.Screens.Actions.PendingEntranceId);     // edits do not replay the new-row entrance
    }

    [AvaloniaFact]
    public void Delete_removes_the_task_after_the_exit()
    {
        using var held = new HeldOverlayTransitions();
        using var shell = ShellFixture.Open(AppView.Week);
        var task = shell.App.Store.All[3];
        shell.Vm.EditTask(task.Id);
        shell.Settle();
        var view = Assert.IsType<TaskDialogView>(shell.Window.Overlays.OverlayView);
        shell.Click(DialogKit.Part<Button>(view, "PART_Delete"));
        Assert.NotNull(shell.App.Store.Get(task.Id));
        held.Release();
        Assert.Null(shell.App.Store.Get(task.Id));
        Assert.Null(shell.Vm.Overlay);
    }

    [AvaloniaFact]
    public void Cancel_close_and_Esc_close_without_saving()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var before = shell.App.Store.All.ToList();
        foreach (var how in new[] { "PART_Cancel", "PART_Close", "Esc" })
        {
            var dialog = DialogKit.Open(shell, TaskDialog.NewDraft(Today, Today, Priority.None) with { Title = "Not saved" });
            if (how == "Esc")
            {
                shell.Key(Key.Escape);
            }
            else
            {
                shell.Click(dialog.Part<Button>(how));
            }

            Assert.Null(shell.Vm.Overlay);
            Assert.Equal(before, shell.App.Store.All);
        }
    }

    [AvaloniaFact]
    public void Enter_in_the_notes_inserts_a_new_line()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, TaskDialog.NewDraft(Today, Today, Priority.None) with { Title = "Plan" });
        dialog.View.NotesBox.Focus();
        shell.Window.KeyTextInput("a");
        shell.Key(Key.Enter);
        shell.Window.KeyTextInput("b");
        shell.Settle();
        Assert.Same(dialog.Vm, shell.Vm.Overlay);
        Assert.Equal(2, dialog.Vm.Draft.Notes.Split('\n').Length);
        Assert.StartsWith("a", dialog.Vm.Draft.Notes, StringComparison.Ordinal);
        Assert.EndsWith("b", dialog.Vm.Draft.Notes, StringComparison.Ordinal);
    }

    // ---- Popovers: open / close ----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Each_pill_opens_its_popover_and_another_pill_closes_it()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        var anchors = dialog.View.Anchors;
        var kinds = new[] { typeof(DatePopover), typeof(TimePopover), typeof(PriorityPopover), typeof(TagPopover) };
        var opens = new[] { DraftPopover.Date, DraftPopover.Time, DraftPopover.Priority, DraftPopover.Tag };
        for (var i = 0; i < anchors.Count; i++)
        {
            dialog.ClickPill(anchors[i]);
            Assert.Equal(opens[i], dialog.Vm.Draft.Open);
            Assert.IsType(kinds[i], DialogKit.Dialog.OpenPopover(anchors[i]));
            Assert.Equal(true, ((Pill)anchors[i].Children[0]).IsChecked);
            for (var j = 0; j < anchors.Count; j++)
            {
                if (j != i)
                {
                    Assert.False(anchors[j].IsShowing);                            // at most one is open
                    Assert.Equal(false, ((Pill)anchors[j].Children[0]).IsChecked);
                }
            }
        }

        dialog.ClickPill(anchors[3]);                                              // the open pill again: closes
        Assert.Equal(DraftPopover.None, dialog.Vm.Draft.Open);
        Assert.All(anchors, a => Assert.False(a.IsShowing));
    }

    [AvaloniaFact]
    public void Esc_closes_the_open_popover_first_and_the_dialog_second()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        dialog.ClickPill(dialog.PriorityAnchor);
        var item = DialogKit.Dialog.OpenPopover(dialog.PriorityAnchor).GetVisualDescendants().OfType<Button>().First();
        Assert.True(item.Focus(NavigationMethod.Tab));

        shell.Key(Key.Escape);
        Assert.Same(dialog.Vm, shell.Vm.Overlay);
        Assert.False(dialog.PriorityAnchor.IsShowing);
        Assert.True(DialogKit.Dialog.Pill(dialog.PriorityAnchor).IsFocused);      // focus returns to the pill

        shell.Key(Key.Escape);
        Assert.Null(shell.Vm.Overlay);
    }

    [AvaloniaFact]
    public void Focusing_the_title_or_the_notes_closes_the_popover()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        dialog.ClickPill(dialog.DateAnchor);
        shell.Click(dialog.View.NotesBox);
        Assert.False(dialog.DateAnchor.IsShowing);

        dialog.ClickPill(dialog.TagAnchor);
        dialog.View.TitleBox.Focus();
        shell.Settle();
        Assert.Equal(DraftPopover.None, dialog.Vm.Draft.Open);
        Assert.False(dialog.TagAnchor.IsShowing);
    }

    [AvaloniaFact]
    public void Popover_sits_under_its_pill_above_the_footer_and_plays_its_entrance()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        AppMotion.SystemAnimationsEnabled = true;                                  // the fixture restores it
        dialog.Vm.TogglePopoverCommand.Execute(DraftPopover.Time);
        var popover = DialogKit.Dialog.OpenPopover(dialog.TimeAnchor);
        var running = Doba.App.Motion.Animator.Running(popover);
        Assert.NotNull(running);
        Assert.Equal(new Doba.App.Motion.MotionState(0, 0, AppMotion.PopY, AppMotion.PopScale, AppMotion.PopScale), running.From);
        Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.Pop), running.Duration);
        running.Complete();
        AppMotion.SystemAnimationsEnabled = false;
        shell.Settle();

        var pill = DialogKit.Dialog.Pill(dialog.TimeAnchor);
        var pillAt = pill.TranslatePoint(default, shell.Window)!.Value;
        var popAt = popover.TranslatePoint(default, shell.Window)!.Value;
        Assert.Equal(pillAt.X, popAt.X);
        Assert.Equal(pillAt.Y + 36, popAt.Y);
        Assert.Equal(248, popover.Bounds.Width);

        // Drawn over the footer: hit testing at a point of the popover inside the footer finds the popover.
        var footerPoint = new Point(popAt.X + 20, shell.Window.Overlays.OverlayPanel.TranslatePoint(new Point(0, 260), shell.Window)!.Value.Y);
        var hit = shell.Window.InputHitTest(footerPoint) as Visual;
        Assert.NotNull(hit);
        Assert.True(ReferenceEquals(hit, popover) || popover.IsVisualAncestorOf(hit));

        // And below the dialog panel it is still hit-testable (not clipped).
        var below = new Point(popAt.X + 120, popAt.Y + popover.Bounds.Height - 20);
        Assert.True(below.Y > shell.Window.Overlays.OverlayPanel.TranslatePoint(new Point(0, shell.Window.Overlays.OverlayPanel.Bounds.Height), shell.Window)!.Value.Y);
        var hitBelow = shell.Window.InputHitTest(below) as Visual;
        Assert.NotNull(hitBelow);
        Assert.True(popover.IsVisualAncestorOf(hitBelow));
    }

    [AvaloniaFact]
    public void Popovers_stay_inside_a_small_window()
    {
        using var shell = ShellFixture.Open(AppView.Week, width: 1100, height: 700);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        foreach (var anchor in dialog.View.Anchors)
        {
            dialog.ClickPill(anchor);
            var popover = DialogKit.Dialog.OpenPopover(anchor);
            var at = popover.TranslatePoint(default, shell.Window)!.Value;
            var rect = new Rect(at, popover.Bounds.Size);
            Assert.True(new Rect(0, 0, 1100, 700).Deflate(8).Contains(rect), $"{popover.GetType().Name} at {rect}");
            shell.Key(Key.Escape);                                                 // (the moved-up Date popover covers its pill)
            Assert.False(anchor.IsShowing);
        }

        // The tall Date popover is moved up by its overflow (the pills sit at 319 px; it would end at ~760 px).
        dialog.ClickPill(dialog.DateAnchor);
        var date = DialogKit.Dialog.OpenPopover(dialog.DateAnchor);
        Assert.True(dialog.DateAnchor.Shift.Y < 0);
        Assert.Equal(0, dialog.DateAnchor.Shift.X);
        Assert.Equal(700 - 8, date.TranslatePoint(new Point(0, date.Bounds.Height), shell.Window)!.Value.Y, 0.5);

        // Its days are still clickable there.
        var day = Days(date).First(d => ((CalendarDayItem)d.DataContext!).Date == new DateOnly(2026, 11, 25));
        shell.Click(day);
        Assert.Equal(new DateOnly(2026, 11, 25), dialog.Vm.Draft.Date);
    }

    [AvaloniaFact]
    public void Tab_goes_from_the_open_pill_into_its_popover_and_on_to_the_next_pill()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        dialog.ClickPill(dialog.PriorityAnchor);
        var pill = DialogKit.Dialog.Pill(dialog.PriorityAnchor);
        Assert.True(pill.Focus(NavigationMethod.Tab));
        var rows = DialogKit.Dialog.OpenPopover(dialog.PriorityAnchor).GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(5, rows.Count);

        shell.Key(Key.Tab);
        Assert.True(rows[0].IsFocused);
        for (var i = 1; i < rows.Count; i++)
        {
            shell.Key(Key.Tab);
            Assert.True(rows[i].IsFocused);
        }

        shell.Key(Key.Tab);
        Assert.True(DialogKit.Dialog.Pill(dialog.TagAnchor).IsFocused);

        shell.Key(Key.Tab, RawInputModifiers.Shift);
        Assert.True(rows[^1].IsFocused);

        shell.Key(Key.Enter);                                                      // Enter on a focused item picks it
        Assert.Equal(Priority.Asap, dialog.Vm.Draft.Priority);
        Assert.False(dialog.PriorityAnchor.IsShowing);
        Assert.True(pill.IsFocused);
    }

    // ---- Popover actions → Core ----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Date_popover_actions_map_to_Core()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        dialog.ClickPill(dialog.DateAnchor);
        var popover = DialogKit.Dialog.OpenPopover(dialog.DateAnchor);
        var vm = dialog.Vm;

        Assert.Equal("November 2026", DialogKit.Part<TextBlock>(popover, "PART_Month").Text);
        Assert.Equal(["M", "T", "W", "T", "F", "S", "S"], popover.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("weekday")).Select(t => t.Text));
        var cells = Days(popover);
        Assert.Equal(42, cells.Count);
        var expected = TaskDialog.MiniCalendar(vm.Draft, Today, DayOfWeek.Monday);
        Assert.Equal(expected.Select(c => c.DayNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            cells.Select(c => c.Content?.ToString()));
        Assert.Contains("marked", cells[21].Classes);                         // 16 Nov
        Assert.Contains("today", cells[20].Classes);                          // 15 Nov
        Assert.Contains("outside", cells[0].Classes);
        Assert.Contains("past", cells[10].Classes);

        // ‹ › and the calendar's Today keep the popover open.
        var before = vm.Draft;
        shell.Click(DialogKit.Part<Button>(popover, "PART_NextMonth"));
        Assert.Equal(TaskDialog.ShiftCalendarMonth(before, 1), vm.Draft);
        Assert.Equal("December 2026", DialogKit.Part<TextBlock>(popover, "PART_Month").Text);
        Assert.Same(cells[0], Days(popover)[0]);                                  // cells updated in place
        shell.Click(DialogKit.Part<Button>(popover, "PART_PreviousMonth"));
        shell.Click(DialogKit.Part<Button>(popover, "PART_PreviousMonth"));
        Assert.Equal("October 2026", vm.CalendarLabel);
        shell.Click(DialogKit.Part<Button>(popover, "PART_CalendarToday"));
        Assert.Equal(new DateOnly(2026, 11, 1), vm.Draft.CalendarMonth);
        Assert.True(dialog.DateAnchor.IsShowing);

        // "Lasts" 1..14 with clamping; the range band and the until-label follow.
        before = vm.Draft;
        shell.Click(DialogKit.Part<Button>(popover, "PART_MoreDays"));
        Assert.Equal(TaskDialog.StepDays(before, 1), vm.Draft);
        Assert.Equal("2 days", DialogKit.Part<TextBlock>(popover, "PART_DaysLabel").Text);
        Assert.Equal("until 17 Nov", DialogKit.Part<TextBlock>(popover, "PART_Until").Text);
        Assert.Equal("Tomorrow → 17 Nov", vm.DateLabel);
        Assert.False(dialog.TimeAnchor.IsVisible);                                 // multi-day: no Time pill
        Assert.Contains("band", cells[22].Classes);
        Assert.Contains("end", cells[22].Classes);
        for (var i = 0; i < 20; i++)
        {
            shell.Click(DialogKit.Part<Button>(popover, "PART_MoreDays"));
        }

        Assert.Equal(TaskDialog.MaxDays, vm.Draft.Days);
        for (var i = 0; i < 20; i++)
        {
            shell.Click(DialogKit.Part<Button>(popover, "PART_FewerDays"));
        }

        Assert.Equal(1, vm.Draft.Days);
        Assert.True(dialog.TimeAnchor.IsVisible);
        Assert.True(dialog.DateAnchor.IsShowing);

        // A day closes the popover.
        before = vm.Draft;
        shell.Click(cells[30]);
        Assert.Equal(TaskDialog.PickDate(before, expected[30].Date), vm.Draft);
        Assert.False(dialog.DateAnchor.IsShowing);

        // Quick options close it too; "No date" also hides Time and "Lasts".
        foreach (var (label, date) in new (string, DateOnly?)[] { ("Next week", new DateOnly(2026, 11, 16)), ("Tomorrow", new DateOnly(2026, 11, 16)), ("Today", Today), ("No date", null) })
        {
            dialog.ClickPill(dialog.DateAnchor);
            popover = DialogKit.Dialog.OpenPopover(dialog.DateAnchor);
            before = vm.Draft;
            var chip = popover.GetVisualDescendants().OfType<ToggleButton>().Single(b => ((DateChipItem)b.DataContext!).Label == label);
            shell.Click(chip);
            Assert.Equal(TaskDialog.PickDate(before, date), vm.Draft);
            Assert.False(dialog.DateAnchor.IsShowing);
        }

        Assert.Null(vm.Draft.Date);
        Assert.Equal("Date", vm.DateLabel);
        Assert.Equal("Inbox", vm.WhereLabel);
        Assert.False(dialog.TimeAnchor.IsVisible);
        dialog.ClickPill(dialog.DateAnchor);
        Assert.False(DialogKit.Part<StackPanel>(DialogKit.Dialog.OpenPopover(dialog.DateAnchor), "PART_Lasts").IsVisible);
    }

    [AvaloniaFact]
    public void Date_popover_follows_a_Sunday_week_start()
    {
        using var shell = ShellFixture.Open(AppView.Week, settings: new AppSettings { WeekStart = DayOfWeek.Sunday });
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        dialog.ClickPill(dialog.DateAnchor);
        var popover = DialogKit.Dialog.OpenPopover(dialog.DateAnchor);
        Assert.Equal(["S", "M", "T", "W", "T", "F", "S"], popover.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("weekday")).Select(t => t.Text));
        Assert.Equal([true, false, false, false, false, false, true], popover.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("weekday")).Select(t => t.Classes.Contains("weekend")));
        Assert.Equal(TaskDialog.MiniCalendar(dialog.Vm.Draft, Today, DayOfWeek.Sunday).Select(c => c.Date),
            Days(popover).Select(d => ((CalendarDayItem)d.DataContext!).Date));
        var next = TaskDialog.NextWeek(Today, DayOfWeek.Sunday);
        Assert.Equal(DateMath.DayMonth(next), dialog.Vm.DateOptions[2].Hint);
    }

    [AvaloniaFact]
    public void Time_popover_actions_map_to_Core()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft() with { Time = new TimeOnly(9, 35), DurationMinutes = 45 });
        dialog.ClickPill(dialog.TimeAnchor);
        var popover = Assert.IsType<TimePopover>(DialogKit.Dialog.OpenPopover(dialog.TimeAnchor));
        var vm = dialog.Vm;

        // On open both wheels sit on the draft's time (WheelIndices), without committing anything.
        Assert.Equal(TaskDialog.WheelIndices(new TimeOnly(9, 35)), (popover.HourWheel.SelectedIndex, popover.MinuteWheel.SelectedIndex));
        Assert.Equal(new TimeOnly(9, 35), vm.Draft.Time);
        Assert.Equal(-1, DialogKit.Part<ListBox>(popover, "PART_Duration").SelectedIndex);   // 45 min is not a chip
        Assert.Equal("09:35 – 10:20", DialogKit.Part<TextBlock>(popover, "PART_Range").Text);

        // Any time / Set time.
        var mode = DialogKit.Part<ListBox>(popover, "PART_Mode");
        shell.Click(mode.ContainerFromIndex(0)!);
        Assert.False(vm.Draft.Timed);
        Assert.Equal("Time", vm.TimeLabel);
        Assert.True(DialogKit.Part<TextBlock>(popover, "PART_Untimed").IsVisible);
        Assert.False(DialogKit.Part<StackPanel>(popover, "PART_Timed").IsVisible);
        shell.Click(mode.ContainerFromIndex(1)!);
        Assert.True(vm.Draft.Timed);
        Assert.True(dialog.TimeAnchor.IsShowing);

        // Duration: picks the minutes (and Set time).
        vm.TimeModeIndex = 0;
        shell.Settle();
        vm.TimeModeIndex = 1;
        shell.Settle();
        var durations = DialogKit.Part<ListBox>(popover, "PART_Duration");
        shell.Click(durations.ContainerFromIndex(3)!);
        Assert.Equal((120, true), (vm.Draft.DurationMinutes, vm.Draft.Timed));
        Assert.Equal("09:35 · 2h", vm.TimeLabel);

        // A settled wheel commits through CommitWheel (and switches on Set time).
        vm.TimeModeIndex = 0;
        shell.Settle();
        vm.TimeModeIndex = 1;
        shell.Settle();
        var clock = new ManualClock();
        popover.HourWheel.SettleClock = clock;
        popover.MinuteWheel.SettleClock = clock;
        var before = vm.Draft;
        Assert.True(popover.HourWheel.Focus(NavigationMethod.Tab));
        shell.Key(Key.Down);
        shell.Key(Key.Down);
        Assert.Equal(before, vm.Draft);                                            // nothing while it moves
        clock.Advance(TimeSpan.FromMilliseconds(AppMotion.WheelSettle));
        shell.Settle();
        Assert.Equal(TaskDialog.CommitWheel(before, hourColumn: true, 11), vm.Draft);

        // Done closes it; a wheel commit still pending is flushed when the popover leaves the tree.
        before = vm.Draft;
        Assert.True(popover.MinuteWheel.Focus(NavigationMethod.Tab));
        shell.Key(Key.Up);
        Assert.True(popover.MinuteWheel.CommitPending);
        shell.Click(DialogKit.Part<Button>(popover, "PART_Done"));
        Assert.False(dialog.TimeAnchor.IsShowing);
        Assert.Equal(TaskDialog.CommitWheel(before, hourColumn: false, 6) with { Open = DraftPopover.None }, vm.Draft);
    }

    [AvaloniaFact]
    public void Scrolling_a_wheel_switches_on_Set_time()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, TaskDialog.NewDraft(Today, Today, Priority.None));
        dialog.Vm.TogglePopoverCommand.Execute(DraftPopover.Time);
        shell.Settle();
        var popover = Assert.IsType<TimePopover>(DialogKit.Dialog.OpenPopover(dialog.TimeAnchor));
        Assert.False(dialog.Vm.Draft.Timed);
        Assert.Equal(TaskDialog.WheelIndices(TaskDialog.DefaultTime), (popover.HourWheel.SelectedIndex, popover.MinuteWheel.SelectedIndex));

        // A wheel is visible only with a set time; the commit itself is what CommitWheel does.
        dialog.Vm.CommitMinute(3);
        Assert.Equal((true, new TimeOnly(10, 15)), (dialog.Vm.Draft.Timed, dialog.Vm.Draft.Time));
        Assert.Equal("10:15 · 1h", dialog.Vm.TimeLabel);
        Assert.True(((Pill)dialog.TimeAnchor.Children[0]).HasValue);
    }

    [AvaloniaFact]
    public void Priority_and_tag_popovers_pick_and_close()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        var vm = dialog.Vm;

        dialog.ClickPill(dialog.PriorityAnchor);
        var rows = DialogKit.Dialog.OpenPopover(dialog.PriorityAnchor).GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(["No priority", "Low", "Medium", "High", "ASAP"], rows.Select(r => ((PriorityOptionItem)r.DataContext!).Label));
        Assert.Equal([false, false, false, true, false], rows.Select(r => r.Classes.Contains("selected")));
        Assert.Equal([Priority.None, Priority.Low, Priority.Medium, Priority.High, Priority.Asap],
            rows.Select(r => r.GetVisualDescendants().OfType<PriorityIcon>().Single().Priority));
        shell.Click(rows[4]);
        Assert.Equal((Priority.Asap, DraftPopover.None), (vm.Draft.Priority, vm.Draft.Open));
        Assert.Equal("ASAP", vm.PriorityLabel);
        dialog.ClickPill(dialog.PriorityAnchor);
        shell.Click(DialogKit.Dialog.OpenPopover(dialog.PriorityAnchor).GetVisualDescendants().OfType<Button>().First());
        Assert.Equal(Priority.None, vm.Draft.Priority);
        Assert.Equal("Priority", vm.PriorityLabel);
        Assert.False(((Pill)dialog.PriorityAnchor.Children[0]).HasValue);

        shell.App.Store.AddTag("errands");
        dialog.ClickPill(dialog.TagAnchor);
        var tags = DialogKit.Dialog.OpenPopover(dialog.TagAnchor).GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(["No tag", "work", "personal", "learning", "errands"], tags.Select(r => ((TagOptionItem)r.DataContext!).Label));
        Assert.Equal(["–", "#", "#", "#", "#"], tags.Select(r => ((TagOptionItem)r.DataContext!).Symbol));
        Assert.Equal([false, true, false, false, false], tags.Select(r => r.Classes.Contains("selected")));
        shell.Click(tags[3]);
        Assert.Equal(("learning", DraftPopover.None), (vm.Draft.Tag, vm.Draft.Open));
        dialog.ClickPill(dialog.TagAnchor);
        shell.Click(DialogKit.Dialog.OpenPopover(dialog.TagAnchor).GetVisualDescendants().OfType<Button>().First());
        Assert.Null(vm.Draft.Tag);
        Assert.Equal("Tag", vm.TagLabel);
    }

    [AvaloniaFact]
    public void Time_pill_exists_only_for_dated_single_day_drafts()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        Assert.True(dialog.TimeAnchor.IsVisible);
        dialog.Vm.Draft = dialog.Vm.Draft with { Days = 3 };
        shell.Settle();
        Assert.False(dialog.TimeAnchor.IsVisible);
        dialog.Vm.Draft = dialog.Vm.Draft with { Days = 1, Date = null };
        shell.Settle();
        Assert.False(dialog.TimeAnchor.IsVisible);
        dialog.Vm.Draft = dialog.Vm.Draft with { Date = Today };
        shell.Settle();
        Assert.True(dialog.TimeAnchor.IsVisible);
    }

    private static List<Button> Days(Control popover) =>
        [.. popover.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is CalendarDayItem)];
}
