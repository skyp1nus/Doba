using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Screens.Month;
using Doba.App.Tests.Support;
using Doba.App.ViewModels;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Screens.Month;

/// <summary>Month screen hosted on its own (ScreenHarness, DemoData, clock 2026-11-15 22:26).</summary>
public class MonthViewTests
{
    private static readonly DateOnly DemoToday = new(2026, 11, 15);

    /// <summary>The month body in the 1600 × 1000 shell (main padding 16 / 20); the prototype's row formula uses 832.</summary>
    private const double BodyHeight = 836;

    private sealed class Scene : IDisposable
    {
        public Scene(AppSettings? settings = null, double height = BodyHeight, DateOnly? anchor = null,
            Action<ScreenHarness>? seed = null)
        {
            Harness = new ScreenHarness(settings);
            seed?.Invoke(Harness);
            Anchor = anchor ?? DemoToday;
            ViewModel = new MonthViewModel(Harness.Services);
            ViewModel.Show(new NavState(AppView.Month, Anchor));
            View = new MonthView { DataContext = ViewModel };
            Window = ScreenHarness.Host(View, ScreenHarness.ContentWidth, height);
            Settle();
        }

        public ScreenHarness Harness { get; }
        public DateOnly Anchor { get; }
        public MonthViewModel ViewModel { get; }
        public MonthView View { get; }
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

        /// <summary>The projection at the view's measured height.</summary>
        public MonthModel Expected() => Projector.Month(Harness.App.Store, Context, Anchor, MonthLayout.Metrics(View.Bounds.Height));

        /// <summary>Realized week rows (the Grid of each week template), top to bottom.</summary>
        public List<Grid> Weeks() =>
            View.FindControl<ItemsControl>("WeeksHost")!.GetRealizedContainers()
            .Select(c => (Grid)((ContentPresenter)c).Child!).ToList();

        /// <summary>All day-cell buttons, week by week, column by column.</summary>
        public List<Button> Cells() => View.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("cell")).ToList();

        public Button Cell(DateOnly date) => Cells().Single(b => ((MonthDayViewModel)b.DataContext!).Date == date);

        public static List<Button> Cells(Control week) => week.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("cell")).ToList();

        /// <summary>The panel holding a cell's button and its overlay (number, lanes, items).</summary>
        public static Panel CellPanel(Button cell) => (Panel)cell.GetVisualParent()!;

        public static List<Border> Chips(Button cell) =>
            CellPanel(cell).GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("chip") && b.IsEffectivelyVisible).ToList();

        public static TextBlock? More(Button cell) =>
            CellPanel(cell).GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Classes.Contains("more") && t.IsEffectivelyVisible);

        public static TextBlock DayNumber(Button cell) =>
            CellPanel(cell).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("day-num"));

        public static Button WeekLabel(Control week) =>
            week.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("week-label"));

        public static List<Border> Bars(Control week) =>
            week.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("bar")).ToList();

        public List<string> HeaderLabels() =>
            View.FindControl<ItemsControl>("WeekdayHeader")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text!).ToList();

        public void Click(Control target) => ClickAt(target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)!.Value);

        public void ClickAt(Point point)
        {
            Window.MouseMove(point);
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
            Settle();
        }

        public Color Resource(string key) => ((ISolidColorBrush)View.FindResource(View.ActualThemeVariant, key)!).Color;

        public void Dispose()
        {
            Window.Close();
            ViewModel.Dispose();
            Harness.Dispose();
        }
    }

    /// <summary>The task behind a chip (DataContext TaskRowViewModel) or a bar (DataContext MonthSpanViewModel).</summary>
    private static TaskRowViewModel Row(Control c) => c.DataContext as TaskRowViewModel ?? ((MonthSpanViewModel)c.DataContext!).Row;

    private static string Title(Control c) => Row(c).Title;

    private static Color ColorOf(IBrush? brush) => ((ISolidColorBrush)brush!).Color;

    private static TaskItem Span(string title, DateOnly from, DateOnly to, string? tag = null) =>
        new(0, title, string.Empty, from, to, null, 0, Priority.None, tag, false);

    [AvaloniaFact]
    public void Demo_month_renders_the_projection_at_the_measured_height()
    {
        using var s = new Scene();
        Assert.True(s.ViewModel.IsMeasured);
        Assert.Equal(832, s.ViewModel.Metrics.AvailableHeight, 0.5); // the body minus PrototypeBodySlack, as in monthData

        var expected = s.Expected();
        var weeks = s.Weeks();

        // November 2026 starts on a Sunday: six weeks (26 Oct – 6 Dec), each floor((832 − 26) / 6) = 134 px.
        Assert.Equal(6, weeks.Count);
        Assert.All(weeks, w => Assert.Equal(134, w.Bounds.Height, 0.5));
        Assert.Equal(["W44", "W45", "W46", "W47", "W48", "W49"], weeks.Select(w => (string)Scene.WeekLabel(w).Content!));
        Assert.Equal(["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"], s.HeaderLabels());
        Assert.Equal(MonthLayout.HeaderHeight, weeks[0].TranslatePoint(default, s.View)!.Value.Y, 0.5);

        for (var w = 0; w < weeks.Count; w++)
        {
            var e = expected.Weeks[w];
            var cells = Scene.Cells(weeks[w]);
            Assert.Equal(e.Days.Select(d => d.Date), cells.Select(c => ((MonthDayViewModel)c.DataContext!).Date));
            for (var c = 0; c < 7; c++)
            {
                var day = e.Days[c];
                Assert.Equal(day.DayNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), Scene.DayNumber(cells[c]).Text);
                Assert.Equal(day.Items.Select(i => i.Title), Scene.Chips(cells[c]).Select(Title));
                Assert.Equal(day.MoreCount > 0 ? "+" + day.MoreCount + " more" : null, Scene.More(cells[c])?.Text);
            }

            Assert.Equal(e.Spans.Select(b => b.Row.Title), Scene.Bars(weeks[w]).Select(Title));
        }

        // The design's "+N more" counts for W46 (two lanes leave room for one slot: the "+N more" line itself).
        Assert.Equal(["+4 more", "+3 more", "+3 more", "+3 more", "+3 more", "+2 more", "+10 more"],
            Scene.Cells(weeks[2]).Select(c => Scene.More(c)!.Text));
        Assert.Equal(["API migration to the new gateway", "Portfolio site redesign"], Scene.Bars(weeks[2]).Select(Title));
    }

    [AvaloniaFact]
    public void Current_week_today_and_other_months_are_marked()
    {
        using var s = new Scene();
        var weeks = s.Weeks();
        var accent = s.Resource("Brush.Accent");
        var muted = s.Resource("Brush.Text.Muted");
        var primary = s.Resource("Brush.Text.Primary");

        // W46 (9–15 Nov) contains today: label in the accent; the others muted.
        for (var w = 0; w < weeks.Count; w++)
        {
            var label = Scene.WeekLabel(weeks[w]);
            Assert.Equal(w == 2, label.Classes.Contains("current"));
            Assert.Equal(w == 2 ? accent : muted, ColorOf(label.Foreground));
        }

        // Today: accent semibold number and the accent dot; no other cell has a dot.
        var today = s.Cell(DemoToday);
        var number = Scene.DayNumber(today);
        Assert.Equal(accent, ColorOf(number.Foreground));
        Assert.Equal(FontWeight.SemiBold, number.FontWeight);
        var dots = s.View.GetVisualDescendants().OfType<Ellipse>().Where(e => e.IsEffectivelyVisible).ToList();
        var dot = Assert.Single(dots);
        Assert.Same(Scene.CellPanel(today), dot.GetVisualAncestors().OfType<Panel>().First(p => p.Children.OfType<Button>().Any()));
        Assert.Equal(accent, ColorOf(dot.Fill));

        // Other months: muted number on the faint overlay; this month: primary number, transparent cell.
        var inside = s.Cell(new DateOnly(2026, 11, 3));
        Assert.Equal(primary, ColorOf(Scene.DayNumber(inside).Foreground));
        Assert.Equal(FontWeight.Medium, Scene.DayNumber(inside).FontWeight);
        Assert.Equal(0, ColorOf(inside.Background).A);
        foreach (var date in new[] { new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 31), new DateOnly(2026, 12, 6) })
        {
            var cell = s.Cell(date);
            Assert.Equal(muted, ColorOf(Scene.DayNumber(cell).Foreground));
            Assert.Equal(s.Resource("Brush.Overlay.Faint"), ColorOf(cell.Background));
        }
    }

    [AvaloniaFact]
    public void A_five_week_month_has_taller_rows()
    {
        using var s = new Scene(anchor: new DateOnly(2026, 12, 10));
        var weeks = s.Weeks();
        Assert.Equal(5, weeks.Count);                                   // 30 Nov – 3 Jan
        Assert.All(weeks, w => Assert.Equal(161, w.Bounds.Height, 0.5)); // floor(806 / 5)
        Assert.Equal(new DateOnly(2026, 11, 30), ((MonthDayViewModel)Scene.Cells(weeks[0])[0].DataContext!).Date);
        Assert.Equal(["W49", "W50", "W51", "W52", "W53"], weeks.Select(w => (string)Scene.WeekLabel(w).Content!));
        Assert.DoesNotContain(weeks, w => Scene.WeekLabel(w).Classes.Contains("current"));
        Assert.DoesNotContain(s.View.GetVisualDescendants().OfType<Ellipse>(), e => e.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Sunday_week_start_changes_the_header_and_columns()
    {
        using var s = new Scene(new AppSettings { WeekStart = DayOfWeek.Sunday });
        Assert.Equal(["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"], s.HeaderLabels());
        var weeks = s.Weeks();
        Assert.Equal(5, weeks.Count); // 1 Nov is a Sunday: 1 Nov – 5 Dec
        Assert.All(weeks, w => Assert.Equal(161, w.Bounds.Height, 0.5));
        var first = Scene.Cells(weeks[0]);
        Assert.Equal(new DateOnly(2026, 11, 1), ((MonthDayViewModel)first[0].DataContext!).Date);
        Assert.All(Scene.Cells(weeks[2]), c => Assert.Equal(DayOfWeek.Sunday + Scene.Cells(weeks[2]).IndexOf(c), ((MonthDayViewModel)c.DataContext!).Date.DayOfWeek));

        // Today (Sunday 15 Nov) opens the third week and makes it current.
        Assert.Same(s.Cell(DemoToday), Scene.Cells(weeks[2])[0]);
        Assert.Contains("current", Scene.WeekLabel(weeks[2]).Classes);

        // Changing the setting live re-projects in place: Monday first again.
        s.Harness.App.Settings.Update(c => c with { WeekStart = DayOfWeek.Monday });
        s.Settle();
        Assert.Equal(["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"], s.HeaderLabels());
        Assert.Equal(6, s.Weeks().Count);
    }

    [AvaloniaFact]
    public void Bars_use_at_most_two_lanes_and_are_squared_at_week_boundaries()
    {
        // A span crossing W47 → W48 (Fri 20 – Tue 24 Nov), and a third span overlapping W46's two lanes.
        using var s = new Scene(seed: h =>
        {
            h.App.Store.Add(Span("Conference trip", new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24)));
            h.App.Store.Add(Span("Offsite planning", new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 12)));
        });
        var weeks = s.Weeks();
        var expected = s.Expected();
        Assert.All(expected.Weeks, w => Assert.InRange(w.LaneCount, 0, 2));
        Assert.All(s.ViewModel.Weeks.SelectMany(w => w.Spans), b => Assert.InRange(b.Lane, 0, 1));

        // W46: three overlapping spans, two lanes, so one is not shown (prototype rule).
        Assert.Equal(2, s.ViewModel.Weeks[2].LaneCount);
        Assert.Equal(2, Scene.Bars(weeks[2]).Count);
        Assert.Equal(expected.Weeks[2].Spans.Select(b => b.Row.Title), Scene.Bars(weeks[2]).Select(Title));

        var col = (weeks[0].Bounds.Width - MonthLayout.WeekLabelWidth) / 7;
        Rect BarRect(int week, string title)
        {
            var bar = Scene.Bars(weeks[week]).Single(b => Title(b) == title);
            var origin = bar.TranslatePoint(default, weeks[week])!.Value;
            return new Rect(origin, bar.Bounds.Size);
        }

        // W47: starts Friday (column 4) with a 6 px inset, runs to the week's right edge, square right end.
        var w47 = s.ViewModel.Weeks[3].Spans.Single(b => b.Row.Title == "Conference trip");
        Assert.False(w47.ClipStart);
        Assert.True(w47.ClipEnd);
        var top = MonthLayout.DayNumberHeight + (w47.Lane * MonthLayout.LaneHeight) + ((MonthLayout.LaneHeight - MonthLayout.BarHeight) / 2);
        AssertRect(new Rect(MonthLayout.WeekLabelWidth + (4 * col) + 6, top, (3 * col) - 6, MonthLayout.BarHeight), BarRect(3, "Conference trip"));
        var bar47 = Scene.Bars(weeks[3]).Single(b => Title(b) == "Conference trip");
        Assert.Equal(new CornerRadius(5, 0, 0, 5), bar47.CornerRadius);

        // W48: continues from the left edge (square left end) to Tuesday with a 6 px inset.
        var w48 = s.ViewModel.Weeks[4].Spans.Single(b => b.Row.Title == "Conference trip");
        Assert.True(w48.ClipStart);
        Assert.False(w48.ClipEnd);
        top = MonthLayout.DayNumberHeight + (w48.Lane * MonthLayout.LaneHeight) + ((MonthLayout.LaneHeight - MonthLayout.BarHeight) / 2);
        AssertRect(new Rect(MonthLayout.WeekLabelWidth, top, (2 * col) - 6, MonthLayout.BarHeight), BarRect(4, "Conference trip"));
        var bar48 = Scene.Bars(weeks[4]).Single(b => Title(b) == "Conference trip");
        Assert.Equal(new CornerRadius(0, 5, 5, 0), bar48.CornerRadius);

        // An unclipped bar keeps both rounded ends and insets; bars and chips never take clicks.
        var api = Scene.Bars(weeks[2]).Single(b => Title(b) == "API migration to the new gateway");
        Assert.Equal(new CornerRadius(5), api.CornerRadius);
        Assert.Equal(new Thickness(6, 0), api.Margin);
        Assert.False(api.GetVisualAncestors().OfType<ItemsControl>().First().IsHitTestVisible);

        // The lists start below the lanes: one lane row per lane.
        var listTop = Scene.Chips(s.Cell(new DateOnly(2026, 11, 26))).First().TranslatePoint(default, weeks[4])!.Value.Y;
        Assert.Equal(MonthLayout.DayNumberHeight + (s.ViewModel.Weeks[4].LaneCount * MonthLayout.LaneHeight) + 4, listTop, 0.5);
    }

    [AvaloniaFact]
    public void Chip_titles_and_bar_rings_are_drawn_like_the_reference()
    {
        using var s = new Scene();

        // Chip titles start 3 px below the chip top (CSS centres a 16.1 px line box in the 22 px chip; centring the
        // natural 15 px line rounded them to 4).
        var chips = s.Cells().SelectMany(Scene.Chips).ToList();
        Assert.NotEmpty(chips);
        Assert.All(chips, chip =>
        {
            var title = chip.GetVisualDescendants().OfType<StrikeText>().Single();
            Assert.Equal(3, title.TranslatePoint(default, chip)!.Value.Y, 0.5);
        });

        // Bars: the fill runs under the 1 px ring, as the CSS inset box-shadow is painted over the background.
        var bars = s.Weeks().SelectMany(Scene.Bars).ToList();
        Assert.NotEmpty(bars);
        Assert.All(bars, b => Assert.Equal(BackgroundSizing.OuterBorderEdge, b.BackgroundSizing));
    }

    [AvaloniaFact]
    public void Capacity_and_more_follow_the_reported_height()
    {
        int fullMore;
        int fullShown;
        using (var s = new Scene())
        {
            fullMore = s.ViewModel.Model!.Weeks.SelectMany(w => w.Days).Sum(d => d.MoreCount);
            fullShown = s.Cells().Sum(c => Scene.Chips(c).Count);
        }

        using var small = new Scene(height: 600);
        Assert.Equal(596, small.ViewModel.Metrics.AvailableHeight, 0.5);
        Assert.All(small.Weeks(), w => Assert.Equal(95, w.Bounds.Height, 0.5)); // floor((600 − 4 − 26) / 6)
        var smallMore = small.ViewModel.Model!.Weeks.SelectMany(w => w.Days).Sum(d => d.MoreCount);
        var smallShown = small.Cells().Sum(c => Scene.Chips(c).Count);
        Assert.True(smallMore > fullMore, $"more {smallMore} vs {fullMore}");
        Assert.True(smallShown < fullShown, $"shown {smallShown} vs {fullShown}");
        Assert.Equal(fullShown + fullMore, smallShown + smallMore);

        // W44 (no lanes): three items fit at 134 px rows, two slots at 95 px.
        var expected = small.Expected();
        var cells = small.Cells();
        var days = expected.Weeks.SelectMany(w => w.Days).ToList();
        Assert.Equal(days.Count, cells.Count);
        for (var i = 0; i < cells.Count; i++)
        {
            Assert.Equal(days[i].Items.Count, Scene.Chips(cells[i]).Count);
            Assert.Equal(days[i].MoreCount > 0 ? "+" + days[i].MoreCount + " more" : null, Scene.More(cells[i])?.Text);
        }
    }

    [AvaloniaFact]
    public void Short_rows_still_show_more_in_the_day_number_row()
    {
        // The minimum window (1100 × 700) leaves about 532 px: six 81 px weeks; W46's two lanes leave no list line.
        using var s = new Scene(height: 532);
        var w46 = s.Weeks()[2];
        Assert.Equal(2, s.ViewModel.Weeks[2].LaneCount);
        foreach (var cell in Scene.Cells(w46))
        {
            var day = (MonthDayViewModel)cell.DataContext!;
            var total = day.Items.Count + day.MoreCount;
            Assert.True(total > 0);
            Assert.True(day.MoreInHeader);
            Assert.Empty(Scene.Chips(cell));
            var more = Scene.More(cell);
            Assert.NotNull(more);
            Assert.Equal("+" + total + " more", more.Text);

            // Inside the cell, in the day-number row.
            var y = more.TranslatePoint(default, w46)!.Value.Y;
            Assert.InRange(y, 0, MonthLayout.DayNumberHeight - more.Bounds.Height);
        }

        // A week without lanes keeps its list.
        Assert.All(s.ViewModel.Weeks[0].Days, d => Assert.False(d.MoreInHeader));
    }

    [AvaloniaFact]
    public void Day_columns_snap_like_the_reference_render()
    {
        // 1275 px of days: edges at round(i × 182.14) → 182, 364, 546, 729, 911, 1093 (a uniform grid: 728, 910, 1092).
        using var s = new Scene();
        var xs = Scene.Cells(s.Weeks()[0]).Select(c => c.TranslatePoint(default, s.View)!.Value.X - MonthLayout.WeekLabelWidth).ToList();
        Assert.Equal([0, 182, 364, 546, 729, 911, 1093], xs);
    }

    [AvaloniaFact]
    public void Resizing_the_window_changes_more_in_place()
    {
        using var s = new Scene();
        var week = s.ViewModel.Weeks[4];
        var cell = week.Days[0];
        var labels = s.Cells().Select(c => Scene.More(c)?.Text).ToList();

        s.Window.Height = 600;
        s.Settle();

        Assert.Same(week, s.ViewModel.Weeks[4]);
        Assert.Same(cell, s.ViewModel.Weeks[4].Days[0]);
        Assert.Equal(596, s.ViewModel.Metrics.AvailableHeight, 0.5);
        Assert.NotEqual(labels, s.Cells().Select(c => Scene.More(c)?.Text).ToList());
        Assert.All(s.Weeks(), w => Assert.Equal(95, w.Bounds.Height, 0.5));
    }

    [AvaloniaFact]
    public void Day_cell_week_label_and_more_zoom_with_an_origin()
    {
        using var s = new Scene();
        var zooms = s.Harness.Commands.Zooms;

        // A day cell → Day view of that date, from the cell's centre.
        s.Click(s.Cell(new DateOnly(2026, 11, 19)));
        var (view, anchor, origin) = Assert.Single(zooms);
        Assert.Equal((AppView.Day, new DateOnly(2026, 11, 19)), (view, anchor));
        Assert.NotNull(origin);
        Assert.InRange(origin.Value.X, 0.5, 0.65);   // Thursday: fourth of seven columns right of the 40 px labels
        Assert.InRange(origin.Value.Y, 0.55, 0.75);  // fourth of six weeks

        // A chip is not interactive: clicking it opens its day.
        var chip = Scene.Chips(s.Cell(new DateOnly(2026, 11, 23))).First();
        s.Click(chip);
        Assert.Equal((AppView.Day, new DateOnly(2026, 11, 23)), (zooms[1].View, zooms[1].Anchor));
        Assert.Empty(s.Harness.Commands.Edits);

        // "+N more" (W46 Sunday, today) → that day.
        var more = Scene.More(s.Cell(DemoToday))!;
        s.Click(more);
        Assert.Equal((AppView.Day, DemoToday), (zooms[2].View, zooms[2].Anchor));
        Assert.InRange(zooms[2].Origin!.Value.X, 0.85, 1);
        Assert.InRange(zooms[2].Origin!.Value.Y, 0, 1);

        // W46 (current) → Week view at today; W44 → its Monday.
        var weeks = s.Weeks();
        s.Click(Scene.WeekLabel(weeks[2]));
        Assert.Equal((AppView.Week, DemoToday), (zooms[3].View, zooms[3].Anchor));
        Assert.InRange(zooms[3].Origin!.Value.X, 0, 0.05);
        Assert.InRange(zooms[3].Origin!.Value.Y, 0.3, 0.55);
        s.Click(Scene.WeekLabel(weeks[0]));
        Assert.Equal((AppView.Week, new DateOnly(2026, 10, 26)), (zooms[4].View, zooms[4].Anchor));
        Assert.All(zooms, z =>
        {
            Assert.NotNull(z.Origin);
            Assert.InRange(z.Origin.Value.X, 0, 1);
            Assert.InRange(z.Origin.Value.Y, 0, 1);
        });
    }

    [AvaloniaFact]
    public void Completing_a_task_updates_its_chip_in_place()
    {
        using var s = new Scene();
        var date = new DateOnly(2026, 11, 23);
        var cellVm = s.ViewModel.Weeks[4].Days[0];
        Assert.Equal(date, cellVm.Date);
        var item = cellVm.Items.First(i => !i.Done);
        var button = s.Cell(date);
        var chip = Scene.Chips(button).Single(c => ReferenceEquals(c.DataContext, item));
        var title = chip.GetVisualDescendants().OfType<StrikeText>().Single();
        var weekVm = s.ViewModel.Weeks[4];

        s.Harness.Actions.ToggleDone(item.Id);
        s.Settle();

        Assert.True(s.Harness.App.Store.Get(item.Id)!.Done);
        Assert.True(item.Done);
        Assert.Same(weekVm, s.ViewModel.Weeks[4]);
        Assert.Same(cellVm, s.ViewModel.Weeks[4].Days[0]);
        Assert.Same(item, s.ViewModel.Weeks[4].Days[0].Items.Single(i => i.Id == item.Id));
        Assert.Same(button, s.Cell(date));
        Assert.Same(chip, Scene.Chips(button).Single(c => ReferenceEquals(c.DataContext, item)));
        Assert.True(title.Struck);
        Assert.Contains("done", title.Classes);
        Assert.True(chip.GetVisualDescendants().OfType<TaskCheckBox>().Single().IsChecked);

        // And back.
        s.Harness.Actions.ToggleDone(item.Id);
        s.Settle();
        Assert.False(item.Done);
        Assert.Same(chip, Scene.Chips(button).Single(c => ReferenceEquals(c.DataContext, item)));
    }

    [AvaloniaFact]
    public void Moving_to_another_month_reuses_the_rows()
    {
        using var s = new Scene();
        var first = s.ViewModel.Weeks[0];
        var cell = first.Days[3];
        s.ViewModel.Show(new NavState(AppView.Month, new DateOnly(2026, 12, 1)));
        s.Settle();
        Assert.Equal(5, s.ViewModel.Weeks.Count);
        Assert.Same(first, s.ViewModel.Weeks[0]);
        Assert.Same(cell, s.ViewModel.Weeks[0].Days[3]);
        Assert.Equal(new DateOnly(2026, 12, 3), cell.Date);
        Assert.Equal(5, s.Weeks().Count);
        Assert.Equal(12, s.ViewModel.Month);
    }

    [AvaloniaFact]
    public void Tag_filter_dims_other_tags()
    {
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false; // read the opacities at their end values
        try
        {
            using var s = new Scene();
            s.Harness.FilterTag = "work";
            s.ViewModel.RefreshIfActive();
            s.Settle();
            var chips = s.Cells().SelectMany(Scene.Chips).ToList();
            var bars = s.Weeks().SelectMany(Scene.Bars).ToList();
            Assert.Contains(chips, c => Row(c).Dimmed);
            Assert.Contains(chips, c => !Row(c).Dimmed);
            Assert.NotEmpty(bars);
            Assert.All(chips.Concat(bars), c =>
            {
                Assert.Equal(Row(c).TagLabel != "#work", Row(c).Dimmed);
                Assert.Equal(Row(c).Dimmed ? 0.3 : 1, c.Opacity, 3);
            });

            // Clearing the filter restores full opacity on the same chips.
            s.Harness.FilterTag = null;
            s.ViewModel.RefreshIfActive();
            s.Settle();
            Assert.All(chips.Concat(bars), c => Assert.Equal(1, c.Opacity, 3));
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
        }
    }

    [AvaloniaFact]
    public void Screenshot()
    {
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false;
        try
        {
            using var s = new Scene();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            var path = Screenshots.Capture(s.Window, "month-content");
            Assert.True(new FileInfo(path).Length > 10_000);
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
        }
    }

    private static void AssertRect(Rect expected, Rect actual)
    {
        Assert.Equal(expected.X, actual.X, 1.01);
        Assert.Equal(expected.Y, actual.Y, 1.01);
        Assert.Equal(expected.Width, actual.Width, 1.01);
        Assert.Equal(expected.Height, actual.Height, 1.01);
    }
}
