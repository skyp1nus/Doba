using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Doba.App.Shell;
using Doba.App.Tests.DesignSystem;
using Doba.App.Tests.Support;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Shell;

/// <summary>The app and settings sidebars hosted on their own against a fake shell (DemoData, clock 2026-11-15 22:26).</summary>
public class SidebarTests
{
    private static readonly DateOnly DemoToday = new(2026, 11, 15);

    private sealed class Scene : IDisposable
    {
        private readonly bool _animations;

        public Scene(NavState? start = null, bool settings = false, double width = 232, double height = 960)
        {
            // Zero-length transitions: opacity/colour changes settle on the next clock tick.
            _animations = AppMotion.SystemAnimationsEnabled;
            AppMotion.SystemAnimationsEnabled = false;
            Harness = new ScreenHarness();
            Shell = new FakeShellNavigation(Harness.App.Store, DemoToday, start);
            ViewModel = new SidebarViewModel(Harness.App, Shell);
            View = settings ? new SettingsSidebarView { DataContext = ViewModel } : new SidebarView { DataContext = ViewModel };
            Window = ScreenHarness.Host(View, width, height);
            Assert.True(Window.TryFindResource("Brush.Bg.Window", Window.ActualThemeVariant, out var bg));
            Window.Background = (IBrush)bg!;
            Settle();
        }

        public ScreenHarness Harness { get; }
        public FakeShellNavigation Shell { get; }
        public SidebarViewModel ViewModel { get; }
        public UserControl View { get; }
        public Window Window { get; }

        public SidebarModel Expected() => Projector.Sidebar(Harness.App.Store,
            new ProjectionContext(Harness.App.DayClock.Today, Harness.App.DayClock.Now, Harness.App.Settings.Current.WeekStart, Shell.FilterTag));

        public T Named<T>(string name) where T : Control => View.FindControl<T>(name) ?? throw new InvalidOperationException(name);

        public List<SidebarItem> TagRows() =>
            View.GetVisualDescendants().OfType<SidebarItem>().Where(i => i.Classes.Contains("tag")).ToList();

        public SidebarItem TagRow(string name) => TagRows().Single(r => r.Label == name);

        public List<SidebarItem> TabRows() =>
            View.GetVisualDescendants().OfType<SidebarItem>().Where(i => i.Classes.Contains("tab")).ToList();

        public static TextBlock Part(SidebarItem item, string name) =>
            item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == name);

        public void Settle()
        {
            Window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Window.UpdateLayout();
        }

        public void Click(Control target)
        {
            var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)!.Value;
            Window.MouseMove(centre);
            Window.MouseDown(centre, MouseButton.Left);
            Window.MouseUp(centre, MouseButton.Left);
            Settle();
        }

        public void Key(Key key)
        {
            Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
            Settle();
        }

        public string Capture(string name) => Screenshots.Capture(Window, name);

        public void Dispose()
        {
            Window.Close();
            ViewModel.Dispose();
            Harness.Dispose();
            AppMotion.SystemAnimationsEnabled = _animations;
        }
    }

    private static bool Settles(Func<bool> condition) => Pump.Until(condition, TimeSpan.FromSeconds(2));

    // ---------- Counts ----------

    [AvaloniaFact]
    public void Counts_come_from_the_sidebar_projection()
    {
        using var s = new Scene();
        var expected = s.Expected();

        Assert.Equal(expected.InboxCount, s.ViewModel.InboxCount);
        Assert.Equal(expected.TodayCount, s.ViewModel.TodayCount);
        Assert.Equal(expected.Tags.Select(t => (t.Name, t.OpenCount, t.Active)),
            s.ViewModel.Tags.Select(t => (t.Name, t.Count, t.IsActive)));
        Assert.Equal(expected.InboxCount.ToString(System.Globalization.CultureInfo.InvariantCulture), s.Named<SidebarItem>("InboxItem").Count);
        Assert.Equal(expected.TodayCount.ToString(System.Globalization.CultureInfo.InvariantCulture), s.Named<SidebarItem>("TodayItem").Count);
        Assert.Null(s.Named<SidebarItem>("CalendarItem").Count);

        // Demo data as in docs/design/screens/week.png: Inbox 6, Today 2, work 12 / personal 6 / learning 6.
        Assert.Equal((6, 2), (s.ViewModel.InboxCount, s.ViewModel.TodayCount));
        Assert.Equal(["work", "personal", "learning"], s.TagRows().Select(r => r.Label));
        Assert.Equal(["12", "6", "6"], s.TagRows().Select(r => r.Count));
    }

    [AvaloniaFact]
    public void Counts_follow_store_changes_and_tag_rows_update_in_place()
    {
        using var s = new Scene();
        var store = s.Harness.App.Store;
        var rows = s.ViewModel.Tags.ToList();
        var visuals = s.TagRows();
        var inbox = s.ViewModel.InboxCount;
        var today = s.ViewModel.TodayCount;
        var work = s.ViewModel.Tags.Single(t => t.Name == "work");
        var personal = s.ViewModel.Tags.Single(t => t.Name == "personal");
        var workCount = work.Count;
        var personalCount = personal.Count;

        // Add: an undated #work task → Inbox +1, work +1.
        var added = store.Add(new TaskItem(0, "Sidebar test", string.Empty, null, null, null, 0, Priority.None, "work", false));
        s.Settle();
        Assert.Equal(inbox + 1, s.ViewModel.InboxCount);
        Assert.Equal(workCount + 1, work.Count);
        Assert.Equal((inbox + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), s.Named<SidebarItem>("InboxItem").Count);

        // Complete it → back to the original counts.
        store.ToggleDone(added.Id);
        s.Settle();
        Assert.Equal(inbox, s.ViewModel.InboxCount);
        Assert.Equal(workCount, work.Count);

        // Tag change on an open task for today: work → personal.
        store.ToggleDone(added.Id);
        store.Update(store.Get(added.Id)! with { Date = DemoToday, Tag = "personal" });
        s.Settle();
        Assert.Equal(inbox, s.ViewModel.InboxCount);
        Assert.Equal(today + 1, s.ViewModel.TodayCount);
        Assert.Equal(workCount, work.Count);
        Assert.Equal(personalCount + 1, personal.Count);
        Assert.Equal((personalCount + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), s.TagRow("personal").Count);

        // Delete → everything as before.
        store.Delete(added.Id);
        s.Settle();
        Assert.Equal((inbox, today, workCount, personalCount), (s.ViewModel.InboxCount, s.ViewModel.TodayCount, work.Count, personal.Count));

        // Same row view-models and the same visuals throughout (KeyedSync, no rebuild).
        Assert.Equal(rows, s.ViewModel.Tags);
        Assert.Equal(visuals, s.TagRows());
        var e = s.Expected();
        Assert.Equal(e.Tags.Select(t => t.OpenCount), s.ViewModel.Tags.Select(t => t.Count));
    }

    [AvaloniaFact]
    public void Refresh_is_idempotent_and_counts_are_not_dimmed_by_the_filter()
    {
        using var s = new Scene();
        var before = s.ViewModel.Tags.Select(t => (t.Name, t.Count)).ToList();
        s.Shell.SetFilter("work");
        s.ViewModel.Refresh();
        s.ViewModel.Refresh();
        s.Settle();

        Assert.Equal(before, s.ViewModel.Tags.Select(t => (t.Name, t.Count)));
        Assert.Equal(s.Expected().InboxCount, s.ViewModel.InboxCount);
        Assert.All(s.TagRows(), r => Assert.Equal(1.0, r.Opacity));
        Assert.All(s.TagRows(), r => Assert.Equal(1.0, Scene.Part(r, "PART_Count").Opacity));
    }

    // ---------- Highlight ----------

    [AvaloniaFact]
    public void Highlight_follows_the_section_index_and_hides_in_settings()
    {
        using var s = new Scene(new NavState(AppView.Inbox, DemoToday));
        var highlight = s.Named<SidebarHighlight>("Highlight");
        var layer = s.Named<Panel>("HighlightLayer");
        var inbox = s.Named<SidebarItem>("InboxItem");

        void Check(int index, bool visible)
        {
            Assert.Equal(index, s.ViewModel.HighlightIndex);
            Assert.Equal(visible, s.ViewModel.IsHighlightVisible);
            Assert.Equal(index * AppMotion.NavStep, highlight.RestY);
            Assert.True(Settles(() => Math.Abs(layer.Opacity - (visible ? 1 : 0)) < 0.001));
            // The pill lies exactly on the row it marks (render transform, not Margin/Top).
            var row = index switch { 0 => inbox, 1 => s.Named<SidebarItem>("TodayItem"), _ => s.Named<SidebarItem>("CalendarItem") };
            var rowTop = row.TranslatePoint(default, s.View)!.Value.Y;
            var pillTop = highlight.TranslatePoint(default, s.View)!.Value.Y;
            Assert.Equal(rowTop, pillTop, 3);
            Assert.Equal(row.Bounds.Width, highlight.Bounds.Width, 3);
            Assert.Equal(row.Bounds.Height, highlight.Bounds.Height, 3);
            Assert.Equal(default, highlight.Margin);
        }

        Check(0, true);
        Assert.Equal(4, inbox.TranslatePoint(default, s.View)!.Value.Y, 3); // nav padding-top 4
        Assert.True(inbox.IsCurrent);
        Assert.False(s.Named<SidebarItem>("TodayItem").IsCurrent);

        s.Shell.SetNav(new NavState(AppView.Today, DemoToday));
        Check(1, true);
        Assert.True(s.Named<SidebarItem>("TodayItem").IsCurrent);

        foreach (var calendar in new[] { AppView.Year, AppView.Month, AppView.Week, AppView.Day })
        {
            s.Shell.SetNav(new NavState(calendar, DemoToday));
            Check(2, true);
            Assert.True(s.Named<SidebarItem>("CalendarItem").IsCurrent);
        }

        s.Shell.SetNav(new NavState(AppView.Settings, DemoToday));
        Check(2, false); // the prototype parks it under Calendar (min(2, 3)) and fades it out
        Assert.False(s.Named<SidebarItem>("CalendarItem").IsCurrent);

        s.Shell.SetNav(new NavState(AppView.Inbox, DemoToday));
        Check(0, true);
    }

    [AvaloniaFact]
    public void Highlight_slides_on_the_compositor_with_the_indicator_timing()
    {
        var animations = AppMotion.SystemAnimationsEnabled;
        try
        {
            AppMotion.SystemAnimationsEnabled = true;
            var highlight = new SidebarHighlight();
            var host = new Window { Width = 232, Height = 200, Content = new Panel { Children = { highlight } } };
            highlight.Height = 32;
            highlight.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            host.Show();
            host.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // the composition visual exists after the first frame

            highlight.Index = 2;
            var handle = Doba.App.Motion.Animator.Running(highlight);
            Assert.NotNull(handle);
            Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.Indicator), handle.Duration);
            Assert.Equal(-2 * AppMotion.NavStep, handle.From.Y, 3);    // starts where the pill was drawn
            Assert.Equal(0, handle.To.Y, 3);                           // ends at its new resting transform
            Assert.Equal(1, handle.From.Opacity);                      // no fade
            Assert.Same(AppMotion.EaseOut, handle.Easing);
            Assert.Equal(2 * AppMotion.NavStep, highlight.RestY);
            handle.Complete();
            host.Close();
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
        }
    }

    // ---------- Clicks ----------

    [AvaloniaFact]
    public void Each_row_calls_the_right_shell_method()
    {
        using var s = new Scene();
        s.Shell.Apply = false;

        s.Click(s.Named<SidebarItem>("InboxItem"));
        s.Click(s.Named<SidebarItem>("TodayItem"));
        s.Click(s.Named<SidebarItem>("CalendarItem"));
        s.Click(s.TagRow("personal"));
        s.Click(s.Named<SidebarItem>("CollapseItem"));
        s.Click(s.Named<SidebarItem>("SettingsItem"));

        Assert.Equal(
            ["GoSection(Inbox)", "GoSection(Today)", "GoCalendar()", "ToggleTagFilter(personal)", "ToggleSidebar()", "OpenSettings()"],
            s.Shell.Calls);
    }

    [AvaloniaFact]
    public void Every_row_is_a_focusable_button_with_an_accessible_name()
    {
        using var s = new Scene();
        var items = s.View.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        Assert.All(items, b => Assert.True(b.Focusable));
        Assert.Equal("Inbox", AutomationProperties.GetName(s.Named<SidebarItem>("InboxItem")));
        Assert.Equal("work", AutomationProperties.GetName(s.TagRow("work")));
        Assert.Equal("New tag", AutomationProperties.GetName(s.Named<Button>("AddTagButton")));
        Assert.Equal("New tag", ToolTip.GetTip(s.Named<Button>("AddTagButton")));
        Assert.Equal("Collapse sidebar", AutomationProperties.GetName(s.Named<SidebarItem>("CollapseItem")));
        Assert.Equal("Collapse sidebar", ToolTip.GetTip(s.Named<SidebarItem>("CollapseItem")));

        s.Shell.SetCollapsed(true);
        s.Settle();
        Assert.Equal("Expand sidebar", AutomationProperties.GetName(s.Named<SidebarItem>("CollapseItem")));
        Assert.Equal("Expand sidebar", ToolTip.GetTip(s.Named<SidebarItem>("CollapseItem")));
    }

    // ---------- Tags ----------

    [AvaloniaFact]
    public void Active_filter_shows_the_pressed_state()
    {
        using var s = new Scene();
        s.Click(s.TagRow("work"));

        Assert.Equal("work", s.Shell.FilterTag);
        Assert.True(s.ViewModel.Tags.Single(t => t.Name == "work").IsActive);
        Assert.True(s.TagRow("work").IsSelected);
        Assert.Contains(":selected", s.TagRow("work").Classes);
        Assert.All(s.TagRows().Where(r => r.Label != "work"), r => Assert.False(r.IsSelected));
        var active = Pixels.Resource("Brush.Overlay.Active");
        Assert.True(Settles(() => s.TagRow("work").GetVisualDescendants().OfType<Border>()
            .First(p => p.Name == "PART_Item").Background is ISolidColorBrush b && b.Color == active));
        var strong = Pixels.Resource("Brush.Text.Strong");
        Assert.True(Settles(() => Scene.Part(s.TagRow("work"), "PART_Label").Foreground is ISolidColorBrush f && f.Color == strong));

        s.Click(s.TagRow("work"));
        Assert.Null(s.Shell.FilterTag);
        Assert.All(s.TagRows(), r => Assert.False(r.IsSelected));
        Assert.Equal(["ToggleTagFilter(work)", "ToggleTagFilter(work)"], s.Shell.Calls);
    }

    [AvaloniaFact]
    public void Plus_opens_the_input_and_enter_creates_the_tag_with_the_raw_text()
    {
        using var s = new Scene();
        var row = s.Named<Border>("AddTagRow");
        var input = s.Named<TextBox>("TagInput");
        Assert.False(row.IsVisible);
        Assert.False(s.Named<TextBlock>("AddTagHint").IsVisible);

        s.Click(s.Named<Button>("AddTagButton"));
        Assert.True(s.ViewModel.IsAddingTag);
        Assert.True(row.IsVisible);
        Assert.True(s.Named<TextBlock>("AddTagHint").IsVisible);
        Assert.Equal("↵ create · Esc cancel", s.Named<TextBlock>("AddTagHint").Text);
        Assert.Equal("Tag name", input.PlaceholderText);
        Assert.True(Settles(() => input.IsFocused));

        s.Window.KeyTextInput("# Deep Work");
        s.Settle();
        Assert.Equal("# Deep Work", s.ViewModel.TagDraft);
        s.Key(Key.Enter);

        Assert.Equal(["# Deep Work"], s.Shell.AddedTags);
        Assert.False(s.ViewModel.IsAddingTag);
        Assert.False(row.IsVisible);
        Assert.Equal(string.Empty, s.ViewModel.TagDraft);
        // The shell normalised and stored it; the new row arrived through the store change.
        Assert.Equal(["work", "personal", "learning", "deep-work"], s.TagRows().Select(r => r.Label));
        Assert.Equal("0", s.TagRow("deep-work").Count);
        Assert.Single(s.Shell.AddedTags); // losing focus after Enter does not create it twice
    }

    [AvaloniaFact]
    public void Esc_cancels_without_creating_and_losing_focus_creates_a_typed_name()
    {
        using var s = new Scene();
        s.Click(s.Named<Button>("AddTagButton"));
        Assert.True(Settles(() => s.Named<TextBox>("TagInput").IsFocused));
        s.Window.KeyTextInput("errands");
        s.Settle();
        s.Key(Key.Escape);

        Assert.Empty(s.Shell.AddedTags);
        Assert.False(s.ViewModel.IsAddingTag);
        Assert.Equal(string.Empty, s.ViewModel.TagDraft);
        Assert.Equal(3, s.TagRows().Count);

        // Blur with text: created (the prototype's onBlur → createList). Blur when empty: just closes.
        s.Click(s.Named<Button>("AddTagButton"));
        Assert.True(Settles(() => s.Named<TextBox>("TagInput").IsFocused));
        s.Window.KeyTextInput("errands");
        s.Settle();
        s.Named<SidebarItem>("CalendarItem").Focus();
        s.Settle();
        Assert.Equal(["errands"], s.Shell.AddedTags);
        Assert.False(s.ViewModel.IsAddingTag);

        s.Click(s.Named<Button>("AddTagButton"));
        Assert.True(Settles(() => s.Named<TextBox>("TagInput").IsFocused));
        s.Named<SidebarItem>("CalendarItem").Focus();
        s.Settle();
        Assert.Equal(["errands"], s.Shell.AddedTags);
        Assert.False(s.ViewModel.IsAddingTag);
    }

    // ---------- Rail ----------

    [AvaloniaFact]
    public void Rail_hides_labels_and_shows_tag_initials()
    {
        using var s = new Scene(width: 56);
        var items = s.View.GetVisualDescendants().OfType<SidebarItem>().ToList();
        Assert.All(s.TagRows(), r => Assert.Equal("#", Scene.Part(r, "PART_BadgeText").Text));

        s.Shell.SetFilter("personal");
        s.Shell.SetCollapsed(true);
        s.Settle();

        Assert.True(s.ViewModel.IsCollapsed);
        Assert.All(items, i => Assert.True(i.IsRail));
        Assert.True(Settles(() => items.All(i => Scene.Part(i, "PART_Label").Opacity == 0)));
        Assert.True(Settles(() => items.Where(i => i.Count is not null).All(i => Scene.Part(i, "PART_Count").Opacity == 0)));
        var header = s.Named<Grid>("TagsHeader");
        Assert.True(Settles(() => header.Opacity == 0));
        Assert.False(header.IsHitTestVisible);
        Assert.False(s.Named<Button>("AddTagButton").IsEffectivelyEnabled);
        Assert.Equal(["W", "P", "L"], s.TagRows().Select(r => Scene.Part(r, "PART_BadgeText").Text));

        // Badge colours: Border.Default + Text.Secondary, the active filter Accent + OnPrimary.
        static Border Badge(SidebarItem r) => r.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Badge");
        var personal = s.TagRow("personal");
        Assert.True(Settles(() => Badge(personal).Background is ISolidColorBrush b && b.Color == Pixels.Resource("Brush.Accent")));
        Assert.True(Settles(() => Badge(s.TagRow("work")).Background is ISolidColorBrush b && b.Color == Pixels.Resource("Brush.Border.Default")));
        Assert.True(Settles(() => Scene.Part(personal, "PART_BadgeText").Foreground is ISolidColorBrush f && f.Color == Pixels.Resource("Brush.Text.OnPrimary")));

        // Icons stay centred in the 56 px rail: 10 (nav padding) + 10 (row padding) + 16 + 10 + 10.
        foreach (var item in items)
        {
            var icon = item.GetVisualDescendants().OfType<Control>().First(c => c.Name is "PART_Icon" or "PART_Badge");
            var x = icon.TranslatePoint(new Point(icon.Bounds.Width / 2, 0), s.View)!.Value.X;
            Assert.Equal(28, x, 1);
        }

        // Back to expanded: labels return, "#" badges.
        s.Shell.SetCollapsed(false);
        s.Settle();
        Assert.True(Settles(() => items.All(i => Scene.Part(i, "PART_Label").Opacity == 1)));
        Assert.All(s.TagRows(), r => Assert.Equal("#", Scene.Part(r, "PART_BadgeText").Text));
        Assert.True(Settles(() => header.Opacity == 1));
    }

    [AvaloniaFact]
    public void Collapsing_while_adding_a_tag_closes_the_input_and_keeps_what_was_typed()
    {
        using var s = new Scene();
        s.Click(s.Named<Button>("AddTagButton"));
        s.ViewModel.TagDraft = "reading";
        s.Shell.SetCollapsed(true);
        s.Settle();

        Assert.False(s.ViewModel.IsAddingTag);
        Assert.Equal(["reading"], s.Shell.AddedTags);
    }

    // ---------- Settings sidebar ----------

    [AvaloniaFact]
    public void Settings_tabs_reflect_the_open_tab_and_switch_it()
    {
        using var s = new Scene(new NavState(AppView.Settings, DemoToday, SettingsTab.Calendar, new NavState(AppView.Week, DemoToday)), settings: true);
        var tabs = s.TabRows();
        Assert.Equal(["Appearance", "Calendar", "Tasks", "Windows", "Data", "Shortcuts"], tabs.Select(t => t.Label));
        Assert.All(tabs, t => Assert.NotNull(t.Icon));
        Assert.Equal([false, true, false, false, false, false], tabs.Select(t => t.IsSelected));
        Assert.Equal(SettingsTab.Calendar, s.ViewModel.ActiveTab);

        s.Click(tabs[4]);
        Assert.Equal(["SetSettingsTab(Data)"], s.Shell.Calls);
        Assert.Equal([false, false, false, false, true, false], s.TabRows().Select(t => t.IsSelected));
        Assert.Equal(tabs, s.TabRows()); // same rows

        s.Click(s.Named<SidebarItem>("BackItem"));
        Assert.Equal("BackFromSettings()", s.Shell.Calls[^1]);
        Assert.Equal(AppView.Week, s.Shell.Nav.View);

        var footer = s.View.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Doba 0.1 · local only");
        Assert.True(footer.IsEffectivelyVisible);
        Assert.Contains(s.View.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "SETTINGS");
    }

    // ---------- Screenshots (compare with the left edge of docs/design/screens/*.png) ----------

    [AvaloniaFact]
    public void Screenshot_sidebar_content()
    {
        using var s = new Scene(new NavState(AppView.Week, DemoToday));
        var path = s.Capture("sidebar-content");
        Assert.True(new FileInfo(path).Length > 1000);
    }

    [AvaloniaFact]
    public void Screenshot_sidebar_rail_content()
    {
        using var s = new Scene(new NavState(AppView.Week, DemoToday), width: 56);
        s.Shell.SetCollapsed(true);
        s.Settle();
        Assert.True(Settles(() => s.Named<Grid>("TagsHeader").Opacity == 0));
        var path = s.Capture("sidebar-rail-content");
        Assert.True(new FileInfo(path).Length > 500);
    }

    [AvaloniaFact]
    public void Screenshot_sidebar_add_tag_content()
    {
        // No reference image: the inline input as the markup draws it (lines 108-115), for review.
        using var s = new Scene(new NavState(AppView.Week, DemoToday));
        s.Click(s.Named<Button>("AddTagButton"));
        s.Settle();
        var row = s.Named<Border>("AddTagRow");
        var last = s.TagRow("learning");
        // 30 px row 1 px below the last tag, hint 1 px below the row.
        Assert.Equal(30, row.Bounds.Height, 3);
        Assert.Equal(last.TranslatePoint(default, s.View)!.Value.Y + 31, row.TranslatePoint(default, s.View)!.Value.Y, 3);
        var path = s.Capture("sidebar-add-tag-content");
        Assert.True(new FileInfo(path).Length > 1000);
    }

    [AvaloniaFact]
    public void Screenshot_settings_sidebar_content()
    {
        using var s = new Scene(new NavState(AppView.Settings, DemoToday, SettingsTab.Appearance, new NavState(AppView.Week, DemoToday)), settings: true);
        var path = s.Capture("settings-sidebar-content");
        Assert.True(new FileInfo(path).Length > 1000);
    }
}
