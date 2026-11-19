using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Doba.App.Dialogs;
using Doba.App.Dialogs.Search;
using Doba.App.Shell;
using Doba.App.Tests.Support;
using Doba.Core.Demo;
using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Navigation;

namespace Doba.App.Tests.Shell;

/// <summary>Overlays: in-window host, Esc routing, the close path, focus return, and the N / Ctrl+K / Esc keys.</summary>
public class ShellOverlayTests
{
    private static readonly DateOnly Today = DemoData.Today;

    private sealed class FakeOverlay : OverlayViewModel
    {
        public bool HandlesEscape { get; set; }

        public int Escapes { get; private set; }

        public int Closed { get; private set; }

        public override bool OnEscape()
        {
            Escapes++;
            return HandlesEscape;
        }

        public Action? WhenClosed { get; init; }

        public override void OnClosed()
        {
            Closed++;
            WhenClosed?.Invoke();
        }

        public void Close() => RequestClose();
    }

    private sealed class HeldOverlayTransitions : IOverlayTransitions
    {
        public int Opens { get; private set; }

        public Action? Pending { get; private set; }

        public void Open(Control shade, Control panel) => Opens++;

        public void Close(Control shade, Control panel, Action done) => Pending = done;
    }

    [AvaloniaFact]
    public void Overlay_covers_the_window_and_hosts_the_panel_at_the_dialog_position()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var host = shell.Window.Overlays;
        Assert.False(host.IsVisible);

        var overlay = new FakeOverlay();
        shell.Vm.OpenOverlay(overlay);
        shell.Settle();
        Assert.True(host.IsVisible);
        Assert.Same(overlay, host.OverlayView!.DataContext);

        var shade = host.Shade.TranslatePoint(default, shell.Window)!.Value;
        Assert.Equal(new Point(0, 0), shade);                                   // over the title bar too
        Assert.Equal(new Size(1600, 1000), host.Shade.Bounds.Size);
        var panel = host.OverlayPanel.TranslatePoint(default, shell.Window)!.Value;
        Assert.Equal(150, panel.Y);
        Assert.Equal(640, host.OverlayPanel.Bounds.Width);
        Assert.Equal((1600 - 640) / 2.0, panel.X);
        Assert.Same(shell.Window, TopLevel.GetTopLevel(host.OverlayView));     // in the window, not a popup top-level
    }

    [AvaloniaFact]
    public void Esc_goes_to_the_overlay_first_and_closes_when_unhandled()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var overlay = new FakeOverlay { HandlesEscape = true };
        shell.Vm.OpenOverlay(overlay);
        shell.Settle();

        shell.Key(Key.Escape);
        Assert.Equal(1, overlay.Escapes);
        Assert.Same(overlay, shell.Vm.Overlay);
        Assert.Equal(0, overlay.Closed);

        overlay.HandlesEscape = false;
        shell.Key(Key.Escape);
        Assert.Equal(2, overlay.Escapes);
        Assert.Null(shell.Vm.Overlay);
        Assert.Equal(1, overlay.Closed);
        Assert.False(shell.Window.Overlays.IsVisible);

        shell.Key(Key.Escape);                                                  // nothing open: no-op
        Assert.Equal(2, overlay.Escapes);
        Assert.Equal(1, overlay.Closed);
    }

    [AvaloniaFact]
    public void Esc_already_handled_by_a_focused_control_does_not_reach_the_overlay()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var overlay = new FakeOverlay();
        shell.Vm.OpenOverlay(overlay);
        shell.Settle();

        // A control below the window (the sidebar's tag input, a text box) marked Esc as handled.
        shell.Window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Handled = true });
        Assert.Equal(0, overlay.Escapes);
        Assert.Same(overlay, shell.Vm.Overlay);

        shell.Window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Equal(1, overlay.Escapes);
    }

    [AvaloniaFact]
    public void Esc_in_the_sidebar_tag_input_only_cancels_the_input()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var sidebar = shell.Vm.Sidebar;
        var plus = shell.Window.AppSidebar.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddTagButton");
        var input = shell.Window.AppSidebar.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "TagInput");
        shell.Click(plus);
        Assert.True(sidebar.IsAddingTag);
        Assert.True(input.IsFocused);

        shell.Window.KeyTextInput("errands");
        shell.Key(Key.Escape);
        Assert.False(sidebar.IsAddingTag);
        Assert.DoesNotContain("errands", shell.App.Store.Tags);
        Assert.True(plus.IsFocused);
        Assert.Null(shell.Vm.Overlay);
        Assert.Equal(new NavState(AppView.Week, Today), shell.Vm.Nav);
    }

    [AvaloniaFact]
    public void Close_request_plays_the_exit_hook_before_CloseOverlay()
    {
        var held = new HeldOverlayTransitions();
        var previous = OverlayHost.OverlayTransitions;
        OverlayHost.OverlayTransitions = held;
        try
        {
            using var shell = ShellFixture.Open(AppView.Week);
            var overlay = new FakeOverlay();
            shell.Vm.OpenOverlay(overlay);
            Assert.Equal(1, held.Opens);

            overlay.Close();
            overlay.Close();                                                    // a second request while closing is ignored
            Assert.Same(overlay, shell.Vm.Overlay);                             // still on screen during the exit
            Assert.Equal(0, overlay.Closed);
            Assert.NotNull(held.Pending);

            held.Pending!();
            Assert.Null(shell.Vm.Overlay);
            Assert.Equal(1, overlay.Closed);
        }
        finally
        {
            OverlayHost.OverlayTransitions = previous;
        }
    }

    [AvaloniaFact]
    public void Disposing_the_shell_finishes_a_running_close_exactly_once_before_unsubscribing()
    {
        var held = new HeldOverlayTransitions();
        var previous = OverlayHost.OverlayTransitions;
        OverlayHost.OverlayTransitions = held;
        try
        {
            using var shell = ShellFixture.Open(AppView.Week);
            var overlay = new FakeOverlay();
            shell.Vm.OpenOverlay(overlay);
            overlay.Close();
            Assert.True(shell.Vm.IsOverlayClosing);

            shell.Vm.CompletePendingOverlayClose();                             // e.g. the window closing
            Assert.Null(shell.Vm.Overlay);
            Assert.False(shell.Vm.IsOverlayClosing);
            Assert.Equal(1, overlay.Closed);
            held.Pending!();                                                    // the exit finishing later changes nothing
            Assert.Equal(1, overlay.Closed);

            // Merely open: completing pending closes leaves it; disposing the shell closes it once.
            var open = new FakeOverlay();
            shell.Vm.OpenOverlay(open);
            shell.Vm.CompletePendingOverlayClose();
            Assert.Same(open, shell.Vm.Overlay);
            var inbox = shell.Vm.Sidebar.InboxCount;
            var closing = new FakeOverlay
            {
                // Writes like the task dialog's save: the shell must still be listening to the store.
                WhenClosed = () => shell.App.Store.Add(new TaskItem(0, "written on close", string.Empty, null, null, null, 0,
                    Priority.None, null, false)),
            };
            shell.Vm.OpenOverlay(closing);
            Assert.Equal(1, open.Closed);
            closing.Close();
            shell.Vm.Dispose();
            Assert.Equal(1, closing.Closed);
            Assert.Null(shell.Vm.Overlay);
            Assert.Equal(inbox + 1, shell.Vm.Sidebar.InboxCount);
            shell.Vm.Dispose();
            Assert.Equal(1, closing.Closed);
        }
        finally
        {
            OverlayHost.OverlayTransitions = previous;
        }
    }

    [AvaloniaFact]
    public void Opening_another_overlay_replaces_the_open_one()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var first = new FakeOverlay();
        var second = new FakeOverlay();
        shell.Vm.OpenOverlay(first);
        shell.Vm.OpenOverlay(second);
        Assert.Equal(1, first.Closed);
        Assert.Same(second, shell.Vm.Overlay);
        Assert.Same(second, shell.Window.Overlays.OverlayView!.DataContext);
        first.Close();                                                          // stale request: ignored
        Assert.Same(second, shell.Vm.Overlay);
        shell.Vm.CloseOverlay();
        Assert.Equal(1, second.Closed);
    }

    [AvaloniaFact]
    public void Focus_returns_to_the_element_that_had_it()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var today = shell.Window.Header.TodayButton;
        Assert.True(today.Focus());
        var overlay = new FakeOverlay();
        shell.Vm.OpenOverlay(overlay);
        shell.Settle();
        Assert.Same(shell.Window.Overlays.OverlayPanel, shell.Window.FocusManager!.GetFocusedElement());
        Assert.False(today.IsFocused);

        shell.Key(Key.Escape);
        Assert.True(today.IsFocused);
    }

    [AvaloniaFact]
    public void Focus_falls_back_to_New_task_when_the_element_is_gone()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var today = shell.Window.Header.TodayButton;
        Assert.True(today.Focus());
        shell.Vm.OpenOverlay(new FakeOverlay());
        shell.Vm.GoSection(AppView.Inbox);                                     // Today button is hidden outside the calendar
        shell.Settle();
        shell.Vm.CloseOverlay();
        shell.Settle();
        Assert.False(today.IsFocused);
        Assert.True(shell.Window.Header.NewTaskButton.IsFocused);
    }

    /// <summary>The title button of the first Week card that is on screen.</summary>
    private static Button FirstCardTitle(ShellFixture shell) =>
        shell.Window.ScreenHost.GetVisualDescendants().OfType<Doba.App.Controls.TaskCard>().First(c => c.IsEffectivelyVisible)
            .GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_TitleButton");

    private static bool FocusVisible(Control c) => c.Classes.Contains(":focus-visible");

    [AvaloniaFact]
    public void Deleting_the_task_the_dialog_was_opened_from_returns_focus_to_New_task()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var title = FirstCardTitle(shell);
        var row = Assert.IsType<Doba.App.ViewModels.TaskRowViewModel>(title.DataContext);
        Assert.True(title.Focus(NavigationMethod.Tab));                         // reached with the keyboard
        shell.Key(Key.Enter);
        var dialog = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        Assert.Equal(row.Id, dialog.Draft.EditId);

        var delete = shell.Window.Overlays.OverlayView!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Delete");
        shell.Click(delete);
        Assert.Null(shell.Vm.Overlay);
        Assert.Null(shell.App.Store.Get(row.Id));                               // the card (and the opener) are gone
        var newTask = shell.Window.Header.NewTaskButton;
        Assert.True(newTask.IsFocused);
        Assert.True(FocusVisible(newTask));                                     // opened from the keyboard: the ring shows
    }

    [AvaloniaFact]
    public void Saving_a_new_date_that_moves_the_card_returns_focus_to_New_task()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var title = FirstCardTitle(shell);
        var row = Assert.IsType<Doba.App.ViewModels.TaskRowViewModel>(title.DataContext);
        shell.Click(title);
        var dialog = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        var date = shell.App.Store.Get(row.Id)!.Date!.Value;
        var moved = date.DayOfWeek == DayOfWeek.Sunday ? date.AddDays(-1) : date.AddDays(1);
        dialog.Draft = TaskDialog.PickDate(dialog.Draft, moved);
        dialog.CommitCommand.Execute(null);
        shell.Settle();
        Assert.Null(shell.Vm.Overlay);
        Assert.Equal(moved, shell.App.Store.Get(row.Id)!.Date);
        Assert.False(title.IsFocused);
        var newTask = shell.Window.Header.NewTaskButton;
        Assert.True(newTask.IsFocused);
        Assert.False(FocusVisible(newTask));                                    // opened with the pointer: no ring
    }

    [AvaloniaFact]
    public void Focus_returns_with_a_visible_ring_to_an_opener_reached_with_the_keyboard()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var title = FirstCardTitle(shell);
        Assert.True(title.Focus(NavigationMethod.Tab));
        Assert.True(FocusVisible(title));
        shell.Key(Key.Enter);
        Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        shell.Key(Key.Escape);
        Assert.Null(shell.Vm.Overlay);
        Assert.True(title.IsFocused);
        Assert.True(FocusVisible(title));

        // Opened with the pointer: focus comes back without the ring.
        Assert.True(shell.Window.Header.TodayButton.Focus(NavigationMethod.Pointer));
        shell.Click(title);
        Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        shell.Key(Key.Escape);
        Assert.True(title.IsFocused);
        Assert.False(FocusVisible(title));
    }

    [AvaloniaFact]
    public void N_opens_the_new_task_dialog_for_the_current_view()
    {
        using var shell = ShellFixture.Open(AppView.Day, new DateOnly(2026, 11, 20),
            new AppSettings { DefaultPriority = Priority.High });
        shell.Key(Key.N);
        var dialog = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        Assert.Equal(TaskDialog.NewDraft(AppView.Day, new DateOnly(2026, 11, 20), Today, Priority.High), dialog.Draft);
        Assert.IsType<TaskDialogView>(shell.Window.Overlays.OverlayView);

        shell.Key(Key.N);                                                       // ignored while an overlay is open
        Assert.Same(dialog, shell.Vm.Overlay);
        shell.Key(Key.Escape);
        Assert.Null(shell.Vm.Overlay);
    }

    [AvaloniaFact]
    public void N_is_ignored_in_text_boxes_and_in_settings()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        Assert.False(shell.Window.Shortcuts!.Handle(Key.N, KeyModifiers.None, new TextBox()));
        Assert.Null(shell.Vm.Overlay);
        Assert.False(shell.Window.Shortcuts.Handle(Key.N, KeyModifiers.Shift, null));
        Assert.Null(shell.Vm.Overlay);

        shell.Navigate(AppView.Settings);
        shell.Key(Key.N);
        Assert.Null(shell.Vm.Overlay);
    }

    [AvaloniaFact]
    public void Ctrl_K_and_the_search_box_open_the_search_palette()
    {
        using var shell = ShellFixture.Open(AppView.Inbox);
        shell.Key(Key.K, RawInputModifiers.Control);
        Assert.IsType<SearchPaletteViewModel>(shell.Vm.Overlay);
        Assert.IsType<SearchPaletteView>(shell.Window.Overlays.OverlayView);
        shell.Key(Key.Escape);
        Assert.Null(shell.Vm.Overlay);

        shell.Key(Key.K);                                                       // plain K does nothing
        Assert.Null(shell.Vm.Overlay);

        shell.Click(shell.Window.TitleBar.SearchButton);
        Assert.IsType<SearchPaletteViewModel>(shell.Vm.Overlay);
    }

    [AvaloniaFact]
    public void App_commands_build_drafts_with_Core()
    {
        using var shell = ShellFixture.Open(AppView.Inbox);
        shell.Vm.NewTask();
        Assert.Equal(TaskDialog.NewDraft(AppView.Inbox, Today, Today, Priority.None),
            Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay).Draft);

        var date = new DateOnly(2026, 11, 18);
        shell.Vm.NewTask(date, hasExplicitDate: true);
        Assert.Equal(TaskDialog.NewDraft(date, Today, Priority.None), Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay).Draft);

        var task = shell.App.Store.All[0];
        shell.Vm.EditTask(task.Id);
        Assert.Equal(TaskDialog.EditDraft(task, Today), Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay).Draft);

        shell.Vm.CloseOverlay();
        shell.Vm.EditTask(-42);                                                 // unknown id: nothing opens
        Assert.Null(shell.Vm.Overlay);
    }
}
