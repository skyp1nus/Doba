using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Doba.App.Dialogs;
using Doba.App.Dialogs.Popovers;
using Doba.App.Dialogs.Search;
using Doba.App.Tests.Dialogs;
using Doba.App.Tests.Support;
using Doba.Core.Demo;
using Doba.Core.Navigation;
using static Doba.App.Tests.Keyboard.KeyboardKit;

namespace Doba.App.Tests.Keyboard;

/// <summary>
/// KICKOFF §6: everything is reachable by Tab with a visible focus state. For every screen and overlay a full Tab cycle
/// visits every interactive control exactly once, in reading order within each region (title bar, sidebar, page header,
/// content; dialog, popover), every visited control shows the keyboard focus ring, Shift+Tab walks the same cycle
/// backwards, and inside an overlay Tab never leaves it.
/// </summary>
public class TabOrderTests
{
    public static TheoryData<AppView, SettingsTab> Screens => new()
    {
        { AppView.Inbox, SettingsTab.Appearance },
        { AppView.Today, SettingsTab.Appearance },
        { AppView.Year, SettingsTab.Appearance },
        { AppView.Month, SettingsTab.Appearance },
        { AppView.Week, SettingsTab.Appearance },
        { AppView.Day, SettingsTab.Appearance },
        { AppView.Settings, SettingsTab.Appearance },
        { AppView.Settings, SettingsTab.Calendar },
        { AppView.Settings, SettingsTab.Tasks },
        { AppView.Settings, SettingsTab.Windows },
        { AppView.Settings, SettingsTab.Data },
        { AppView.Settings, SettingsTab.Shortcuts },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Screens))]
    public void Tab_cycles_through_every_control_of_the_screen_in_reading_order(AppView view, SettingsTab tab)
    {
        // The Inbox list is virtualised: a tall window realises all of it, so "every control" is well defined (the
        // scrolling case at the normal size is covered below).
        using var shell = ShellFixture.Open(view, tab: tab, height: view == AppView.Inbox ? 2600 : 1000);
        AssertWindowCycle(shell);
    }

    [AvaloniaFact]
    public void Tab_cycles_through_the_collapsed_sidebar_rail()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        shell.Vm.ToggleSidebar();
        shell.Settle();
        var walk = AssertWindowCycle(shell);
        Assert.Contains(walk, c => c.Name == "InboxItem");
        Assert.DoesNotContain(walk, c => c.Name == "AddTagButton");               // disabled in the rail
    }

    [AvaloniaFact]
    public void Tab_reaches_the_end_of_the_virtualised_Inbox_and_wraps_to_the_title_bar()
    {
        using var shell = ShellFixture.Open(AppView.Inbox);
        // Rows are recycled as the list scrolls: record each focused row's task as it gets focus.
        var rows = new List<long>();
        var walk = Walk(shell, shell.Window.TitleBar.SearchButton, each: c =>
        {
            if (c.DataContext is Doba.App.ViewModels.TaskRowViewModel row && !rows.Contains(row.Id))
            {
                rows.Add(row.Id);
            }
        });
        var inbox = shell.Screen<Doba.App.Screens.Inbox.InboxViewModel>();
        Assert.Equal(AllInboxRowIds(inbox).Order(), rows.Order());
        Assert.Equal(AllInboxRowIds(inbox).Last(), rows.Last());                    // in order, down to the last row
    }

    [AvaloniaFact]
    public void Tab_stays_inside_the_new_task_dialog()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        shell.Key(Key.N);
        var view = Assert.IsType<TaskDialogView>(shell.Window.Overlays.OverlayView);
        AssertOverlayCycle(shell, view, view.TitleBox);
    }

    [AvaloniaFact]
    public void Tab_stays_inside_the_edit_dialog_including_Delete()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        shell.Vm.EditTask(shell.App.Store.All.First(t => t.Date == DemoData.Today).Id);
        shell.Settle();
        var view = Assert.IsType<TaskDialogView>(shell.Window.Overlays.OverlayView);
        var walk = AssertOverlayCycle(shell, view, view.TitleBox);
        Assert.Contains(walk, c => c.Name == "PART_Delete");
    }

    public static TheoryData<int> Popovers => new() { 0, 1, 2, 3 };

    /// <summary>
    /// With a popover open Tab goes pill → popover → the next pill, as in the prototype's DOM. The popover closes when the
    /// cycle reaches the title box (focusing the title or the notes closes it: decided), so the cycle is checked against
    /// the stops that exist while it is open, with positions recorded as each control takes focus.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Popovers))]
    public void Tab_walks_the_dialog_with_each_popover_open(int index)
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        var anchor = dialog.View.Anchors[index];
        dialog.ClickPill(anchor);
        var popover = DialogKit.Dialog.OpenPopover(anchor);
        var pill = (Control)DialogKit.Dialog.Pill(anchor);
        var stops = Stops(dialog.View);

        var bounds = new Dictionary<Control, Rect>();
        var names = new Dictionary<Control, string>();
        var inPopover = new HashSet<Control>();
        var walk = Walk(shell, pill, each: c =>
        {
            AssertFocusVisible(c);
            AssertShowable(c);
            Assert.True(dialog.View.IsVisualAncestorOf(c), Describe(c) + " is outside the overlay.");
            bounds[c] = Bounds(c, shell.Window);
            names[c] = Describe(c);
            if (popover.IsVisualAncestorOf(c))
            {
                inPopover.Add(c);
            }
        });

        AssertVisitsEveryStopOnce(walk, stops);
        Assert.NotEmpty(inPopover);
        var popoverRun = walk.Skip(1).TakeWhile(inPopover.Contains).ToList();
        Assert.Equal(inPopover.Count, popoverRun.Count);                              // right after its pill, in one run
        AssertReadingOrder(walk, c => bounds[c], c => inPopover.Contains(c) ? popover : dialog.View);

        // Shift+Tab from the control after the popover walks back through it to the pill.
        if (!anchor.IsShowing)
        {
            dialog.ClickPill(anchor);
        }

        var segment = walk.Take(popoverRun.Count + 2).ToList();                       // pill, popover…, next stop
        Assert.True(segment[^1].Focus(NavigationMethod.Tab));
        shell.Settle();
        var back = new List<Control> { segment[^1] };
        for (var i = 1; i < segment.Count; i++)
        {
            shell.Key(Key.Tab, RawInputModifiers.Shift);
            back.Add(Assert.IsAssignableFrom<Control>(Focused(shell)));
        }

        Assert.Equal(Enumerable.Reverse(segment).Select(c => names[c]), back.Select(Describe));
        Assert.True(anchor.IsShowing);
        AssertRingsDrawn(shell, FocusTargets(anchor.Popover!));          // the reopened popover's controls
    }
    [AvaloniaFact]
    public void Tab_stays_in_the_search_query_box()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        shell.Key(Key.K, RawInputModifiers.Control);
        var view = Assert.IsType<SearchPaletteView>(shell.Window.Overlays.OverlayView);
        shell.Window.KeyTextInput("re");
        shell.Settle();
        Assert.NotEmpty(((SearchPaletteViewModel)view.DataContext!).Hits);

        var walk = AssertOverlayCycle(shell, view, view.QueryBox);
        Assert.Equal([view.QueryBox], walk);                                     // hit rows are not focusable (decided)
    }

    // ---- Shared assertions -----------------------------------------------------------------------------------------

    private static List<Control> AssertWindowCycle(ShellFixture shell)
    {
        var w = shell.Window;
        Visual[] regions = [w.TitleBar, w.SidebarColumn, w.Header, w.ScreenHost];
        var start = w.TitleBar.SearchButton;
        var walk = Walk(shell, start, each: c => { AssertFocusVisible(c); AssertShowable(c); });
        AssertVisitsEveryStopOnce(walk, w);
        AssertReadingOrder(walk, w, c => RegionOf(c, regions));
        // Title bar → sidebar → header → content (Settings has nothing focusable in its header).
        Assert.Equal(regions.Where(r => walk.Any(c => RegionOf(c, regions) == r)), walk.Select(c => RegionOf(c, regions)).Distinct());

        var back = Walk(shell, start, backwards: true);
        AssertReverses(walk, back);
        AssertRingsDrawn(shell, walk);
        return walk;
    }

    private static List<Control> AssertOverlayCycle(ShellFixture shell, Control view, Control start)
    {
        var walk = Walk(shell, start, each: c => { AssertFocusVisible(c); AssertShowable(c); });
        foreach (var c in walk)
        {
            Assert.True(view.IsVisualAncestorOf(c), Describe(c) + " is outside the overlay.");
        }

        AssertVisitsEveryStopOnce(walk, view);
        AssertReadingOrder(walk, shell.Window, _ => view);

        var back = Walk(shell, start, backwards: true);
        AssertReverses(walk, back);
        AssertRingsDrawn(shell, walk);
        return walk;
    }

    /// <summary>Pixel-checks the ring on one control per look; the neutral focus is the title-bar search box (or the
    /// first sidebar row when that is the control itself).</summary>
    private static void AssertRingsDrawn(ShellFixture shell, IEnumerable<Control> walk)
    {
        foreach (var c in OnePerTheme(walk))
        {
            Control neutral = ReferenceEquals(c, shell.Window.TitleBar.SearchButton)
                ? shell.Window.GetVisualDescendants().OfType<Doba.App.Shell.SidebarItem>().First(i => i.IsEffectivelyVisible)
                : shell.Window.TitleBar.SearchButton;
            AssertRingDrawn(shell, c, neutral);
        }
    }

    private static IEnumerable<long> AllInboxRowIds(Doba.App.Screens.Inbox.InboxViewModel inbox) =>
        inbox.Left.Concat(inbox.Right).SelectMany(g => g.Items).Select(r => r.Id);
}
