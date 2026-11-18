using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Doba.App.Screens.Year;
using Doba.App.Tests.DesignSystem;
using Doba.App.Tests.Support;
using Doba.App.ViewModels;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Screens.Year;

/// <summary>Year screen hosted on its own (ScreenHarness, DemoData, clock 2026-11-15 22:26).</summary>
public class YearViewTests
{
    private static readonly DateOnly DemoToday = new(2026, 11, 15);

    private sealed class Scene : IDisposable
    {
        public Scene(string? filter = null, double height = ScreenHarness.ContentHeight)
        {
            Harness = new ScreenHarness { FilterTag = filter };
            ViewModel = new YearViewModel(Harness.Services);
            ViewModel.Show(new NavState(AppView.Year, DemoToday));
            View = new YearView { DataContext = ViewModel };
            Window = ScreenHarness.Host(View, ScreenHarness.ContentWidth, height);
        }

        public ScreenHarness Harness { get; }
        public YearViewModel ViewModel { get; }
        public YearView View { get; }
        public Window Window { get; }

        public YearModel Expected() => Projector.Year(Harness.App.Store,
            new ProjectionContext(Harness.App.DayClock.Today, Harness.App.DayClock.Now, Harness.App.Settings.Current.WeekStart, Harness.FilterTag),
            2026);

        public List<Button> Buttons(string cls) =>
            View.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains(cls) && b.IsEffectivelyVisible).ToList();

        public Button ItemButton(long id) => Buttons("item").Single(b => b.DataContext is TaskRowViewModel r && r.Id == id);

        public Button Header(int month) => Buttons("month-header").Single(b => b.DataContext is YearMonthViewModel m && m.Month == month);

        public void Click(Control target)
        {
            var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)!.Value;
            Window.MouseMove(centre);
            Window.MouseDown(centre, MouseButton.Left);
            Window.MouseUp(centre, MouseButton.Left);
            Window.UpdateLayout();
        }

        public void Dispose()
        {
            Window.Close();
            ViewModel.Dispose();
            Harness.Dispose();
        }
    }

    [AvaloniaFact]
    public void Shows_twelve_months_with_the_projections_stats_rows_and_more_counts()
    {
        using var s = new Scene();
        var expected = s.Expected();

        Assert.Equal(2026, s.ViewModel.Year);
        Assert.Equal(12, s.ViewModel.Months.Count);
        Assert.Equal(12, s.Buttons("month-header").Count);
        for (var i = 0; i < 12; i++)
        {
            var e = expected.Months[i];
            var m = s.ViewModel.Months[i];
            Assert.Equal(i + 1, m.Month);
            Assert.Equal(e.Name, m.Name);
            Assert.Equal(e.Stat, m.Stat);
            Assert.Equal(e.Progress, m.Progress);
            Assert.Equal(e.IsCurrent, m.IsCurrent);
            Assert.Equal(e.IsPast, m.IsPast);
            Assert.Equal(e.MoreCount, m.MoreCount);
            Assert.Equal(e.ZoomTarget, m.ZoomTarget);
            Assert.Equal(e.Rows, m.Rows.Select(r => r.Row));
        }

        // Rendered: one item button per row, a "+N more" link only where the projection has more.
        Assert.Equal(expected.Months.Sum(m => m.Rows.Count), s.Buttons("item").Count);
        var more = s.Buttons("more");
        Assert.Equal(expected.Months.Count(m => m.MoreCount > 0), more.Count);
        foreach (var link in more)
        {
            var month = (YearMonthViewModel)link.DataContext!;
            Assert.Equal("+" + month.MoreCount + " more", link.Content);
        }

        // The demo year: November is current with "+3 more"; stat text as in the prototype.
        var november = s.ViewModel.Months[10];
        Assert.True(november.IsCurrent);
        Assert.Equal(3, november.MoreCount);
        Assert.Equal(DemoToday, november.ZoomTarget);
        Assert.Equal(new DateOnly(2026, 12, 1), s.ViewModel.Months[11].ZoomTarget);
        Assert.Equal("NOW", s.Header(11).GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Last(t => t.Classes.Contains("stat")).Text);
        Assert.DoesNotContain(s.Header(12).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "NOW" && t.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Grid_is_four_by_three_with_the_prototypes_cell_borders_and_paddings()
    {
        using var s = new Scene();
        var cells = s.ViewModel.Months.Select(m => (m, Header: s.Header(m.Month))).ToList();
        var width = ScreenHarness.ContentWidth / 4;
        var height = ScreenHarness.ContentHeight / 3;
        foreach (var (m, header) in cells)
        {
            var col = (m.Month - 1) % 4;
            var row = (m.Month - 1) / 4;
            var origin = header.TranslatePoint(default, s.View)!.Value;
            // Tolerance 1 px: layout rounding of the 328.75 px columns / 277.33 px rows.
            Assert.Equal(col * width + (col == 0 ? 0 : 1 + 22), origin.X, 1.0);
            Assert.Equal(row * height + (row == 0 ? 6 : 1 + 18), origin.Y, 1.0);
            var right = col * width + width - (col == 3 ? 0 : 22);
            Assert.Equal(right, origin.X + header.Bounds.Width, 1.0);
            var cell = header.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("cell"));
            Assert.Equal(new Thickness(col == 0 ? 0 : 1, row == 0 ? 0 : 1, 0, 0), cell.BorderThickness);
            Assert.Equal(new Thickness(col == 0 ? 0 : 22, row == 0 ? 6 : 18, col == 3 ? 0 : 22, 12), cell.Padding);
        }
    }

    [AvaloniaFact]
    public void Track_edges_snap_like_the_reference_render()
    {
        // 1315 × 836 (the content area at 1600 × 1000): column edges round(i × 328.75) → 329, 658 (657.5 rounds up),
        // 986; row edges floor(j × 278.67) → 278, 557. A uniform grid would put the fourth column at 987.
        using var s = new Scene(height: 836);
        double[] xs = [.. Enumerable.Range(0, 4).Select(c => s.Header(c + 1).GetVisualAncestors().OfType<Border>()
            .First(b => b.Classes.Contains("cell")).TranslatePoint(default, s.View)!.Value.X)];
        double[] ys = [.. Enumerable.Range(0, 3).Select(r => s.Header((r * 4) + 1).GetVisualAncestors().OfType<Border>()
            .First(b => b.Classes.Contains("cell")).TranslatePoint(default, s.View)!.Value.Y)];
        Assert.Equal([0, 329, 658, 986], xs);
        Assert.Equal([0, 278, 557], ys);
    }

    [AvaloniaFact]
    public void Clicking_an_item_toggles_it_in_place()
    {
        using var s = new Scene();
        var november = s.ViewModel.Months[10];
        var months = s.ViewModel.Months.ToList();
        var span = november.Rows.First(r => r is { IsMultiDay: true, Done: false });
        var rows = november.Rows.ToList();
        var button = s.ItemButton(span.Id);

        s.Click(button);

        Assert.True(s.Harness.App.Store.Get(span.Id)!.Done);
        Assert.True(span.Done);
        Assert.Equal(months, s.ViewModel.Months);                 // same month instances
        Assert.Equal(rows, november.Rows);                         // same row instances
        Assert.Same(button, s.ItemButton(span.Id));                // same visual
        Assert.Contains("done", button.Classes);
        Assert.Equal(s.Expected().Months[10].Rows, november.Rows.Select(r => r.Row));

        s.Click(button);
        Assert.False(span.Done);
        Assert.DoesNotContain("done", button.Classes);
        Assert.Same(button, s.ItemButton(span.Id));
    }

    [AvaloniaFact]
    public void Completing_an_upcoming_task_updates_the_stat_and_progress_and_keeps_other_rows()
    {
        using var s = new Scene();
        var december = s.ViewModel.Months[11];
        var before = december.Stat;
        var single = december.Rows.First(r => !r.IsMultiDay);
        var others = december.Rows.Where(r => r != single).ToList();

        s.Click(s.ItemButton(single.Id));

        var expected = s.Expected().Months[11];
        Assert.NotEqual(before, december.Stat);
        Assert.Equal(expected.Stat, december.Stat);
        Assert.Equal(expected.Progress, december.Progress);
        Assert.True(december.Progress > 0);
        Assert.Same(december, s.ViewModel.Months[11]);
        Assert.Equal(expected.Rows, december.Rows.Select(r => r.Row));
        foreach (var other in others)
        {
            Assert.Contains(other, december.Rows); // the rows that stay are the same instances
        }
    }

    [AvaloniaFact]
    public void Month_header_and_more_link_zoom_into_the_month_from_the_click()
    {
        using var s = new Scene();
        var header = s.Header(11);

        s.Click(header);

        var zoom = Assert.Single(s.Harness.Commands.Zooms);
        Assert.Equal(AppView.Month, zoom.View);
        Assert.Equal(DemoToday, zoom.Anchor);
        var origin = Assert.NotNull(zoom.Origin);
        var centre = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), s.View)!.Value;
        Assert.Equal(centre.X / s.View.Bounds.Width, origin.X, 3);
        Assert.Equal(centre.Y / s.View.Bounds.Height, origin.Y, 3);
        Assert.InRange(origin.X, 0.5, 0.75);   // third column
        Assert.InRange(origin.Y, 0.66, 1.0);   // third row

        s.Click(s.Header(3));
        Assert.Equal((AppView.Month, new DateOnly(2026, 3, 1)), (s.Harness.Commands.Zooms[1].View, s.Harness.Commands.Zooms[1].Anchor));
        Assert.InRange(s.Harness.Commands.Zooms[1].Origin!.Value.X, 0.5, 0.75);
        Assert.InRange(s.Harness.Commands.Zooms[1].Origin!.Value.Y, 0, 0.34);

        var more = Assert.Single(s.Buttons("more"));
        s.Click(more);
        Assert.Equal(3, s.Harness.Commands.Zooms.Count);
        var last = s.Harness.Commands.Zooms[2];
        Assert.Equal((AppView.Month, DemoToday), (last.View, last.Anchor));
        var moreCentre = more.TranslatePoint(new Point(more.Bounds.Width / 2, more.Bounds.Height / 2), s.View)!.Value;
        Assert.Equal(moreCentre.X / s.View.Bounds.Width, last.Origin!.Value.X, 3);
        Assert.Equal(moreCentre.Y / s.View.Bounds.Height, last.Origin!.Value.Y, 3);
    }

    [AvaloniaFact]
    public void Keyboard_activation_zooms_without_an_origin()
    {
        using var s = new Scene();
        var header = s.Header(11);
        header.Focus(NavigationMethod.Tab);
        s.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        s.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        var zoom = Assert.Single(s.Harness.Commands.Zooms);
        Assert.Equal((AppView.Month, DemoToday, (Point?)null), zoom);

        // A pointer press that did not end in a click does not make the next keyboard activation look like one.
        var march = s.Header(3);
        var centre = march.TranslatePoint(new Point(march.Bounds.Width / 2, march.Bounds.Height / 2), s.Window)!.Value;
        s.Window.MouseDown(centre, MouseButton.Left);
        s.Window.MouseMove(new Point(2, 2));
        s.Window.MouseUp(new Point(2, 2), MouseButton.Left);
        march.Focus(NavigationMethod.Tab);
        s.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        s.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.Equal(2, s.Harness.Commands.Zooms.Count);
        Assert.Null(s.Harness.Commands.Zooms[1].Origin);
    }

    [AvaloniaFact]
    public void Completing_a_key_item_keeps_its_strike_and_fades_it_in()
    {
        using var s = new Scene();
        var span = s.ViewModel.Months[10].Rows.First(r => r is { IsMultiDay: true, Done: false }); // stays listed when done
        var button = s.ItemButton(span.Id);
        var title = button.GetVisualDescendants().OfType<Doba.App.Controls.StrikeText>().Single();
        Assert.False(title.Struck);
        Assert.Equal(0, title.StrokeColor.A);                 // present, transparent

        s.Click(button);
        Assert.True(title.Struck);
        Assert.Same(title, button.GetVisualDescendants().OfType<Doba.App.Controls.StrikeText>().Single());
        Assert.Contains(title.TextDecorations!, d => d.Location == TextDecorationLocation.Strikethrough);
    }

    [AvaloniaFact]
    public void Tag_filter_dims_items_of_other_tags()
    {
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false; // zero-length transitions: opacity settles on the next clock tick
        try
        {
            using var s = new Scene();
            var buttons = s.Buttons("item");
            Assert.All(buttons, b => Assert.Equal(1.0, b.Opacity));

            s.Harness.FilterTag = "work";
            s.ViewModel.RefreshIfActive();

            var rows = s.ViewModel.Months.SelectMany(m => m.Rows).ToList();
            Assert.Contains(rows, r => r.Dimmed);
            Assert.Contains(rows, r => !r.Dimmed);
            Assert.Equal(buttons, s.Buttons("item")); // filtering changes properties, not the visual tree
            foreach (var b in buttons)
            {
                var row = (TaskRowViewModel)b.DataContext!;
                Assert.Equal(row.TagLabel != "#work", row.Dimmed);
                Assert.Equal(row.Dimmed, b.Classes.Contains("dimmed"));
            }

            Assert.True(Pump.Until(() => buttons.All(b => Math.Abs(b.Opacity - Expected(b)) < 0.001), TimeSpan.FromSeconds(2)));
            Assert.Contains(buttons, b => Math.Abs(b.Opacity - 0.3) < 0.001);

            s.Harness.FilterTag = null;
            s.ViewModel.RefreshIfActive();
            Assert.True(Pump.Until(() => buttons.All(b => Math.Abs(b.Opacity - 1.0) < 0.001), TimeSpan.FromSeconds(2)));
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
        }

        static double Expected(Button b) => ((TaskRowViewModel)b.DataContext!).Dimmed ? 0.3 : 1.0;
    }

    [AvaloniaFact]
    public void Hidden_screen_does_not_refresh_and_show_reprojects_another_year()
    {
        using var s = new Scene();
        var november = s.ViewModel.Months[10];
        var span = november.Rows.First(r => r is { IsMultiDay: true, Done: false });
        s.ViewModel.Hide();
        s.Harness.App.Store.ToggleDone(span.Id);
        Assert.False(span.Done);

        s.ViewModel.Show(new NavState(AppView.Year, new DateOnly(2027, 3, 1)));
        Assert.Equal(2027, s.ViewModel.Year);
        Assert.Same(november, s.ViewModel.Months[10]);
        Assert.False(s.ViewModel.Months[10].IsCurrent);
        Assert.All(s.ViewModel.Months, m => Assert.False(m.IsPast));
    }

    [AvaloniaFact]
    public void Screenshot_year_content()
    {
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false;
        try
        {
            using var s = new Scene();
            // The shell's main panel colour behind the content, as in docs/design/screens/year.png.
            Assert.True(s.Window.TryFindResource("Brush.Bg.Main", s.Window.ActualThemeVariant, out var panel));
            s.Window.Background = (IBrush)panel!;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            var path = Screenshots.Capture(s.Window, "year-content");
            Assert.True(new FileInfo(path).Length > 1000);
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
        }
    }
}
