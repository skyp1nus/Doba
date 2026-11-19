using Avalonia.Headless.XUnit;
using Doba.App.Dialogs;
using Doba.App.Tests.Lifecycle;
using Doba.Core.Model;
using Doba.Core.Time;

namespace Doba.App.Tests.Dialogs;

/// <summary>A task dialog left open across midnight: a save never writes back the date carry-over has just moved, a new
/// task still on its default date follows today, and the labels follow the new day.</summary>
public class TaskDialogMidnightTests
{
    private static readonly DateOnly Day = new(2026, 11, 15);

    private static FixedTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 15, 23, 50, 0, TimeSpan.Zero));

    private static TaskItem Task(string title, DateOnly? date) =>
        new(0, title, string.Empty, date, null, null, 0, Priority.None, null, false);

    private static void Midnight(LifecycleHarness h, FixedTimeProvider clock)
    {
        clock.Advance(TimeSpan.FromMinutes(20));
        h.App.DayClock.Check();
        h.Settle();
        Assert.Equal(Day.AddDays(1), h.App.DayClock.Today);
    }

    private static void Save(LifecycleHarness h, TaskDialogViewModel dialog)
    {
        dialog.CommitCommand.Execute(null);
        h.Vm.CompletePendingOverlayClose();                                       // the exit, finished at once
        h.Settle();
        Assert.Null(h.Vm.Overlay);
    }

    [AvaloniaFact]
    public void Saving_an_edit_keeps_the_date_carry_over_moved_while_the_dialog_was_open()
    {
        using var folder = new TempFolder();
        var clock = Clock();
        using var h = LifecycleHarness.Disk(folder, clock, settings: s => s with { CarryOver = true, DailyBackup = false });
        var task = h.App.Store.Add(Task("left open", Day));
        h.Settle();
        h.Vm.EditTask(task.Id);
        h.Settle();
        var dialog = Assert.IsType<TaskDialogViewModel>(h.Vm.Overlay);
        dialog.Title = "left open, renamed";

        Midnight(h, clock);
        Assert.Equal(Day.AddDays(1), h.App.Store.Get(task.Id)!.Date);             // carried over meanwhile

        Save(h, dialog);
        var saved = h.App.Store.Get(task.Id)!;
        Assert.Equal("left open, renamed", saved.Title);
        Assert.Equal(Day.AddDays(1), saved.Date);                                  // not written back to yesterday
    }

    [AvaloniaFact]
    public void A_date_picked_in_the_edit_dialog_still_wins_after_midnight()
    {
        using var folder = new TempFolder();
        var clock = Clock();
        using var h = LifecycleHarness.Disk(folder, clock, settings: s => s with { CarryOver = true, DailyBackup = false });
        var task = h.App.Store.Add(Task("moved by hand", Day));
        h.Settle();
        h.Vm.EditTask(task.Id);
        h.Settle();
        var dialog = Assert.IsType<TaskDialogViewModel>(h.Vm.Overlay);
        var picked = Day.AddDays(3);
        dialog.Draft = Doba.Core.Editing.TaskDialog.PickDate(dialog.Draft, picked);

        Midnight(h, clock);
        Save(h, dialog);
        Assert.Equal(picked, h.App.Store.Get(task.Id)!.Date);
    }

    [AvaloniaFact]
    public void A_new_task_on_its_default_today_moves_to_the_new_today_and_the_labels_follow()
    {
        using var folder = new TempFolder();
        var clock = Clock();
        using var h = LifecycleHarness.Disk(folder, clock, settings: s => s with { CarryOver = true, DailyBackup = false });
        h.Settle();
        h.Vm.NewTask();
        h.Settle();
        var dialog = Assert.IsType<TaskDialogViewModel>(h.Vm.Overlay);
        Assert.Equal(Day, dialog.Draft.Date);
        Assert.Equal("Today", dialog.DateLabel);
        dialog.Title = "drafted before midnight";

        Midnight(h, clock);
        Assert.Equal(Day.AddDays(1), dialog.Draft.Date);
        Assert.Equal("Today", dialog.DateLabel);
        Assert.Equal("Today", dialog.WhereLabel);
        Assert.Contains(dialog.CalendarDays, d => d.Date == Day.AddDays(1) && d.IsToday);

        Save(h, dialog);
        Assert.Contains(h.App.Store.All, t => t.Title == "drafted before midnight" && t.Date == Day.AddDays(1));
    }

    [AvaloniaFact]
    public void A_new_task_for_tomorrow_keeps_its_date_and_its_label_becomes_Today()
    {
        using var folder = new TempFolder();
        var clock = Clock();
        using var h = LifecycleHarness.Disk(folder, clock, settings: s => s with { CarryOver = true, DailyBackup = false });
        h.Settle();
        h.Vm.NewTask();
        h.Settle();
        var dialog = Assert.IsType<TaskDialogViewModel>(h.Vm.Overlay);
        dialog.Draft = Doba.Core.Editing.TaskDialog.PickDate(dialog.Draft, Day.AddDays(1));
        Assert.Equal("Tomorrow", dialog.DateLabel);

        Midnight(h, clock);
        Assert.Equal(Day.AddDays(1), dialog.Draft.Date);
        Assert.Equal("Today", dialog.DateLabel);
    }
}
