using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Dialogs;
using Doba.App.Motion;
using Doba.App.Screens.Day;
using Doba.App.Shell;
using Doba.App.Tests.Support;
using Doba.App.Theme;
using Doba.App.ViewModels;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using AppMotion = Doba.App.Motion.Motion;
using AppToggle = Doba.App.Controls.ToggleSwitch;

namespace Doba.App.Tests.MotionChecks;

/// <summary>
/// Motion spec "Мікровзаємодії" row by row (plus the calendar cell of "Оверлеї" and the time wheel): each reaction
/// animates the specified property for the specified token with the specified curve, changes no layout, and reaches its
/// end state. Durations are asserted on the declared transitions / running Animator handles; visual states are checked
/// on the animated values while the real dispatcher (render timer included) runs.
/// </summary>
public class MicroTests
{
    private static readonly Color White = Transit.Resource("Color.Text.Primary");

    // ---- 1. Checkbox --------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Checkbox_fills_with_the_accent_in_250ms_and_shows_its_mark_in_200ms()
    {
        using var stage = new Stage(() => new TaskCheckBox());
        var check = (TaskCheckBox)stage.Content;
        var box = Transit.Part<Border>(check, "PART_Box");
        var mark = Transit.Part<Icon>(check, "PART_Check");
        Transit.Is(box, Border.BackgroundProperty, AppMotion.Check, AppMotion.EaseStd);
        Transit.Is(box, Border.BorderBrushProperty, AppMotion.Check, AppMotion.EaseStd);
        Transit.Is(mark, Visual.OpacityProperty, AppMotion.CheckMark, AppMotion.EaseStd);
        Assert.Equal(Transit.Resource("Color.Border.Check"), Transit.ColorOf(box.BorderBrush));
        var bounds = box.Bounds;

        check.IsChecked = true;
        Assert.True(mark.Opacity < 1);                                                 // fading, not switched
        Assert.NotEqual(Transit.Resource("Color.Accent"), Transit.ColorOf(box.Background));
        Assert.True(Transit.Until(() => mark.Opacity == 1
            && Transit.ColorOf(box.Background) == Transit.Resource("Color.Accent")
            && Transit.ColorOf(box.BorderBrush) == Transit.Resource("Color.Accent")));
        Assert.Equal(bounds, box.Bounds);                                              // nothing re-laid out
    }

    // ---- 2. Task title ----------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Done_title_fades_its_text_and_its_always_present_strike_to_muted_in_300ms()
    {
        using var harness = new ScreenHarness();
        var vm = new TaskRowViewModel(new TaskRow(901, "Water the plants", false, Priority.None, string.Empty, string.Empty,
            string.Empty, false, false), harness.Actions, harness.Commands);
        using var stage = new Stage(() => new TaskListRow { DataContext = vm, Width = 500 }, width: 600, height: 80);
        var row = (TaskListRow)stage.Content;
        var title = row.TitleText!;
        var presenter = Transit.Presenter(Transit.Part<Button>(row, "PART_Title"));
        Transit.Is(presenter, ContentPresenter.ForegroundProperty, AppMotion.Strike, AppMotion.EaseStd);
        Assert.Equal(0, title.StrokeColor.A);                                          // the strike is there, transparent
        var muted = Transit.Resource("Color.Text.Muted");
        var bounds = title.Bounds;

        vm.Update(vm.Row with { Done = true });
        stage.Pixels();
        Assert.NotEqual(muted, Transit.ColorOf(presenter.Foreground));
        Assert.True(title.StrokeColor.A < 255);
        Assert.True(Transit.Until(() => Transit.ColorOf(presenter.Foreground) == muted && title.StrokeColor == muted));
        Assert.Same(title, row.TitleText);                                             // same text, never rebuilt
        Assert.Equal(bounds, title.Bounds);
    }

    [AvaloniaFact]
    public void Every_task_title_on_every_screen_is_a_StrikeText_whose_colour_fades_in_300ms()
    {
        using var shell = new AnimatedShell(AppView.Inbox);
        foreach (var view in new[] { AppView.Inbox, AppView.Today, AppView.Week, AppView.Month, AppView.Year, AppView.Day })
        {
            shell.Show(view, DemoData.Today);
            var titles = shell.OnScreen<StrikeText>().ToList();
            Assert.True(titles.Count > 0, $"{view}: no task titles");
            foreach (var title in titles)
            {
                var host = ColourTransitionOf(title)
                    ?? throw new Xunit.Sdk.XunitException($"{view}: title '{title.Text}' has no colour transition");
                Transit.Is(host, TextElement.ForegroundProperty, AppMotion.Strike, AppMotion.EaseStd);
            }

            // No task title is a plain TextBlock with a decoration toggled on.
            Assert.DoesNotContain(shell.OnScreen<TextBlock>(), t => t.TextDecorations is { Count: > 0 });
        }

        // The title's own Foreground transition, or the one of the Button.Title presenter it inherits from.
        static Animatable? ColourTransitionOf(Visual title)
        {
            for (Visual? v = title; v is not null && v is not Button; v = v.GetVisualParent())
            {
                if (v is Animatable a && a.Transitions?.OfType<TransitionBase>().Any(t => t.Property == TextElement.ForegroundProperty) == true)
                {
                    return a;
                }
            }

            return null;
        }
    }

    // ---- 3. New task in a list --------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void New_or_moved_task_slides_in_from_minus_8_over_360ms_and_ordinary_rows_do_not()
    {
        using var shell = new AnimatedShell(AppView.Inbox);
        var store = shell.App.Store;
        var actions = shell.Vm.Screens.Actions;
        var entrance = MotionState.Offset(0, AppMotion.NewRowY, 0);

        // Inbox list row.
        var undated = store.Add(new TaskItem(0, "Aaa new inbox task", string.Empty, null, null, null, 0, Priority.None, null, false));
        actions.MarkCreated(undated.Id);
        shell.Pump();
        var row = shell.OnScreen<TaskListRow>().Single(r => ((TaskRowViewModel)r.DataContext!).Id == undated.Id);
        Transit.Is(Animator.Running(row), entrance, MotionState.Rest, AppMotion.NewRow, AppMotion.EaseOut);
        Assert.All(shell.OnScreen<TaskListRow>().Where(r => r != row), r => Assert.Null(Animator.Running(r)));
        shell.Settle();

        // Week card.
        shell.Show(AppView.Week, DemoData.Today);
        var dated = store.Add(new TaskItem(0, "Early run", string.Empty, DemoData.Today, null, new TimeOnly(5, 0), 30, Priority.None, null, false));
        actions.MarkCreated(dated.Id);
        shell.Pump();
        var card = shell.OnScreen<TaskCard>().Single(c => ((TaskRowViewModel)c.DataContext!).Id == dated.Id);
        Transit.Is(Animator.Running(card), entrance, MotionState.Rest, AppMotion.NewRow, AppMotion.EaseOut);
        Assert.All(shell.OnScreen<TaskCard>().Where(c => c != card), c => Assert.Null(Animator.Running(c)));
        shell.Settle();

        // Day card.
        shell.Show(AppView.Day, DemoData.Today);
        var day = store.Add(new TaskItem(0, "Early stretch", string.Empty, DemoData.Today, null, new TimeOnly(5, 30), 30, Priority.None, null, false));
        actions.MarkCreated(day.Id);
        shell.Pump();
        var dayCard = shell.OnScreen<DayCard>().Single(c => ((TaskRowViewModel)c.DataContext!).Id == day.Id);
        Transit.Is(Animator.Running(dayCard), entrance, MotionState.Rest, AppMotion.NewRow, AppMotion.EaseOut);
        Assert.All(shell.OnScreen<DayCard>().Where(c => c != dayCard), c => Assert.Null(Animator.Running(c)));

        // An ordinary change (completing a task) plays no entrance.
        shell.Settle();
        actions.ToggleDone(day.Id);
        shell.Pump();
        Assert.All(shell.OnScreen<DayCard>(), c => Assert.Null(Animator.Running(c)));
    }

    // ---- 4. Tag filter ----------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Tag_filter_dims_other_tags_to_03_in_300ms_on_every_screen()
    {
        using var shell = new AnimatedShell(AppView.Week);
        var tag = shell.App.Store.All.First(t => t.Tag is not null).Tag!;
        var card = shell.OnScreen<Card>().First(c => c.DataContext is TaskRowViewModel r && r.TagLabel != "#" + tag && !c.Classes.Contains("faded"));
        Assert.Equal(1, card.Opacity);

        shell.Vm.ToggleTagFilter(tag);
        shell.Pump();
        Assert.True(card.Opacity > 0.3, "dims gradually");
        Assert.True(Transit.Until(() => Math.Abs(card.Opacity - 0.3) < 1e-6));

        var dimmed = Transit.Number("Opacity.Dimmed");
        Assert.Equal(0.3, dimmed);
        foreach (var view in new[] { AppView.Inbox, AppView.Today, AppView.Week, AppView.Month, AppView.Year, AppView.Day })
        {
            shell.Show(view, DemoData.Today);
            var faded = shell.OnScreen<Control>().Where(c => c.Classes.Contains("dimmed")).ToList();
            Assert.True(faded.Count > 0, $"{view}: nothing dimmed");
            foreach (var element in faded)
            {
                Transit.Is(element, Visual.OpacityProperty, AppMotion.Dim, AppMotion.EaseStd);
                var expected = element.Classes.Contains("faded") ? Transit.Number("Opacity.Week.PreviewDimmed") : dimmed;
                Assert.Equal(expected, element.Opacity, 6);
            }
        }

        // The next-week preview: 0.55, 0.2 when also filtered out, over the same transition.
        shell.Show(AppView.Week, DemoData.Today);
        var previews = shell.OnScreen<Card>().Where(c => c.Classes.Contains("faded")).ToList();
        Assert.NotEmpty(previews);
        Assert.All(previews, p => Assert.Equal(p.Classes.Contains("dimmed") ? 0.2 : 0.55, p.Opacity, 6));
        Assert.All(previews, p => Transit.Is(p, Visual.OpacityProperty, AppMotion.Dim, AppMotion.EaseStd));

        shell.Vm.ToggleTagFilter(tag);                                                // cleared: back to 1 / 0.55
        shell.Settle();
        Assert.True(Transit.Until(() => Math.Abs(card.Opacity - 1) < 1e-6));
    }

    // ---- 5. Progress ------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Progress_moves_by_a_left_anchored_scale_in_500ms_ease_out()
    {
        using var stage = new Stage(() => new ProgressLine { Width = 200, Value = 0.25 });
        var line = (ProgressLine)stage.Content;
        var fill = Transit.Part<Border>(line, "PART_Fill");
        var bounds = fill.Bounds;

        line.Value = 0.75;
        Transit.Is(Animator.Running(fill), new MotionState(1, 0, 0, 0.25 / 0.75, 1), MotionState.Rest, AppMotion.Progress, AppMotion.EaseOut);
        Assert.Equal(0, ElementComposition.GetElementVisual(fill)!.CenterPoint.X);  // grows from the left edge
        Assert.Equal(0.75, ((ScaleTransform)fill.RenderTransform!).ScaleX, 6);
        Assert.Equal(bounds, fill.Bounds);                                             // width never animated

        // The day headers (Week), months (Year) and Today's summary all use it.
        using var shell = new AnimatedShell(AppView.Week);
        Assert.NotEmpty(shell.OnScreen<Doba.App.Screens.Week.DayProgressLine>());
        shell.Show(AppView.Year);
        Assert.NotEmpty(shell.OnScreen<ProgressLine>());
        shell.Show(AppView.Today);
        Assert.NotEmpty(shell.OnScreen<ProgressLine>());
    }

    // ---- 6. Settings toggle -----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Toggle_track_turns_accent_in_200ms_and_the_knob_travels_16px_in_250ms_ease_out()
    {
        using var stage = new Stage(() => new AppToggle());
        var toggle = (AppToggle)stage.Content;
        var track = Transit.Part<Border>(toggle, "PART_Track");
        var knob = Transit.Part<Border>(toggle, "PART_Knob");
        Transit.Is(track, Border.BackgroundProperty, AppMotion.ToggleTrack, AppMotion.EaseStd);
        Transit.Is(knob, Border.BackgroundProperty, AppMotion.ToggleTrack, AppMotion.EaseStd);
        Assert.Equal(Transit.Resource("Color.Bg.Control"), Transit.ColorOf(track.Background));
        var bounds = knob.Bounds;

        toggle.IsChecked = true;
        Transit.Is(Animator.Running(knob), MotionState.Offset(-AppMotion.ToggleTravel, 0), MotionState.Rest, AppMotion.Toggle, AppMotion.EaseOut);
        Assert.Equal(AppMotion.ToggleTravel, knob.RenderTransform!.Value.M31, 6);     // resting place: a render transform
        Assert.Equal(bounds, knob.Bounds);                                             // never re-laid out
        Assert.NotEqual(Transit.Resource("Color.Accent"), Transit.ColorOf(track.Background));
        Assert.True(Transit.Until(() => Transit.ColorOf(track.Background) == Transit.Resource("Color.Accent")
            && Transit.ColorOf(knob.Background) == Transit.Resource("Color.Knob.On")));
    }

    // ---- 7. Segmented controls --------------------------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData("SegmentedControl")]
    [InlineData("SegmentedControl.Fill")]
    [InlineData("SegmentedControl.Mono")]
    public void Segment_background_and_text_change_in_200ms(string theme)
    {
        using var stage = new Stage(() => new ListBox
        {
            Theme = Transit.Theme(theme),
            ItemsSource = new[] { "One", "Two" },
            SelectedIndex = 0,
            Width = 200,
        });
        var list = (ListBox)stage.Content;
        var second = (ListBoxItem)list.ContainerFromIndex(1)!;
        var presenter = Transit.Presenter(second);
        Transit.Is(presenter, ContentPresenter.BackgroundProperty, AppMotion.Segment, AppMotion.EaseStd);
        Transit.Is(presenter, ContentPresenter.ForegroundProperty, AppMotion.Segment, AppMotion.EaseStd);
        var bounds = second.Bounds;

        list.SelectedIndex = 1;
        stage.Pixels();
        Assert.NotEqual(Transit.Resource("Color.Bg.Control"), (presenter.Background as ISolidColorBrush)?.Color);
        Assert.True(Transit.Until(() => (presenter.Background as ISolidColorBrush)?.Color == Transit.Resource("Color.Bg.Control")
            && Transit.ColorOf(presenter.Foreground) == Transit.Resource("Color.Text.Primary")));
        Assert.Equal(bounds, second.Bounds);
    }

    [AvaloniaFact]
    public void Settings_time_mode_and_duration_segments_use_the_segment_themes()
    {
        using var shell = new AnimatedShell(AppView.Settings);
        var settingsSegments = shell.OnScreen<ListBox>().Where(l => l is not SegmentedSwitch).ToList();
        Assert.NotEmpty(settingsSegments);
        Assert.All(settingsSegments, l => Assert.Same(Transit.Theme("SegmentedControl"), l.Theme));

        shell.Show(AppView.Week, DemoData.Today);
        shell.Vm.NewTask();
        shell.Settle();
        var view = Assert.IsType<TaskDialogView>(shell.Overlays.OverlayView);
        shell.Click(view.Anchors[1].Children[0]);                                     // Time
        shell.Settle();
        var popover = view.Anchors[1].Popover!;
        Assert.Same(Transit.Theme("SegmentedControl.Fill"), Transit.Part<ListBox>(popover, "PART_Mode").Theme);
        Assert.Same(Transit.Theme("SegmentedControl.Mono"), Transit.Part<ListBox>(popover, "PART_Duration").Theme);
    }

    [AvaloniaFact]
    public void Header_view_switch_slides_its_indicator_by_64px_steps_in_420ms_and_hides_it_outside_the_calendar()
    {
        using var stage = new Stage(() => new SegmentedSwitch { ItemsSource = new[] { "Year", "Month", "Week", "Day" }, SelectedIndex = 0 });
        var segments = (SegmentedSwitch)stage.Content;
        var indicator = Transit.Part<Border>(segments, "PART_Indicator");
        Transit.Is(indicator, Visual.OpacityProperty, AppMotion.SegColor, AppMotion.EaseStd);
        var third = (ListBoxItem)segments.ContainerFromIndex(2)!;
        Transit.Is(Transit.Presenter(third), ContentPresenter.ForegroundProperty, AppMotion.SegColor, AppMotion.EaseStd);
        Assert.Equal(AppMotion.SegStep, segments.ContainerFromIndex(1)!.Bounds.X - segments.ContainerFromIndex(0)!.Bounds.X);

        segments.SelectedIndex = 2;
        Transit.Is(Animator.Running(indicator), MotionState.Offset(-2 * AppMotion.SegStep, 0), MotionState.Rest, AppMotion.SegIndicator, AppMotion.EaseOut);
        Assert.Equal(2 * AppMotion.SegStep, ((TranslateTransform)indicator.RenderTransform!).X, 6);
        Assert.True(Transit.Until(() => Transit.ColorOf(Transit.Presenter(third).Foreground) == Transit.Resource("Color.Text.Primary")));

        Animator.Running(indicator)?.Complete();
        segments.SelectedIndex = -1;                                                   // Inbox, Today, Settings
        stage.Pixels();
        Assert.True(indicator.Opacity > 0);
        Assert.True(Transit.Until(() => indicator.Opacity == 0));

        using var shell = new AnimatedShell(AppView.Week);
        Assert.IsType<SegmentedSwitch>(shell.Window.Header.ZoomSwitch);
    }

    // ---- 8–11. Hover ------------------------------------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData("Button.Ghost", AppMotion.Hover, "Color.Overlay.Hover")]
    [InlineData("Button.Outline", AppMotion.Hover, "Color.Overlay.Hover")]
    [InlineData("Button.Dashed", AppMotion.Hover, "Color.Overlay.Hover")]
    [InlineData("Button.Menu", AppMotion.HoverFast, "Color.Overlay.Press")]
    [InlineData("Button.Caption", AppMotion.HoverFast, "Color.Overlay.Press")]
    public void Button_and_menu_item_hover_backgrounds_fade_in(string theme, int ms, string hover)
    {
        using var stage = new Stage(() => new Button { Theme = Transit.Theme(theme), Width = 80, Height = 30, Content = "x" });
        var button = (Button)stage.Content;
        var presenter = Transit.Presenter(button);
        Transit.Is(presenter, ContentPresenter.BackgroundProperty, ms, AppMotion.EaseStd);
        var target = Transit.Resource(hover);
        Assert.Equal(0x0A, Transit.Resource("Color.Overlay.Hover").A);               // rgba(255,255,255,0.04)
        Assert.Equal(0x0F, Transit.Resource("Color.Overlay.Press").A);               // rgba(255,255,255,0.06)

        stage.Hover(button);
        stage.Pixels();
        Assert.NotEqual(target, (presenter.Background as ISolidColorBrush)?.Color);
        Assert.True(Transit.Until(() => (presenter.Background as ISolidColorBrush)?.Color == target));
        stage.Leave();
        Assert.True(Transit.Until(() => (presenter.Background as ISolidColorBrush)?.Color.A is null or 0));
    }

    [AvaloniaFact]
    public void Task_card_hover_lifts_its_background_in_180ms()
    {
        using var stage = new Stage(() => new Card { Width = 120, Height = 40 });
        var card = (Card)stage.Content;
        var presenter = Transit.Presenter(card);
        Transit.Is(presenter, ContentPresenter.BackgroundProperty, AppMotion.Hover, AppMotion.EaseStd);
        Assert.Equal(Transit.Resource("Color.Bg.Card"), Transit.ColorOf(presenter.Background));

        stage.Hover(card);
        stage.Pixels();
        Assert.NotEqual(Transit.Resource("Color.Bg.CardHover"), Transit.ColorOf(presenter.Background));
        Assert.True(Transit.Until(() => Transit.ColorOf(presenter.Background) == Transit.Resource("Color.Bg.CardHover")));
    }

    [AvaloniaFact]
    public void Bar_and_schedule_block_hover_brightens_over_200ms()
    {
        using var stage = new Stage(() => new Button { Theme = Transit.Theme("Button.Bar"), Width = 160, Height = 28, Content = "Trip" });
        var bar = (Button)stage.Content;
        var brighten = Transit.Part<Border>(bar, "PART_Brighten");
        Transit.Is(brighten, Visual.OpacityProperty, AppMotion.ColorChange, AppMotion.EaseStd);
        Assert.Equal(0, brighten.Opacity);
        Assert.False(brighten.IsHitTestVisible);
        var bounds = bar.Bounds;

        stage.Hover(bar);
        stage.Pixels();
        Assert.True(brighten.Opacity < 1);
        Assert.True(Transit.Until(() => brighten.Opacity == 1));
        Assert.Equal(bounds, bar.Bounds);

        // Week bars, Day spans, Year items and schedule blocks all use Button.Bar.
        using var shell = new AnimatedShell(AppView.Week);
        Assert.Contains(shell.OnScreen<Button>(), b => b.Classes.Contains("bar") && b.Theme == Transit.Theme("Button.Bar"));
        shell.Show(AppView.Day, DemoData.Today);
        Assert.Contains(shell.OnScreen<Button>(), b => b.Classes.Contains("block") && b.Theme == Transit.Theme("Button.Bar"));
    }

    [AvaloniaFact]
    public void Day_and_month_headers_fade_to_075_on_hover_over_200ms()
    {
        using var stage = new Stage(() => new Button { Theme = Transit.Theme("Button.Header"), Width = 160, Height = 60, Content = "Mon 16" });
        var header = (Button)stage.Content;
        var presenter = Transit.Presenter(header);
        Transit.Is(presenter, Visual.OpacityProperty, AppMotion.ColorChange, AppMotion.EaseStd);

        stage.Hover(header);
        stage.Pixels();
        Assert.True(presenter.Opacity > 0.75);
        Assert.True(Transit.Until(() => Math.Abs(presenter.Opacity - Transit.Number("Opacity.HeaderHover")) < 1e-6));
        Assert.Equal(0.75, Transit.Number("Opacity.HeaderHover"));

        using var shell = new AnimatedShell(AppView.Week);
        Assert.True(shell.OnScreen<Button>().Count(b => b.Classes.Contains("day-header") && b.Theme == Transit.Theme("Button.Header")) >= 7);
        shell.Show(AppView.Year);
        Assert.Equal(12, shell.OnScreen<Button>().Count(b => b.Classes.Contains("month-header") && b.Theme == Transit.Theme("Button.Header")));
    }

    // ---- 12. Accent and transparency ---------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Accent_and_transparency_changes_cross_fade_in_300ms_ease()
    {
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = true;
        var service = new ThemeService(Application.Current!);
        try
        {
            service.ApplyAccent(Accent.Sage, animate: true);
            var tween = service.Running(ThemeService.AccentBrushKey)!;
            Assert.NotNull(tween);
            Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.AccentChange), tween.Duration);
            Assert.Same(AppMotion.EaseStd, tween.Easing);
            Assert.Equal(AccentPalette.Mist, tween.From);
            Assert.Equal(AccentPalette.Sage, tween.To);
            var half = AppMotion.EaseStd.Ease(0.5);
            Assert.Equal((byte)Math.Round(AccentPalette.Mist.R + ((AccentPalette.Sage.R - AccentPalette.Mist.R) * half)), tween.ColorAt(0.5).R);
            foreach (var key in new[] { ThemeService.Accent10BrushKey, ThemeService.Accent15BrushKey, ThemeService.Accent25BrushKey })
            {
                Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.AccentChange), service.Running(key)!.Duration);
            }

            service.ApplyChromeOpacity(40, animate: true);
            var chrome = service.Running(ThemeService.ChromeBrushKey)!;
            Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.AccentChange), chrome.Duration);
            Assert.Same(AppMotion.EaseStd, chrome.Easing);
            Assert.Equal((byte)Math.Round(0.4 * 255), chrome.To.A);
            Assert.True(Transit.Until(() => service.Running(ThemeService.ChromeBrushKey) is null
                && Transit.Resource(ThemeService.AccentBrushKey) == AccentPalette.Sage));
        }
        finally
        {
            service.Apply(AppSettings.Default);
            AppMotion.SystemAnimationsEnabled = animations;
        }
    }

    [AvaloniaFact]
    public void Settings_accent_change_reaches_the_window_through_the_cross_fade()
    {
        using var shell = new AnimatedShell(AppView.Settings);
        shell.App.Settings.Update(s => s with { Accent = Accent.Sand });

        // Read before the dispatcher runs anything: the cross-fade only ticks on the dispatcher, so this holds however
        // long the update took (no wall-clock dependence).
        Assert.NotEqual(AccentPalette.Sand, Transit.Resource(ThemeService.AccentBrushKey));   // not switched at once
        Assert.True(Transit.Until(() => Transit.Resource(ThemeService.AccentBrushKey) == AccentPalette.Sand));
    }

    // ---- 16. Calendar cell -----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Calendar_cell_hover_ring_and_selection_fill_change_in_150ms()
    {
        using var stage = new Stage(() => new Button { Theme = Transit.Theme("Button.CalendarDay"), Width = 40, Content = "16" });
        var cell = (Button)stage.Content;
        var dot = Transit.Part<Border>(cell, "PART_Dot");
        Transit.Is(dot, Border.BorderBrushProperty, AppMotion.HoverFast, AppMotion.EaseStd);
        Transit.Is(dot, Border.BackgroundProperty, AppMotion.HoverFast, AppMotion.EaseStd);
        Assert.Equal(1, dot.BorderThickness.Left);

        stage.Hover(cell);
        stage.Pixels();
        Assert.True(Transit.Until(() => (dot.BorderBrush as ISolidColorBrush)?.Color == Transit.Resource("Color.Border.Focus")));

        cell.Classes.Add("marked");
        stage.Pixels();
        Assert.NotEqual(Transit.Resource("Color.Accent"), (dot.Background as ISolidColorBrush)?.Color);
        Assert.True(Transit.Until(() => (dot.Background as ISolidColorBrush)?.Color == Transit.Resource("Color.Accent")));
    }

    // ---- Time wheel -------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Time_popover_places_its_wheels_instantly_and_a_click_scrolls_smoothly()
    {
        using var shell = new AnimatedShell(AppView.Week);
        shell.Vm.NewTask();
        shell.Settle();
        var view = Assert.IsType<TaskDialogView>(shell.Overlays.OverlayView);
        shell.Click(view.Anchors[1].Children[0]);                                     // Time
        var popover = view.Anchors[1].Popover!;
        Animator.Running(popover)?.Complete();
        Transit.Part<ListBox>(popover, "PART_Mode").SelectedIndex = 1;               // "Set time" shows the wheels
        shell.Pump();
        var wheels = popover.GetVisualDescendants().OfType<TimeWheel>().ToList();
        Assert.Equal(2, wheels.Count);
        var strips = wheels.Select(w => w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_Items")).ToList();
        Assert.All(strips, s => Assert.Null(Animator.Running(s)));                    // positioned without animation

        var hours = wheels[0];
        var row = strips[0].Children[Math.Min(hours.SelectedIndex + 2, 23)];
        shell.Click(row);
        var scroll = Animator.Running(strips[0]);
        Assert.NotNull(scroll);                                                        // smooth scroll on click
        Assert.Equal(TimeSpan.FromMilliseconds(AppMotion.Slide), scroll.Duration);
        Assert.Same(AppMotion.EaseOut, scroll.Easing);
    }
}
