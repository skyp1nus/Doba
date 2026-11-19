using Avalonia.Controls;
using Avalonia.VisualTree;
using Doba.App.Dialogs;
using Doba.App.Dialogs.Popovers;
using Doba.App.Tests.Support;
using Doba.Core.Demo;
using Doba.Core.Editing;
using Doba.Core.Model;

namespace Doba.App.Tests.Dialogs;

/// <summary>Opens the task dialog in a <see cref="ShellFixture"/> and finds its parts.</summary>
internal static class DialogKit
{
    public static readonly DateOnly Today = DemoData.Today;

    /// <summary>The prototype's demo modal (tools/design-render/jobs.js, `startModal`): Tomorrow, 14:00 · 1h, High, work.</summary>
    public static TaskDraft DemoDraft() =>
        TaskDialog.NewDraft(new DateOnly(2026, 11, 16), Today, Priority.High) with
        {
            Title = "Prepare slides for the Friday demo",
            Notes = "Outline: problem → live demo → next steps. Ask for feedback on the onboarding flow.",
            Timed = true,
            Time = new TimeOnly(14, 0),
            Tag = "work",
        };

    /// <summary>Opens a dialog on <paramref name="draft"/> the way the shell does and waits for focus to settle.</summary>
    public static Dialog Open(ShellFixture shell, TaskDraft draft)
    {
        var vm = new TaskDialogViewModel(shell.Vm.Screens, draft);
        shell.Vm.OpenOverlay(vm);
        shell.Settle();
        return new Dialog(shell, vm, Assert.IsType<TaskDialogView>(shell.Window.Overlays.OverlayView));
    }

    public static T Part<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"No {typeof(T).Name} named {name}.");

    internal sealed record Dialog(ShellFixture Shell, TaskDialogViewModel Vm, TaskDialogView View)
    {
        public PopoverAnchor DateAnchor => View.Anchors[0];

        public PopoverAnchor TimeAnchor => View.Anchors[1];

        public PopoverAnchor PriorityAnchor => View.Anchors[2];

        public PopoverAnchor TagAnchor => View.Anchors[3];

        public static Control Pill(PopoverAnchor anchor) => anchor.Children[0];

        public T Part<T>(string name)
            where T : Control => DialogKit.Part<T>(View, name);

        /// <summary>Clicks a pill (opens or closes its popover).</summary>
        public void ClickPill(PopoverAnchor anchor) => Shell.Click(Pill(anchor));

        /// <summary>The open popover's root (asserts that it is in the tree).</summary>
        public static Control OpenPopover(PopoverAnchor anchor)
        {
            Assert.True(anchor.IsShowing);
            return anchor.Popover!;
        }
    }
}
