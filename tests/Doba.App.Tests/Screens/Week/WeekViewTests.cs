using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Screens.Week;
using Doba.App.Tests.Support;
using Doba.App.ViewModels;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Screens.Week;

/// <summary>Week screen hosted on its own (ScreenHarness, DemoData, clock 2026-11-15 22:26).</summary>
public class WeekViewTests
{
    private static readonly DateOnly DemoToday = new(2026, 11, 15);

    private sealed class Scene : IDisposable
    {
        public Scene(AppSettings? settings = null, double width = ScreenHarness.ContentWidth, double height = ScreenHarness.ContentHeight,
            DateOnly? anchor = null)
        {
            Harness = new ScreenHarness(settings);
            ViewModel = new WeekViewModel(Harness.Services);
            ViewModel.Show(new NavState(AppView.Week, anchor ?? DemoToday));
            View = new WeekView { DataContext = ViewModel };
            Window = ScreenHarness.Host(View, width, height);
            Settle();
        }

        public ScreenHarness Harness { get; }
        public WeekViewModel ViewModel { get; }
        public WeekView View { get; }
        public Window Window { get; }

        /// <summary>Runs posted work and layout until the measured re-projection has been applied.</summary>
        public void Settle()
        {
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Window.UpdateLayout();
            }
        }

        public ProjectionContext Context => new(Harness.App.DayClock.Today, Harness.App.DayClock.Now,
            Harness.App.Settings.Current.WeekStart, Harness.FilterTag);

        /// <summary>The projection at the view's measured size (row 2 with its own metrics).</summary>
        public WeekModel Expected()
        {
            var first = Projector.Week(Harness.App.Store, Context, DemoToday, ViewModel.MetricsFor(0));
            var second = Projector.Week(Harness.App.Store, Context, DemoToday, ViewModel.MetricsFor(1));
            return first with { Rows = [first.Rows[0], second.Rows[1]] };
        }

        public List<WeekRowView> Rows() => View.GetVisualDescendants().OfType<WeekRowView>().ToList();

        /// <summary>Realized cells of a row, in column order.</summary>
        public List<Panel> Cells(int row) =>
            Rows()[row].FindControl<ItemsControl>("CellsHost")!.GetRealizedContainers()
            .Select(c => (Panel)((ContentPresenter)c).Child!).ToList();

        public static List<TaskCard> Cards(Control cell) =>
            cell.GetVisualDescendants().OfType<TaskCard>().Where(c => c.IsEffectivelyVisible).ToList();

        public static Button? More(Control cell) =>
            cell.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Classes.Contains("more") && b.IsEffectivelyVisible);

        public static Button Header(Control cell) =>
            cell.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("day-header"));

        public List<SpanBarButton> Bars(int row) =>
            Rows()[row].GetVisualDescendants().OfType<SpanBarButton>().ToList();

        public int ShownCards() => View.GetVisualDescendants().OfType<TaskCard>().Count(c => c.IsEffectivelyVisible);

        public void Click(Control target)
        {
            var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)!.Value;
            Window.MouseMove(centre);
            Window.MouseDown(centre, MouseButton.Left);
            Window.MouseUp(centre, MouseButton.Left);
            Settle();
        }

        public void Dispose()
        {
            Window.Close();
            ViewModel.Dispose();
            Harness.Dispose();
        }
    }

    [AvaloniaFact]
    public void Demo_week_renders_the_projection_at_the_measured_size()
    {
        using var s = new Scene();
        Assert.True(s.ViewModel.IsMeasured);

        // Row content heights as laid out: 416 − 12 padding, 416 − 1 border − 14 padding.
        Assert.Equal(404, s.ViewModel.MetricsFor(0).RowHeight, 0.5);
        Assert.Equal(401, s.ViewModel.MetricsFor(1).RowHeight, 0.5);

        var expected = s.Expected();
        for (var r = 0; r < 2; r++)
        {
            var cells = s.Cells(r);
            Assert.Equal(4, cells.Count);
            for (var c = 0; c < 4; c++)
            {
                var e = expected.Rows[r].Cells[c];
                Assert.Equal(e.Tasks.Select(t => t.Title), Scene.Cards(cells[c]).Select(k => ((TaskRowViewModel)k.DataContext!).Title));
                var more = Scene.More(cells[c]);
                Assert.Equal(e.MoreCount > 0, more is not null);
                if (more is not null)
                {
                    Assert.Equal("+" + e.MoreCount + " more", more.Content);
                }
            }

            Assert.Equal(expected.Rows[r].Spans.Select(b => b.Row.Title), s.Bars(r).Select(b => ((TaskRowViewModel)b.DataContext!).Title));
        }

        // The same cards and "+N more" as the design (docs/design/screens/week.png).
        int[] perCell = [.. Enumerable.Range(0, 2).SelectMany(r => s.Cells(r).Select(c => Scene.Cards(c).Count))];
        Assert.Equal([4, 3, 3, 3, 3, 2, 4, 2], perCell);
        Assert.Equal("+2 more", Scene.More(s.Cells(1)[2])!.Content);
        Assert.Equal(["API migration to the new gateway", "Portfolio site redesign"],
            s.Bars(0).Select(b => ((TaskRowViewModel)b.DataContext!).Title));
        Assert.Equal(["Portfolio site redesign"], s.Bars(1).Select(b => ((TaskRowViewModel)b.DataContext!).Title));
        var portfolio = s.Bars(0)[1];
        Assert.True(portfolio.ClipEnd);
        Assert.False(portfolio.ClipStart);
        Assert.True(s.Bars(1)[0].ClipStart);

        // Bars sit in their columns and lanes: 28 px high, centred in 36 px lanes under the 86 px header, 8 px in
        // from unclipped ends (prototype: margin 0 8px, grid-column c / span n, grid-row lane + 2).
        var col = s.Rows()[0].Bounds.Width / 4;
        Rect BarRect(int row, int i)
        {
            var b = s.Bars(row)[i].Bar;
            var origin = b.TranslatePoint(default, s.Rows()[row])!.Value;
            return new Rect(origin, b.Bounds.Size);
        }

        AssertRect(new Rect(8, 90, (3 * col) - 16, 28), BarRect(0, 0));          // API migration: Mon–Wed, lane 0
        AssertRect(new Rect((2 * col) + 8, 126, (2 * col) - 8, 28), BarRect(0, 1)); // Portfolio: Wed→, lane 1
        AssertRect(new Rect(0, 90, (2 * col) - 8, 28), BarRect(1, 0));             // ←Portfolio: Fri–Sat, lane 0

        // Today and the preview cell.
        Assert.True(((WeekCellViewModel)s.Cells(1)[2].DataContext!).IsToday);
        var preview = (WeekCellViewModel)s.Cells(1)[3].DataContext!;
        Assert.True(preview.IsPreview);
        Assert.Equal(new DateOnly(2026, 11, 16), preview.Date);
        Assert.All(Scene.Cards(s.Cells(1)[3]), c => Assert.Equal(0.55, c.FindControl<Card>("PART_Card")!.Opacity, 3));
    }

    [AvaloniaFact]
    public void A_shorter_window_shows_fewer_cards_and_a_larger_more_count()
    {
        int full, fullMore;
        using (var s = new Scene())
        {
            full = s.ShownCards();
            fullMore = s.ViewModel.Model!.Rows.SelectMany(r => r.Cells).Sum(c => c.MoreCount);
        }

        using var small = new Scene(height: 560);
        Assert.Equal(268, small.ViewModel.MetricsFor(0).RowHeight, 0.5);
        Assert.True(small.ShownCards() < full);
        var smallMore = small.ViewModel.Model!.Rows.SelectMany(r => r.Cells).Sum(c => c.MoreCount);
        Assert.True(smallMore > fullMore);
        Assert.Equal(full + fullMore, small.ShownCards() + smallMore);

        // The visible "+N more" links show the projection's counts.
        var expected = small.Expected();
        for (var r = 0; r < 2; r++)
        {
            var cells = small.Cells(r);
            for (var c = 0; c < 4; c++)
            {
                Assert.Equal(expected.Rows[r].Cells[c].Tasks.Count, Scene.Cards(cells[c]).Count);
                var n = expected.Rows[r].Cells[c].MoreCount;
                Assert.Equal(n > 0 ? "+" + n + " more" : null, Scene.More(cells[c])?.Content);
            }
        }
    }

    [AvaloniaFact]
    public void Resizing_re_projects_in_place()
    {
        using var s = new Scene();
        var cellVm = s.ViewModel.Row2.Cells[2];
        var first = cellVm.Tasks[0];
        s.Window.Height = 560;
        s.Settle();
        Assert.Same(cellVm, s.ViewModel.Row2.Cells[2]);
        Assert.Same(first, s.ViewModel.Row2.Cells[2].Tasks[0]);
        Assert.Equal(268, s.ViewModel.MetricsFor(0).RowHeight, 0.5);
    }

    [AvaloniaFact]
    public void Toggling_a_card_updates_it_in_place()
    {
        using var s = new Scene();
        var cell = s.Cells(1)[1]; // Sat 14 Nov: "Take the bike in for a brake repair" (open)
        var card = Scene.Cards(cell).First(c => !((TaskRowViewModel)c.DataContext!).Done);
        var row = (TaskRowViewModel)card.DataContext!;
        var cellVm = (WeekCellViewModel)cell.DataContext!;
        var progress = cellVm.Progress;

        s.Click(card.GetVisualDescendants().OfType<TaskCheckBox>().Single());

        Assert.True(s.Harness.App.Store.Get(row.Id)!.Done);
        Assert.True(row.Done);
        Assert.Same(row, card.DataContext);
        Assert.Same(card, Scene.Cards(s.Cells(1)[1]).Single(c => ReferenceEquals(c.DataContext, row)));
        Assert.Same(row, s.ViewModel.Row2.Cells[1].Tasks.Single(t => t.Id == row.Id));
        Assert.Same(cellVm, s.ViewModel.Row2.Cells[1]);
        Assert.True(cellVm.Progress > progress);
        Assert.True(card.TitleText.Struck);
        Assert.Contains("done", card.FindControl<Button>("PART_TitleButton")!.Classes);
    }

    [AvaloniaFact]
    public void Clicking_a_bar_toggles_its_task_in_place()
    {
        using var s = new Scene();
        var bar = s.Bars(0)[1]; // Portfolio site redesign (open)
        var row = (TaskRowViewModel)bar.DataContext!;
        Assert.False(row.Done);
        s.Click(bar.Bar);
        Assert.True(s.Harness.App.Store.Get(row.Id)!.Done);
        Assert.True(row.Done);
        Assert.Same(bar, s.Bars(0)[1]);
        Assert.Contains("done", bar.Bar.Classes);
    }

    [AvaloniaFact]
    public void Priority_icon_cycles_and_title_opens_the_editor()
    {
        using var s = new Scene();
        var card = Scene.Cards(s.Cells(0)[3]).First(c => ((TaskRowViewModel)c.DataContext!).HasPriority);
        var row = (TaskRowViewModel)card.DataContext!;
        var before = row.Priority;
        var priority = card.GetVisualDescendants().OfType<PriorityIcon>().Single().GetVisualParent()!;
        s.Click((Control)priority.GetVisualAncestors().OfType<Button>().First());
        Assert.Equal(before.Next(), s.Harness.App.Store.Get(row.Id)!.Priority);

        s.Click(card.FindControl<Button>("PART_TitleButton")!);
        Assert.Equal([row.Id], s.Harness.Commands.Edits);
    }

    [AvaloniaFact]
    public void Day_header_and_more_zoom_into_the_day_from_their_centre()
    {
        using var s = new Scene();
        var wednesday = s.Cells(0)[2];
        s.Click(Scene.Header(wednesday));
        var (view, anchor, origin) = Assert.Single(s.Harness.Commands.Zooms);
        Assert.Equal(AppView.Day, view);
        Assert.Equal(new DateOnly(2026, 11, 11), anchor);
        Assert.NotNull(origin);
        Assert.InRange(origin.Value.X, 0.5, 0.75); // third of four columns
        Assert.InRange(origin.Value.Y, 0, 0.15);   // the header sits at the top

        s.Click(Scene.More(s.Cells(1)[2])!);
        Assert.Equal(2, s.Harness.Commands.Zooms.Count);
        var more = s.Harness.Commands.Zooms[1];
        Assert.Equal((AppView.Day, DemoToday), (more.View, more.Anchor));
        Assert.InRange(more.Origin!.Value.X, 0.5, 0.75);
        Assert.InRange(more.Origin!.Value.Y, 0.5, 1);
    }

    [AvaloniaFact]
    public void Sunday_week_start_renders_sunday_first()
    {
        using var s = new Scene(new AppSettings { WeekStart = DayOfWeek.Sunday });
        var row1 = s.ViewModel.Row1.Cells.Select(c => c.Weekday).ToList();
        var row2 = s.ViewModel.Row2.Cells.Select(c => c.Weekday).ToList();
        Assert.Equal(["SUN", "MON", "TUE", "WED"], row1);
        Assert.Equal(["THU", "FRI", "SAT", "SUN"], row2);
        Assert.Equal(DemoToday, s.ViewModel.WeekStart);
        Assert.Equal(DemoToday, ((WeekCellViewModel)s.Cells(0)[0].DataContext!).Date);
        Assert.True(((WeekCellViewModel)s.Cells(0)[0].DataContext!).IsToday);
        Assert.Equal(new DateOnly(2026, 11, 22), s.ViewModel.Row2.Cells[3].Date);
        Assert.Equal("SUN", Scene.Header(s.Cells(0)[0]).GetVisualDescendants().OfType<TextBlock>().First().Text);
    }

    [AvaloniaFact]
    public void Compact_density_fits_more_cards()
    {
        int comfortable;
        using (var s = new Scene(height: 640))
        {
            comfortable = s.ShownCards();
        }

        using var compact = new Scene(new AppSettings { Density = Density.Compact }, height: 640);
        Assert.True(compact.ShownCards() > comfortable, $"compact {compact.ShownCards()} vs comfortable {comfortable}");
    }

    [AvaloniaFact]
    public void Tag_filter_dims_other_cards()
    {
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false; // read the opacities at their end values
        try
        {
            using var s = new Scene();
            s.Harness.FilterTag = "work";
            s.ViewModel.RefreshIfActive();
            s.Settle();
            var cards = s.View.GetVisualDescendants().OfType<TaskCard>().ToList();
            Assert.Contains(cards, c => ((TaskRowViewModel)c.DataContext!).Dimmed);
            Assert.All(cards, c =>
            {
                var dimmed = ((TaskRowViewModel)c.DataContext!).Dimmed;
                var expected = c.Faded ? (dimmed ? 0.2 : 0.55) : (dimmed ? 0.3 : 1);
                Assert.Equal(expected, c.FindControl<Card>("PART_Card")!.Opacity, 3);
            });
            Assert.All(s.Bars(0).Concat(s.Bars(1)), b =>
                Assert.Equal(((TaskRowViewModel)b.DataContext!).Dimmed ? 0.3 : 1, b.Bar.Opacity, 3));
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
        }
    }

    [AvaloniaFact]
    public void Screenshot_comfortable() => Capture(null, "week-content");

    [AvaloniaFact]
    public void Screenshot_compact() => Capture(new AppSettings { Density = Density.Compact }, "week-compact-content");

    private static void AssertRect(Rect expected, Rect actual)
    {
        Assert.Equal(expected.X, actual.X, 1.01);
        Assert.Equal(expected.Y, actual.Y, 1.01);
        Assert.Equal(expected.Width, actual.Width, 1.01);
        Assert.Equal(expected.Height, actual.Height, 1.01);
    }

    private static void Capture(AppSettings? settings, string name)
    {
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false;
        try
        {
            using var s = new Scene(settings);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            var path = Screenshots.Capture(s.Window, name);
            Assert.True(new FileInfo(path).Length > 10_000);
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
        }
    }
}
