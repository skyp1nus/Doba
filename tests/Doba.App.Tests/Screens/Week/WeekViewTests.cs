using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Motion;
using Doba.App.Screens.Week;
using Avalonia.Media;
using Doba.App.Tests.Support;
using Doba.App.Theme;
using Doba.App.ViewModels;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using Doba.Core.Time;
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

        /// <summary>The projection for another anchor at the view's measured size.</summary>
        public WeekModel Expected2(DateOnly anchor) => Projector.Week(Harness.App.Store, Context, anchor, ViewModel.MetricsFor(0));

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

        // Both rows get what the 12 + 1 + 14 px between them leaves, split equally (flex 1 1 0 after the padding):
        // floor((832 − 27) / 2) = 402 each, so one projection serves both rows.
        Assert.Equal(402, s.ViewModel.MetricsFor(0).RowHeight, 0.5);
        Assert.Equal(402, s.ViewModel.MetricsFor(1).RowHeight, 0.5);
        Assert.Equal(402 + 27, s.Rows()[1].TranslatePoint(default, s.View)!.Value.Y, 0.5);

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
    public void Bar_content_and_card_titles_sit_where_the_reference_draws_them()
    {
        foreach (var density in new[] { Density.Comfortable, Density.Compact })
        {
            using var s = new Scene(new AppSettings { Density = density });

            // Bars: padding 0 10 measured from the outer edge (the CSS ring is an inset shadow and takes no space), so
            // the checkbox starts 10 px in and the meta ends 10 px before the right edge.
            foreach (var bar in s.Bars(0).Concat(s.Bars(1)))
            {
                var b = bar.Bar;
                var check = b.GetVisualDescendants().OfType<TaskCheckBox>().Single();
                Assert.Equal(10, check.TranslatePoint(default, b)!.Value.X, 0.5);
                var meta = b.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("meta-sm"));
                Assert.Equal(b.Bounds.Width - 10, meta.TranslatePoint(new Point(meta.Bounds.Width, 0), b)!.Value.X, 0.5);
            }

            // Card titles: the 18 px line block is lifted 1 px (Space.Week.CardTitle), the card itself is unchanged.
            var cards = Enumerable.Range(0, 2).SelectMany(r => s.Cells(r)).SelectMany(Scene.Cards).ToList();
            Assert.NotEmpty(cards);
            Assert.All(cards, c =>
            {
                var card = c.FindControl<Card>("PART_Card")!;
                Assert.Equal(card.Padding.Top - 1, c.TitleText.TranslatePoint(default, card)!.Value.Y, 0.5);
            });
        }
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
        Assert.Equal(266, small.ViewModel.MetricsFor(0).RowHeight, 0.5); // floor((560 − 27) / 2)
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
        Assert.Equal(266, s.ViewModel.MetricsFor(0).RowHeight, 0.5);
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
    public void The_day_changing_at_midnight_moves_today()
    {
        using var s = new Scene();
        var sunday = s.ViewModel.Row2.Cells[2];
        Assert.True(sunday.IsToday);

        // 22:26 → 00:26 on Monday 16 Nov: the shown week stays (its anchor is the 15th), Sunday is no longer today and
        // Monday is the (never "today") next-week preview.
        ((FixedTimeProvider)s.Harness.App.Clock).Advance(TimeSpan.FromHours(2));
        s.Harness.App.DayClock.Check();
        s.Settle();

        Assert.Same(sunday, s.ViewModel.Row2.Cells[2]);
        Assert.False(sunday.IsToday);
        Assert.DoesNotContain(s.ViewModel.Row1.Cells.Concat(s.ViewModel.Row2.Cells), c => c.IsToday);
        Assert.True(s.ViewModel.Row2.Cells[3].IsPreview);
    }

    [AvaloniaFact]
    public void Adding_and_deleting_a_task_updates_its_cell_in_place()
    {
        using var s = new Scene();
        var wednesday = s.ViewModel.Row1.Cells[2];
        Assert.Equal(new DateOnly(2026, 11, 11), wednesday.Date);
        var shown = wednesday.Tasks.ToList();
        var total = shown.Count + wednesday.MoreCount;

        // Timed 06:00: sorts first, so it is shown and the last card moves behind "+N more".
        var added = s.Harness.App.Store.Add(new TaskItem(0, "Early call", string.Empty, wednesday.Date, null,
            new TimeOnly(6, 0), 30, Priority.None, null, false));
        s.Settle();

        Assert.Same(wednesday, s.ViewModel.Row1.Cells[2]);
        Assert.Equal(added.Id, wednesday.Tasks[0].Id);
        Assert.Equal(total + 1, wednesday.Tasks.Count + wednesday.MoreCount);
        Assert.All(wednesday.Tasks.Skip(1), t => Assert.Contains(t, shown)); // the others are the same instances
        Assert.Contains(Scene.Cards(s.Cells(0)[2]), c => ((TaskRowViewModel)c.DataContext!).Id == added.Id);

        s.Harness.App.Store.Delete(added.Id);
        s.Settle();
        Assert.Same(wednesday, s.ViewModel.Row1.Cells[2]);
        Assert.Equal(shown, wednesday.Tasks);
        Assert.Equal(total, wednesday.Tasks.Count + wednesday.MoreCount);
    }

    [AvaloniaFact]
    public void Hidden_screen_does_not_refresh_and_show_moves_the_same_cells_to_another_week()
    {
        using var s = new Scene();
        var monday = s.ViewModel.Row1.Cells[0];
        var card = s.ViewModel.Row1.Cells.Concat(s.ViewModel.Row2.Cells).SelectMany(c => c.Tasks).First(t => !t.Done);
        s.ViewModel.Hide();
        s.Harness.App.Store.ToggleDone(card.Id);
        s.Settle();
        Assert.False(card.Done); // not re-projected while hidden

        s.ViewModel.Show(new NavState(AppView.Week, DemoToday.AddDays(7)));
        s.Settle();
        Assert.Equal(new DateOnly(2026, 11, 16), s.ViewModel.WeekStart);
        Assert.Same(monday, s.ViewModel.Row1.Cells[0]);  // cells are keyed by column
        Assert.Equal(new DateOnly(2026, 11, 16), monday.Date);
        Assert.Equal("16", monday.DayNumber);
        Assert.Equal(new DateOnly(2026, 11, 23), s.ViewModel.Row2.Cells[3].Date);
        Assert.Equal(s.Expected2(DemoToday.AddDays(7)).Rows[0].Cells[0].Tasks.Select(t => t.Id), monday.Tasks.Select(t => t.Id));
    }

    [AvaloniaFact]
    public void Switching_density_at_runtime_refits_the_cards()
    {
        using var s = new Scene(height: 640);
        var comfortable = s.ShownCards();
        s.Harness.App.Settings.Update(c => c with { Density = Density.Compact });
        new ThemeService(Application.Current!).Apply(s.Harness.App.Settings.Current);
        s.Settle();
        try
        {
            Assert.True(s.ShownCards() > comfortable, $"compact {s.ShownCards()} vs comfortable {comfortable}");
        }
        finally
        {
            new ThemeService(Application.Current!).Apply(AppSettings.Default);
        }
    }

    [AvaloniaFact]
    public void Tab_visits_headers_then_bars_then_cards_and_keys_activate_them()
    {
        using var s = new Scene();
        var cells = s.Cells(0);
        var focus = TopLevel.GetTopLevel(s.Window)!.FocusManager!;
        Scene.Header(cells[0]).Focus(NavigationMethod.Tab);
        var order = new List<IInputElement?> { focus.GetFocusedElement() };
        for (var i = 0; i < 7; i++)
        {
            s.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            s.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            order.Add(focus.GetFocusedElement());
        }

        Assert.Equal(cells.Select(c => (IInputElement)Scene.Header(c)), order.Take(4));
        Assert.Equal(s.Bars(0).Select(b => (IInputElement)b.Bar), order.Skip(4).Take(2));
        var firstCard = Scene.Cards(cells[0])[0];
        Assert.Same(firstCard, ((Visual)order[6]!).GetVisualAncestors().OfType<TaskCard>().FirstOrDefault());

        // Enter on a focused header zooms into its day with no origin (keyboard: the transition's default).
        Scene.Header(cells[2]).Focus(NavigationMethod.Tab);
        s.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        s.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        s.Settle();
        var zoom = Assert.Single(s.Harness.Commands.Zooms);
        Assert.Equal((AppView.Day, new DateOnly(2026, 11, 11), (Point?)null), zoom);

        // Space on a focused card title completes the task instead of opening the editor.
        var card = Scene.Cards(s.Cells(1)[1]).First(c => !((TaskRowViewModel)c.DataContext!).Done);
        var row = (TaskRowViewModel)card.DataContext!;
        card.FindControl<Button>("PART_TitleButton")!.Focus(NavigationMethod.Tab);
        s.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        s.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        s.Settle();
        Assert.True(s.Harness.App.Store.Get(row.Id)!.Done);
        Assert.Empty(s.Harness.Commands.Edits);
    }

    [AvaloniaFact]
    public void A_new_task_plays_its_entrance_once_and_never_leaves_it_pending()
    {
        using var s = new Scene();
        var store = s.Harness.App.Store;
        var actions = s.Harness.Actions;

        // Shown card: consumed by the card when it appears.
        var shown = store.Add(new TaskItem(0, "Early call", string.Empty, new DateOnly(2026, 11, 11), null,
            new TimeOnly(6, 0), 30, Priority.None, null, false));
        actions.MarkCreated(shown.Id);
        s.Settle();
        Assert.Null(actions.PendingEntranceId);
        var card = Scene.Cards(s.Cells(0)[2]).Single(c => ((TaskRowViewModel)c.DataContext!).Id == shown.Id);
        Assert.Equal(AppMotion.SystemAnimationsEnabled, Animator.IsAnimating(card));

        // Behind "+N more" on Sunday (untimed, sorts last): no visual appears, and the request is dropped.
        var hidden = store.Add(new TaskItem(0, "Zz later", string.Empty, DemoToday, null, null, 0, Priority.None, null, false));
        actions.MarkCreated(hidden.Id);
        s.Settle();
        Assert.DoesNotContain(s.ViewModel.Row2.Cells[2].Tasks, t => t.Id == hidden.Id);
        Assert.Null(actions.PendingEntranceId);

        // A multi-day task gets a bar, which consumes it.
        var span = store.Add(new TaskItem(0, "Workshop", string.Empty, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 10),
            null, 0, Priority.None, null, false));
        actions.MarkCreated(span.Id);
        s.Settle();
        var bar = s.Bars(0).Single(b => ((TaskRowViewModel)b.DataContext!).Id == span.Id);
        Assert.Null(actions.PendingEntranceId);
        Assert.Equal(AppMotion.SystemAnimationsEnabled, Animator.IsAnimating(bar));

        // A task outside the shown week keeps its entrance for the screen that shows it.
        var inbox = store.Add(new TaskItem(0, "Someday", string.Empty, null, null, null, 0, Priority.None, null, false));
        actions.MarkCreated(inbox.Id);
        s.Settle();
        Assert.Equal(inbox.Id, actions.PendingEntranceId);
    }

    [AvaloniaFact]
    public void Narrow_columns_break_titles_between_words_only()
    {
        // The 1100 × 700 window's content area (1600 × 1000 → 1315 × 832): 203 px columns.
        using var s = new Scene(width: 815, height: 532);
        var cards = s.View.GetVisualDescendants().OfType<TaskCard>().ToList();
        var titles = cards.Select(c => c.TitleText).ToList();
        Assert.NotEmpty(titles);
        Assert.All(titles, t => Assert.Equal(TextWrapping.WrapWithOverflow, t.TextWrapping));

        // Where tag + time would leave the title under 80 px the tag is dropped, and the title gets the room.
        var tagged = cards.Where(c => ((TaskRowViewModel)c.DataContext!).HasTag).ToList();
        Assert.Contains(tagged, c => !c.ShowTag);
        Assert.All(cards, c => Assert.True(c.TitleText.Bounds.Width >= WeekLayout.MinTitleWidth - 1 || !((TaskRowViewModel)c.DataContext!).HasTag,
            $"{((TaskRowViewModel)c.DataContext!).Title}: {c.TitleText.Bounds.Width}"));
        var dentist = titles.FirstOrDefault(t => t.Text!.StartsWith("Dentist", StringComparison.Ordinal));
        if (dentist?.TextLayout is { } layout)
        {
            // The first line ends at a word boundary (or is the whole word, trimmed), never inside "Dentist".
            var first = layout.TextLines[0];
            var length = first.Length;
            Assert.True(length >= "Dentist".Length, $"first line has {length} characters");
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
