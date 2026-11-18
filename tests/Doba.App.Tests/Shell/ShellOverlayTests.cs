using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

        public override void OnClosed() => Closed++;

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
