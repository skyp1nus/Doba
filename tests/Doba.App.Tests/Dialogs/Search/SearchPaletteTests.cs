using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Dialogs;
using Doba.App.Dialogs.Search;
using Doba.App.Tests.Support;
using Doba.Core.Demo;
using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;

namespace Doba.App.Tests.Dialogs.Search;

/// <summary>The Ctrl+K search palette, driven through the real shell (ShellFixture: DemoData, no-op platform).</summary>
public class SearchPaletteTests
{
    private static readonly DateOnly Today = DemoData.Today;

    private static (SearchPaletteViewModel Vm, SearchPaletteView View) Open(ShellFixture shell)
    {
        shell.Key(Key.K, RawInputModifiers.Control);
        var vm = Assert.IsType<SearchPaletteViewModel>(shell.Vm.Overlay);
        var view = Assert.IsType<SearchPaletteView>(shell.Window.Overlays.OverlayView);
        return (vm, view);
    }

    private static void Type(ShellFixture shell, string text)
    {
        shell.Window.KeyTextInput(text);
        shell.Settle();
    }

    private static List<Button> Rows(SearchPaletteView view) =>
        view.HitList.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();

    private static IReadOnlyList<SearchHit> Expected(ShellFixture shell, string query) =>
        Projector.Search(shell.App.Store, new ProjectionContext(Today, shell.App.DayClock.Now, shell.App.Settings.Current.WeekStart, null), query);

    [AvaloniaFact]
    public void Ctrl_K_on_the_Week_screen_opens_it_with_the_focus_in_the_query_box_and_no_rows()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (vm, view) = Open(shell);

        Assert.True(view.QueryBox.IsFocused);
        Assert.Equal("Search tasks", view.QueryBox.PlaceholderText);
        Assert.Empty(vm.Hits);
        Assert.Equal(-1, vm.SelectedIndex);
        Assert.False(view.HitScroll.IsEffectivelyVisible);
        Assert.False(view.NoResults.IsEffectivelyVisible);              // empty query: no "no results" text either
        Assert.True(view.Footer.IsEffectivelyVisible);
        Assert.Equal("↑↓ move · ↵ open · Esc close", Assert.IsType<TextBlock>(view.Footer.Child).Text);
        Assert.Equal(new NavState(AppView.Week, Today), shell.Vm.Nav);   // opening never navigates
    }

    [AvaloniaFact]
    public void The_title_bar_search_button_opens_it()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        shell.Click(shell.Window.TitleBar.SearchButton);
        Assert.IsType<SearchPaletteViewModel>(shell.Vm.Overlay);
        var view = Assert.IsType<SearchPaletteView>(shell.Window.Overlays.OverlayView);
        Assert.True(view.QueryBox.IsFocused);
    }

    [AvaloniaFact]
    public void Typing_filters_titles_by_case_insensitive_substring_and_selects_the_first_hit()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (vm, view) = Open(shell);

        Type(shell, "REVIEW");
        Assert.Equal("REVIEW", vm.Query);
        var expected = Expected(shell, "REVIEW");
        Assert.True(expected.Count > 8);
        Assert.Equal(expected.Select(h => h.Row.Id), vm.Hits.Select(h => h.Id));
        Assert.All(vm.Hits, h => Assert.Contains("review", h.Title, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expected.Select(h => h.Context), vm.Hits.Select(h => h.Context));
        Assert.Equal(0, vm.SelectedIndex);
        Assert.True(vm.Hits[0].IsSelected);
        Assert.Single(vm.Hits, h => h.IsSelected);
        Assert.Equal(vm.Hits.Count, Rows(view).Count);
        Assert.Contains("selected", Rows(view)[0].Classes);

        // Narrowing keeps the rows that still match (updated in place, not rebuilt) and re-selects the first hit.
        Press(shell, Key.Down);
        var kept = vm.Hits.First(h => h.Title.Contains("pull requests", StringComparison.Ordinal));
        Type(shell, " open");
        Assert.Equal(Expected(shell, "REVIEW open").Select(h => h.Row.Id), vm.Hits.Select(h => h.Id));
        Assert.Contains(kept, vm.Hits);
        Assert.Equal(0, vm.SelectedIndex);
        Assert.All(vm.Hits, h => Assert.Contains("review open", h.Title, StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public void Arrows_move_the_selection_clamp_at_both_ends_and_keep_it_in_view()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (vm, view) = Open(shell);
        Type(shell, "review");
        var count = vm.Hits.Count;
        Assert.True(count > 8);

        Press(shell, Key.Up);                                              // clamped at the top, no wrap
        Assert.Equal(0, vm.SelectedIndex);

        Press(shell, Key.Down);
        Press(shell, Key.Down);
        Assert.Equal(2, vm.SelectedIndex);
        Assert.True(vm.Hits[2].IsSelected);
        Assert.Single(vm.Hits, h => h.IsSelected);
        Assert.Equal(0, view.HitScroll.Offset.Y);                        // still in view: no scrolling yet

        for (var i = 0; i < count + 3; i++)
        {
            Press(shell, Key.Down);
        }

        Assert.Equal(count - 1, vm.SelectedIndex);                       // clamped at the bottom, no wrap
        Assert.True(view.HitScroll.Offset.Y > 0);
        AssertInView(view, count - 1);

        for (var i = 0; i < count - 1; i++)
        {
            Press(shell, Key.Up);
        }

        Assert.Equal(0, vm.SelectedIndex);
        Assert.Equal(0, view.HitScroll.Offset.Y);
        Assert.Equal("review", vm.Query);                                // arrows never reach the text box
        Assert.IsType<SearchPaletteViewModel>(shell.Vm.Overlay);
    }

    [AvaloniaFact]
    public void Eight_rows_show_and_the_rest_scroll_inside_the_list()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (vm, view) = Open(shell);
        Type(shell, "e");
        Assert.True(vm.Hits.Count > 8);
        Assert.True(vm.Hits.Count <= SearchPaletteViewModel.Limit);
        Assert.Equal(8 * 32 + 2 * 6, view.HitScroll.Bounds.Height);
        Assert.Equal(32, Rows(view)[0].Bounds.Height);
        Assert.True(view.HitScroll.Extent.Height > view.HitScroll.Viewport.Height);
    }

    [AvaloniaFact]
    public void Enter_opens_the_task_dialog_for_the_selected_hit()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (vm, _) = Open(shell);
        Type(shell, "review");
        Press(shell, Key.Down);
        Press(shell, Key.Down);
        var id = vm.Hits[2].Id;

        Press(shell, Key.Enter);
        var dialog = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        Assert.Equal(TaskDialog.EditDraft(shell.App.Store.Get(id)!, Today), dialog.Draft);
        Assert.IsType<TaskDialogView>(shell.Window.Overlays.OverlayView);
        Assert.Equal(new NavState(AppView.Week, Today), shell.Vm.Nav);   // never navigates
    }

    [AvaloniaFact]
    public void Clicking_a_row_opens_the_task_dialog_for_that_hit()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (vm, view) = Open(shell);
        Type(shell, "review");
        var id = vm.Hits[3].Id;
        var done = shell.App.Store.Get(id)!.Done;

        shell.Click(Rows(view)[3]);
        var dialog = Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        Assert.Equal(TaskDialog.EditDraft(shell.App.Store.Get(id)!, Today), dialog.Draft);
        Assert.Equal(done, shell.App.Store.Get(id)!.Done);                // the palette never toggles a task
    }

    [AvaloniaFact]
    public void Enter_without_hits_does_nothing()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (vm, _) = Open(shell);
        Press(shell, Key.Enter);
        Assert.Same(vm, shell.Vm.Overlay);
        Type(shell, "zzzz");
        Press(shell, Key.Enter);
        Assert.Same(vm, shell.Vm.Overlay);
    }

    [AvaloniaFact]
    public void Esc_closes_it_and_the_focus_returns()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var today = shell.Window.Header.TodayButton;
        Assert.True(today.Focus());
        var (vm, _) = Open(shell);
        Type(shell, "review");
        Assert.False(vm.OnEscape());

        Press(shell, Key.Escape);
        Assert.Null(shell.Vm.Overlay);
        Assert.False(shell.Window.Overlays.IsVisible);
        Assert.True(today.IsFocused);
        Assert.Equal(new NavState(AppView.Week, Today), shell.Vm.Nav);
    }

    [AvaloniaFact]
    public void No_hits_shows_one_muted_line_and_an_empty_query_shows_nothing()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (vm, view) = Open(shell);

        Type(shell, "zzzz");
        Assert.Empty(vm.Hits);
        Assert.Equal(-1, vm.SelectedIndex);
        Assert.True(view.NoResults.IsEffectivelyVisible);
        Assert.Equal("No tasks found", view.NoResults.Text);
        Assert.Equal(Color(view, "Brush.Text.Muted"), ((ISolidColorBrush)view.NoResults.Foreground!).Color);
        Assert.False(view.HitScroll.IsEffectivelyVisible);

        view.QueryBox.Text = "   ";                                      // white space is an empty query
        shell.Settle();
        Assert.Empty(vm.Hits);
        Assert.False(view.NoResults.IsEffectivelyVisible);
        Assert.False(view.HitScroll.IsEffectivelyVisible);

        view.QueryBox.Text = string.Empty;
        shell.Settle();
        Assert.False(view.NoResults.IsEffectivelyVisible);
        Assert.False(view.HitScroll.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Done_tasks_render_struck_and_muted()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (vm, view) = Open(shell);
        Type(shell, "weekly review");
        Assert.Contains(vm.Hits, h => h.Done);
        Assert.Contains(vm.Hits, h => !h.Done);

        var rows = Rows(view);
        for (var i = 0; i < vm.Hits.Count; i++)
        {
            var title = rows[i].GetVisualDescendants().OfType<StrikeText>().Single();
            Assert.Equal(vm.Hits[i].Done, title.Struck);
            var expected = vm.Hits[i].Done ? "Brush.Text.Muted" : "Brush.Text.Primary";
            Assert.Equal(Color(view, expected), ((ISolidColorBrush)title.Foreground!).Color);
        }

        Assert.True(vm.Hits.TakeWhile(h => !h.Done).Count() == vm.Hits.Count(h => !h.Done));   // open first
    }

    [AvaloniaFact]
    public void Typing_n_in_the_query_box_does_not_open_New_task()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (vm, view) = Open(shell);

        shell.Window.KeyPress(Key.N, RawInputModifiers.None, PhysicalKey.N, "n");
        shell.Window.KeyTextInput("n");
        shell.Window.KeyRelease(Key.N, RawInputModifiers.None, PhysicalKey.N, "n");
        shell.Key(Key.D2);                                               // a digit (future zoom shortcut) too
        shell.Window.KeyTextInput("2");
        shell.Settle();

        Assert.Same(vm, shell.Vm.Overlay);
        Assert.Equal("n2", vm.Query);
        Assert.True(view.QueryBox.IsFocused);
        Assert.Equal(new NavState(AppView.Week, Today), shell.Vm.Nav);
    }

    [AvaloniaFact]
    public void A_store_change_while_open_re_runs_the_query_and_keeps_the_selection()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (vm, view) = Open(shell);
        Type(shell, "review");
        Press(shell, Key.Down);
        var selected = vm.Selected!;

        var added = shell.App.Store.Add(new TaskItem(0, "Review the search palette", string.Empty, null, null, null, 0,
            Priority.None, null, false));
        shell.Settle();
        Assert.Equal(Expected(shell, "review").Select(h => h.Row.Id), vm.Hits.Select(h => h.Id));
        Assert.Contains(vm.Hits, h => h.Id == added.Id);
        Assert.Same(selected, vm.Selected);

        shell.App.Store.Delete(selected.Id);
        shell.Settle();
        Assert.DoesNotContain(vm.Hits, h => h.Id == selected.Id);
        Assert.InRange(vm.SelectedIndex, 0, vm.Hits.Count - 1);
        Assert.Single(vm.Hits, h => h.IsSelected);

        // Closed: store changes no longer reach it. "review" has 50 hits in the demo data (Projector.Search's limit), so
        // the check uses a query well under the cap, where one more matching task would show.
        view.QueryBox.Text = "weekly review";
        shell.Settle();
        var count = vm.Hits.Count;
        Assert.InRange(count, 1, SearchPaletteViewModel.Limit - 2);
        shell.Key(Key.Escape);
        Assert.Null(shell.Vm.Overlay);
        shell.App.Store.Add(new TaskItem(0, "Weekly review once more", string.Empty, null, null, null, 0, Priority.None,
            null, false));
        shell.Settle();
        Assert.Equal(count + 1, Expected(shell, "weekly review").Count);  // an open palette would have grown
        Assert.Equal(count, vm.Hits.Count);
    }

    [AvaloniaFact]
    public void Rows_keep_the_same_inset_on_both_sides_and_the_scrollbar_sits_over_the_right_padding()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (vm, view) = Open(shell);
        Type(shell, "review");
        Assert.True(view.HitScroll.Extent.Height > view.HitScroll.Viewport.Height);   // the list scrolls

        var panel = view.Bounds;
        var row = Rows(view)[0];
        var rowLeft = row.TranslatePoint(default, view)!.Value.X;
        var rowRight = row.TranslatePoint(new Point(row.Bounds.Width, 0), view)!.Value.X;
        Assert.Equal(12, rowLeft);
        Assert.Equal(12, panel.Width - rowRight);                                       // symmetric, with the bar shown

        var bar = view.HitScroll.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);
        Assert.True(bar.IsEffectivelyVisible);
        var barTop = bar.TranslatePoint(default, view.HitScroll)!.Value;
        var barRight = bar.TranslatePoint(new Point(bar.Bounds.Width, bar.Bounds.Height), view)!.Value;
        Assert.Equal(8, bar.Bounds.Width);
        Assert.Equal(2, panel.Width - barRight.X);                                      // clear of the panel edge
        Assert.Equal(2, barRight.X - bar.Bounds.Width - rowRight);                      // … and of the rows
        Assert.Equal(6, barTop.Y);                                                      // … and of the rule above
        Assert.Equal(6, view.HitScroll.Bounds.Height - (barTop.Y + bar.Bounds.Height)); // … and of the footer
        Assert.Equal(vm.Hits.Count, Rows(view).Count);
    }

    [AvaloniaFact]
    public void Search_screenshot_over_the_week()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var (_, view) = Open(shell);
        Type(shell, "review");
        var panel = view.TranslatePoint(default, shell.Window)!.Value;
        Assert.Equal(new Point((1600 - 640) / 2.0, 150), panel);
        var path = shell.Capture("search");
        Assert.True(new FileInfo(path).Length > 10_000, path);

        // Border.dialog's ring and shadow are drawn outside the panel: nothing between it and the window may clip them.
        var dialog = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("dialog"));
        // The ring is the 1 px spread, unblurred shadow (#26262A); Border.dialog lists it last, so Avalonia (which paints
        // the shadows of one BoxShadows last-on-top, the reverse of CSS) draws it over the big drop shadow, as in the
        // reference. A clipped ring would leave the shade (#090909) there; a ring under the drop shadow would be darker
        // on the sides and missing along the bottom. All four sides are exactly the ring colour.
        BoxShadow? found = null;
        for (var i = 0; i < dialog.BoxShadow.Count; i++)
        {
            if (dialog.BoxShadow[i] is { Blur: 0, Spread: > 0, IsInset: false } shadow)
            {
                found = shadow;
            }
        }

        var ring = Assert.NotNull(found).Color;
        Assert.Equal(dialog.BoxShadow[dialog.BoxShadow.Count - 1].Color, ring);         // painted last = on top
        var frame = Pixels(shell.Window);
        int left = (int)panel.X, top = (int)panel.Y;
        int right = left + (int)view.Bounds.Width, bottom = top + (int)view.Bounds.Height;
        var midX = (left + right) / 2;
        var rightX = right - 20;                                                          // clear of the bottom corners
        Assert.Equal(ring, frame(midX, top - 1));
        Assert.Equal(ring, frame(left - 1, top + 100));
        Assert.Equal(ring, frame(right, top + 100));
        Assert.Equal(ring, frame(rightX, bottom));
        Assert.True(Luma(frame(midX, bottom + 12)) < Luma(frame(midX, top - 12)), "the drop shadow falls below the panel");
    }

    private static Func<int, int, Color> Pixels(Window window)
    {
        using var bitmap = window.CaptureRenderedFrame()!;
        var width = bitmap.PixelSize.Width;
        var bytes = new byte[width * bitmap.PixelSize.Height * 4];
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), buffer, bytes.Length, width * 4);
            System.Runtime.InteropServices.Marshal.Copy(buffer, bytes, 0, bytes.Length);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
        }

        var bgra = bitmap.Format == Avalonia.Platform.PixelFormat.Bgra8888;
        Assert.True(bgra || bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888, bitmap.Format?.ToString());
        return (x, y) =>
        {
            var i = (y * width + x) * 4;
            return bgra
                ? Avalonia.Media.Color.FromRgb(bytes[i + 2], bytes[i + 1], bytes[i])
                : Avalonia.Media.Color.FromRgb(bytes[i], bytes[i + 1], bytes[i + 2]);
        };
    }

    private static int Luma(Color c) => c.R + c.G + c.B;

    private static void Press(ShellFixture shell, Key key) => shell.Key(key);

    private static void AssertInView(SearchPaletteView view, int index)
    {
        var row = Assert.IsAssignableFrom<Control>(view.HitList.ContainerFromIndex(index));
        var top = row.TranslatePoint(default, view.HitScroll)!.Value.Y;
        Assert.InRange(top, 0, view.HitScroll.Viewport.Height - row.Bounds.Height);
    }

    private static Color Color(Control anchor, string key) =>
        anchor.TryFindResource(key, anchor.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException(key);
}
