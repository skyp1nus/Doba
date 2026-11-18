using Avalonia.Headless.XUnit;
using Doba.App.Tests.Support;
using Doba.Core.Navigation;

namespace Doba.App.Tests.Dialogs;

/// <summary>
/// Full-window screenshots of the task dialog over the Week view at 1600×1000 with the prototype's demo modal state
/// (Tomorrow, 14:00 · 1h, High, work), one per reference image: docs/design/screens/dialog-date / -time / -priority /
/// -tag.png, each with that popover open (opened by clicking its pill, as tools/design-render/jobs.js does).
/// </summary>
public class TaskDialogScreenshotTests
{
    public static TheoryData<string, int> Shots => new()
    {
        { "dialog-date", 0 },
        { "dialog-time", 1 },
        { "dialog-priority", 2 },
        { "dialog-tag", 3 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Shots))]
    public void Dialog_renders_with_a_popover_open(string name, int pill)
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        dialog.ClickPill(dialog.View.Anchors[pill]);
        Assert.True(dialog.View.Anchors[pill].IsShowing);
        var path = shell.Capture(name);
        Assert.True(new FileInfo(path).Length > 10_000, path);
    }

    /// <summary>States without a reference image (for review): edit mode with a three-day span (band, until-label,
    /// Delete) and the Time popover in "Any time" mode.</summary>
    [AvaloniaFact]
    public void Edit_dialog_with_a_span_and_an_untimed_time_popover_render()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var span = shell.App.Store.All.First(t => t is { End: not null, Done: false });
        shell.Vm.EditTask(span.Id);
        shell.Settle();
        var vm = Assert.IsType<Doba.App.Dialogs.TaskDialogViewModel>(shell.Vm.Overlay);
        vm.TogglePopoverCommand.Execute(Doba.Core.Editing.DraftPopover.Date);
        Assert.True(new FileInfo(shell.Capture("dialog-edit-span")).Length > 10_000);

        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft() with { Timed = false });
        dialog.ClickPill(dialog.TimeAnchor);
        Assert.True(new FileInfo(shell.Capture("dialog-time-anytime")).Length > 10_000);
    }
}
