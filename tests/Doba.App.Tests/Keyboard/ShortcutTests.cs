using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Dialogs;
using Doba.App.Dialogs.Search;
using Doba.App.Screens.Day;
using Doba.App.Shell;
using Doba.App.Tests.Dialogs;
using Doba.App.Tests.Support;
using Doba.App.ViewModels;
using Doba.Core.Demo;
using Doba.Core.Navigation;
using static Doba.App.Tests.Keyboard.KeyboardKit;

namespace Doba.App.Tests.Keyboard;

/// <summary>
/// The window shortcuts (KICKOFF §6, Settings › Shortcuts) through real headless key events on the full shell: where
/// each one works, and that it is ignored in a text box, with an overlay open, with modifiers and in the wrong view.
/// </summary>
public class ShortcutTests
{
    private static readonly DateOnly Today = DemoData.Today;

    private static readonly RawInputModifiers[] OtherModifiers =
        [RawInputModifiers.Control, RawInputModifiers.Alt, RawInputModifiers.Shift, RawInputModifiers.Meta,
         RawInputModifiers.Control | RawInputModifiers.Alt];

    // ---- N ---------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void N_opens_New_task_only_without_modifiers_outside_text_boxes_and_Settings()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        foreach (var mods in OtherModifiers)
        {
            shell.Key(Key.N, mods);
            Assert.Null(shell.Vm.Overlay);
        }

        var input = OpenTagInput(shell);
        Type(shell, Key.N, PhysicalKey.N, "n");
        Assert.Null(shell.Vm.Overlay);
        Assert.Equal("n", shell.Vm.Sidebar.TagDraft);                             // typed, not a shortcut
        Assert.True(input.IsFocused);

        shell.Window.ScreenHost.Focus();
        shell.Settle();
        shell.Key(Key.N);
        Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        shell.Key(Key.N);                                                         // the dialog is open: nothing new
        Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        shell.Key(Key.Escape);

        shell.Navigate(AppView.Settings);
        shell.Key(Key.N);
        Assert.Null(shell.Vm.Overlay);
    }

    [AvaloniaFact]
    public void The_N_press_that_opens_the_dialog_does_not_type_into_it_on_a_Cyrillic_layout()
    {
        // Windows reports the Latin virtual key on Cyrillic layouts (the "т" key is VK_N) with the Cyrillic character.
        using var shell = ShellFixture.Open(AppView.Week);
        Type(shell, Key.N, PhysicalKey.N, "т");
        var dialog = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        Assert.Equal(string.Empty, dialog.Title);
    }

    // ---- Ctrl+K ----------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Ctrl_K_opens_search_even_from_a_text_box_but_not_over_an_overlay_or_with_other_modifiers()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        shell.Key(Key.K);
        Assert.Null(shell.Vm.Overlay);
        foreach (var mods in new[] { RawInputModifiers.Control | RawInputModifiers.Shift, RawInputModifiers.Control | RawInputModifiers.Alt, RawInputModifiers.Alt, RawInputModifiers.Meta })
        {
            shell.Key(Key.K, mods);
            Assert.Null(shell.Vm.Overlay);
        }

        OpenTagInput(shell);
        shell.Key(Key.K, RawInputModifiers.Control);
        Assert.IsType<SearchPaletteViewModel>(shell.Vm.Overlay);
        shell.Key(Key.Escape);
        Assert.Null(shell.Vm.Overlay);

        shell.Key(Key.N);
        var dialog = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        shell.Key(Key.K, RawInputModifiers.Control);
        Assert.Same(dialog, shell.Vm.Overlay);
    }

    // ---- G I / G T -------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void G_I_goes_to_Inbox_and_G_T_to_Today_from_the_calendar_and_from_Settings()
    {
        using var shell = ShellFixture.Open(AppView.Week, new DateOnly(2026, 12, 2));
        shell.Key(Key.G);
        shell.Key(Key.I);
        Assert.Equal(AppView.Inbox, shell.Vm.Nav.View);
        Assert.Equal(new DateOnly(2026, 12, 2), shell.Vm.Nav.Anchor);              // Navigator.GoSection keeps the anchor

        shell.Key(Key.G);
        shell.Key(Key.T);
        Assert.Equal(new NavState(AppView.Today, Today), shell.Vm.Nav);

        shell.Navigate(AppView.Settings);
        shell.Key(Key.G);
        shell.Key(Key.I);
        Assert.Equal(AppView.Inbox, shell.Vm.Nav.View);
        Assert.False(shell.Vm.ShowsSettings);
    }

    [AvaloniaFact]
    public void G_chords_are_ignored_in_a_text_box_over_an_overlay_and_with_modifiers()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var start = shell.Vm.Nav;

        OpenTagInput(shell);
        Type(shell, Key.G, PhysicalKey.G, "g");
        Type(shell, Key.I, PhysicalKey.I, "i");
        Assert.Equal(start, shell.Vm.Nav);
        Assert.Equal("gi", shell.Vm.Sidebar.TagDraft);
        shell.Window.ScreenHost.Focus();
        shell.Settle();

        shell.Key(Key.G, RawInputModifiers.Shift);
        shell.Key(Key.I);
        Assert.Equal(start, shell.Vm.Nav);
        shell.Key(Key.G);
        shell.Key(Key.I, RawInputModifiers.Control);
        Assert.Equal(start, shell.Vm.Nav);

        shell.Key(Key.N);
        shell.Key(Key.G);
        shell.Key(Key.T);
        Assert.Equal(start, shell.Vm.Nav);
        Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
    }

    [AvaloniaFact]
    public void Chords_work_by_key_position_when_the_layout_reports_no_virtual_key()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        Type(shell, Key.None, PhysicalKey.G, "п");
        Type(shell, Key.None, PhysicalKey.I, "ш");
        Assert.Equal(AppView.Inbox, shell.Vm.Nav.View);
        Type(shell, Key.None, PhysicalKey.Digit3, "3");
        Assert.Equal(AppView.Week, shell.Vm.Nav.View);
    }

    // ---- 1–4 ---------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Digits_switch_the_calendar_level_at_the_current_anchor()
    {
        var anchor = new DateOnly(2026, 11, 20);
        using var shell = ShellFixture.Open(AppView.Week, anchor);
        (Key Key, AppView View)[] steps =
        [
            (Key.D1, AppView.Year), (Key.D2, AppView.Month), (Key.D4, AppView.Day), (Key.D3, AppView.Week),
            (Key.NumPad1, AppView.Year), (Key.NumPad4, AppView.Day),
        ];
        foreach (var (key, view) in steps)
        {
            shell.Key(key);
            Assert.Equal(new NavState(view, anchor), shell.Vm.Nav);
        }

        var navigations = 0;
        shell.Vm.Navigated += (_, _) => navigations++;
        shell.Key(Key.D4);                                                        // already on Day: nothing happens
        Assert.Equal(0, navigations);
    }

    [AvaloniaTheory]
    [InlineData(AppView.Inbox)]
    [InlineData(AppView.Today)]
    [InlineData(AppView.Settings)]
    public void Digits_open_the_calendar_from_the_other_sections_with_a_zoom_in(AppView from)
    {
        using var shell = ShellFixture.Open(from);
        var before = shell.Vm.Nav;
        NavigatedEventArgs? step = null;
        shell.Vm.Navigated += (_, e) => step = e;
        shell.Key(Key.D2);

        Assert.Equal(Navigator.Zoom(before, AppView.Month, before.Anchor).State, shell.Vm.Nav);
        Assert.Equal(TransitionKind.ZoomIn, step?.Kind);
        Assert.False(shell.Vm.ShowsSettings);
    }

    [AvaloniaFact]
    public void Digits_are_text_in_a_text_box_and_ignored_with_modifiers_or_over_an_overlay()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var start = shell.Vm.Nav;
        foreach (var mods in OtherModifiers)
        {
            shell.Key(Key.D1, mods);
            Assert.Equal(start, shell.Vm.Nav);
        }

        OpenTagInput(shell);
        Type(shell, Key.D2, PhysicalKey.Digit2, "2");
        Assert.Equal(start, shell.Vm.Nav);
        Assert.Equal("2", shell.Vm.Sidebar.TagDraft);
        shell.Window.ScreenHost.Focus();
        shell.Settle();

        shell.Key(Key.N);
        shell.Key(Key.D1);
        Assert.Equal(start, shell.Vm.Nav);
    }

    // ---- ← → ---------------------------------------------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData(AppView.Year)]
    [InlineData(AppView.Month)]
    [InlineData(AppView.Week)]
    [InlineData(AppView.Day)]
    public void Arrows_step_the_calendar_period(AppView view)
    {
        using var shell = ShellFixture.Open(view, new DateOnly(2026, 11, 20));
        var start = shell.Vm.Nav;
        shell.Key(Key.Right);
        var next = Navigator.Shift(start, 1, Today).State;
        Assert.Equal(next, shell.Vm.Nav);
        shell.Key(Key.Left);
        Assert.Equal(Navigator.Shift(next, -1, Today).State, shell.Vm.Nav);
    }

    [AvaloniaTheory]
    [InlineData(AppView.Inbox)]
    [InlineData(AppView.Today)]
    [InlineData(AppView.Settings)]
    public void Arrows_do_nothing_outside_the_calendar_and_pass_through(AppView view)
    {
        using var shell = ShellFixture.Open(view);
        var start = shell.Vm.Nav;
        shell.Key(Key.Right);
        shell.Key(Key.Left);
        Assert.Equal(start, shell.Vm.Nav);
        Assert.False(shell.Window.Shortcuts!.Handle(Key.Right, KeyModifiers.None, null));   // not swallowed either
    }

    [AvaloniaFact]
    public void Arrows_are_ignored_with_modifiers_in_a_text_box_and_over_an_overlay()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var start = shell.Vm.Nav;
        foreach (var mods in OtherModifiers)
        {
            shell.Key(Key.Right, mods);
            Assert.Equal(start, shell.Vm.Nav);
        }

        OpenTagInput(shell);
        shell.Key(Key.Right);
        shell.Key(Key.Left);
        Assert.Equal(start, shell.Vm.Nav);
        shell.Window.ScreenHost.Focus();
        shell.Settle();

        shell.Key(Key.K, RawInputModifiers.Control);
        shell.Key(Key.Right);
        Assert.Equal(start, shell.Vm.Nav);
    }

    [AvaloniaFact]
    public void Arrows_inside_the_view_switch_move_the_switch_and_never_also_shift_the_period()
    {
        var anchor = new DateOnly(2026, 11, 20);
        using var shell = ShellFixture.Open(AppView.Week, anchor);
        var zoom = shell.Window.Header.ZoomSwitch;
        Assert.True(Assert.IsAssignableFrom<Control>(zoom.ContainerFromIndex(zoom.SelectedIndex)).Focus(NavigationMethod.Tab));
        shell.Settle();

        shell.Key(Key.Right);                                                     // Week → Day
        Assert.Equal(new NavState(AppView.Day, anchor), shell.Vm.Nav);
        shell.Key(Key.Right);                                                     // last segment: nothing at all
        Assert.Equal(new NavState(AppView.Day, anchor), shell.Vm.Nav);

        for (var i = 0; i < 3; i++)
        {
            shell.Key(Key.Left);
        }

        Assert.Equal(new NavState(AppView.Year, anchor), shell.Vm.Nav);
        shell.Key(Key.Left);                                                      // first segment: nothing at all
        Assert.Equal(new NavState(AppView.Year, anchor), shell.Vm.Nav);
        Assert.True(zoom.IsKeyboardFocusWithin);
    }

    // ---- Esc ---------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Esc_closes_the_popover_then_the_dialog_and_otherwise_does_nothing()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        dialog.ClickPill(dialog.PriorityAnchor);
        Assert.True(dialog.Vm.IsPriorityOpen);

        Down(shell, Key.Escape);
        Assert.False(dialog.Vm.IsPriorityOpen);
        Assert.Same(dialog.Vm, shell.Vm.Overlay);
        Down(shell, Key.Escape);                                                  // auto-repeat of the same press
        Assert.Same(dialog.Vm, shell.Vm.Overlay);
        Release(shell, Key.Escape);

        shell.Key(Key.Escape);
        Assert.Null(shell.Vm.Overlay);

        shell.Navigate(AppView.Settings);
        var settings = shell.Vm.Nav;
        shell.Key(Key.Escape);                                                    // Esc does not leave Settings
        Assert.Equal(settings, shell.Vm.Nav);
        Assert.False(shell.Window.Shortcuts!.Handle(Key.Escape, KeyModifiers.None, null));
    }

    // ---- Key repeat --------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Held_arrows_repeat_but_held_N_digits_and_Ctrl_K_act_once_per_press()
    {
        using var shell = ShellFixture.Open(AppView.Week, new DateOnly(2026, 11, 20));
        for (var i = 0; i < 3; i++)
        {
            Down(shell, Key.Right);
        }

        Release(shell, Key.Right);
        Assert.Equal(new DateOnly(2026, 12, 11), shell.Vm.Nav.Anchor);           // three weeks on

        Down(shell, Key.N);
        Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        shell.Vm.CloseOverlay();
        shell.Settle();
        Down(shell, Key.N);                                                       // still the same press
        Assert.Null(shell.Vm.Overlay);
        Release(shell, Key.N);

        Down(shell, Key.K, RawInputModifiers.Control);
        Assert.IsType<SearchPaletteViewModel>(shell.Vm.Overlay);
        shell.Vm.CloseOverlay();
        shell.Settle();
        Down(shell, Key.K, RawInputModifiers.Control);
        Assert.Null(shell.Vm.Overlay);
        Release(shell, Key.K, RawInputModifiers.Control);

        Down(shell, Key.D1);
        Assert.Equal(AppView.Year, shell.Vm.Nav.View);
        shell.Vm.SetZoomLevel(AppView.Week);
        shell.Settle();
        Down(shell, Key.D1);
        Assert.Equal(AppView.Week, shell.Vm.Nav.View);
        Release(shell, Key.D1);
    }

    // ---- Space and Enter ---------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_held_Space_on_a_week_card_completes_the_task_once()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var card = shell.Window.GetVisualDescendants().OfType<TaskCard>()
            .First(c => c.IsEffectivelyVisible && c.DataContext is TaskRowViewModel { Done: false });
        var row = (TaskRowViewModel)card.DataContext!;
        var title = card.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_TitleButton");
        Assert.True(title.Focus(NavigationMethod.Tab));

        for (var i = 0; i < 3; i++)
        {
            Down(shell, Key.Space, physical: PhysicalKey.Space, symbol: " ");
        }

        Release(shell, Key.Space, physical: PhysicalKey.Space, symbol: " ");
        Assert.True(shell.App.Store.Get(row.Id)!.Done);
        Assert.Null(shell.Vm.Overlay);                                            // the title did not open the dialog
    }

    [AvaloniaFact]
    public void Space_on_a_Day_card_completes_its_task_from_any_of_its_buttons()
    {
        using var shell = ShellFixture.Open(AppView.Day, Today);
        var card = shell.Window.GetVisualDescendants().OfType<DayCard>()
            .First(c => c.IsEffectivelyVisible && c.DataContext is TaskRowViewModel { Done: false });
        var id = ((TaskRowViewModel)card.DataContext!).Id;
        Assert.True(card.TitleButton.Focus(NavigationMethod.Tab));
        shell.Key(Key.Space);
        Assert.True(shell.App.Store.Get(id)!.Done);
        Assert.Null(shell.Vm.Overlay);
        shell.Key(Key.Space);
        Assert.False(shell.App.Store.Get(id)!.Done);
    }

    [AvaloniaFact]
    public void Space_and_Enter_activate_the_focused_button()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var sidebar = shell.Window.GetVisualDescendants().OfType<SidebarItem>().ToList();
        Assert.True(sidebar.First(i => i.Name == "TodayItem").Focus(NavigationMethod.Tab));
        shell.Key(Key.Space);
        Assert.Equal(AppView.Today, shell.Vm.Nav.View);

        Assert.True(sidebar.First(i => i.Name == "InboxItem").Focus(NavigationMethod.Tab));
        shell.Key(Key.Enter);
        Assert.Equal(AppView.Inbox, shell.Vm.Nav.View);

        Assert.True(shell.Window.Header.NewTaskButton.Focus(NavigationMethod.Tab));
        shell.Key(Key.Space);
        Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
    }

    // ---- Focus after navigation --------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void When_a_shortcut_hides_the_focused_control_focus_moves_to_the_content_and_Tab_enters_the_new_screen()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var card = shell.Window.GetVisualDescendants().OfType<TaskCard>().First(c => c.IsEffectivelyVisible);
        Assert.True(card.GetVisualDescendants().OfType<Button>().First().Focus(NavigationMethod.Tab));

        shell.Key(Key.D1);
        var host = shell.Window.ScreenHost;
        Assert.True(host.IsFocused);
        Assert.DoesNotContain(":focus-visible", host.Classes);                    // no ring on the content area

        shell.Key(Key.Space);                                                     // nothing to activate
        Assert.Null(shell.Vm.Overlay);
        Assert.Equal(AppView.Year, shell.Vm.Nav.View);

        shell.Key(Key.Tab);                                                       // the first control of the Year screen
        var first = Assert.IsAssignableFrom<Control>(Focused(shell));
        Assert.True(host.IsVisualAncestorOf(first));
        Assert.Equal(FocusTargets(host).First(), first);
        AssertFocusVisible(first);

        Assert.True(host.Focus());
        shell.Key(Key.Tab, RawInputModifiers.Shift);                              // back to the header's last control
        Assert.Same(shell.Window.Header.NewTaskButton, Focused(shell));
    }

    [AvaloniaFact]
    public void A_period_step_that_rebuilds_the_focused_card_moves_focus_to_the_content()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var card = shell.Window.GetVisualDescendants().OfType<TaskCard>().First(c => c.IsEffectivelyVisible);
        Assert.True(card.GetVisualDescendants().OfType<Button>().First().Focus(NavigationMethod.Tab));
        shell.Key(Key.Right);
        Assert.True(shell.Window.ScreenHost.IsFocused);
    }

    [AvaloniaFact]
    public void A_still_visible_invoker_keeps_focus()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var calendar = shell.Window.GetVisualDescendants().OfType<SidebarItem>().First(i => i.Name == "CalendarItem");
        Assert.True(calendar.Focus(NavigationMethod.Tab));
        shell.Key(Key.G);
        shell.Key(Key.I);
        Assert.Equal(AppView.Inbox, shell.Vm.Nav.View);
        Assert.True(calendar.IsFocused);
        AssertFocusVisible(calendar);

        var inbox = shell.Window.GetVisualDescendants().OfType<SidebarItem>().First(i => i.Name == "TodayItem");
        shell.Click(inbox);
        Assert.Equal(AppView.Today, shell.Vm.Nav.View);
        Assert.True(inbox.IsFocused);
        Assert.DoesNotContain(":focus-visible", inbox.Classes);                   // a click shows no ring

        shell.Key(Key.D3);
        var next = shell.Window.Header.NextButton;
        Assert.True(next.Focus(NavigationMethod.Tab));
        shell.Key(Key.Enter);
        Assert.True(next.IsFocused);
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------------

    /// <summary>Opens the sidebar's "Tag name" input (a text box with focus).</summary>
    private static TextBox OpenTagInput(ShellFixture shell)
    {
        shell.Vm.Sidebar.StartAddTagCommand.Execute(null);
        shell.Settle();
        var input = shell.Window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "TagInput");
        Assert.True(input.IsFocused);
        return input;
    }
}
