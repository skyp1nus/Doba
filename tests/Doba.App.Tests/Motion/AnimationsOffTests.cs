using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Dialogs;
using Doba.App.Motion;
using Doba.App.ViewModels;
using Doba.Core.Navigation;
using AppMotion = Doba.App.Motion.Motion;
using AppToggle = Doba.App.Controls.ToggleSwitch;

namespace Doba.App.Tests.MotionChecks;

/// <summary>
/// Acceptance checklist item 6: "with animations disabled in Windows everything works instantly". With
/// <see cref="AppMotion.SystemAnimationsEnabled"/> false, completing a task, the tag filter, the settings toggle, a
/// segmented switch, the task dialog and a popover each reach their final state at once, with no animation running.
/// </summary>
public class AnimationsOffTests
{
    private static void NothingRuns(Visual root) =>
        Assert.All(root.GetVisualDescendants(), v => Assert.Null(Animator.Running(v)));

    [AvaloniaFact]
    public void Completing_a_task_and_the_tag_filter_are_instant()
    {
        using var shell = new AnimatedShell(AppView.Week, animations: false);
        var card = shell.OnScreen<TaskCard>().First(c => c.DataContext is TaskRowViewModel { Done: false, TagLabel.Length: > 0 });
        var row = (TaskRowViewModel)card.DataContext!;
        var box = Transit.Part<Border>(card, "PART_Box");
        var mark = Transit.Part<Avalonia.Controls.Control>(card, "PART_Check");
        var title = card.TitleText;
        var presenter = Transit.Presenter(card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_TitleButton"));

        shell.Vm.Screens.Actions.ToggleDone(row.Id);
        shell.Pump();
        Assert.Equal(1, mark.Opacity);
        Assert.Equal(Transit.Resource("Color.Accent"), Transit.ColorOf(box.Background));
        Assert.Equal(Transit.Resource("Color.Text.Muted"), Transit.ColorOf(presenter.Foreground));
        Assert.Equal(Transit.Resource("Color.Text.Muted"), title.StrokeColor);
        NothingRuns(shell.Window);

        var other = shell.OnScreen<Card>().First(c => c.DataContext is TaskRowViewModel r && r.TagLabel.Length > 0
            && r.TagLabel != row.TagLabel && !c.Classes.Contains("faded"));
        shell.Vm.ToggleTagFilter(row.TagLabel.TrimStart('#'));
        shell.Pump();
        Assert.Equal(Transit.Number("Opacity.Dimmed"), other.Opacity, 6);
        NothingRuns(shell.Window);
    }

    [AvaloniaFact]
    public void Toggle_and_segments_switch_at_once()
    {
        using (var stage = new Stage(() => new AppToggle(), animations: false))
        {
            var toggle = (AppToggle)stage.Content;
            toggle.IsChecked = true;
            stage.Pixels(); // one frame: zero-duration transitions land on the next frame
            var knob = Transit.Part<Border>(toggle, "PART_Knob");
            Assert.Null(Animator.Running(knob));
            Assert.Equal(AppMotion.ToggleTravel, knob.RenderTransform!.Value.M31, 6);
            Assert.Equal(Transit.Resource("Color.Accent"), Transit.ColorOf(Transit.Part<Border>(toggle, "PART_Track").Background));
            Assert.Equal(Transit.Resource("Color.Knob.On"), Transit.ColorOf(knob.Background));
        }

        using (var stage = new Stage(() => new SegmentedSwitch { ItemsSource = new[] { "Year", "Month", "Week", "Day" }, SelectedIndex = 0 },
                   animations: false))
        {
            var segments = (SegmentedSwitch)stage.Content;
            segments.SelectedIndex = 2;
            stage.Pixels(); // one frame: zero-duration transitions land on the next frame
            var indicator = Transit.Part<Border>(segments, "PART_Indicator");
            Assert.Null(Animator.Running(indicator));
            Assert.Equal(2 * AppMotion.SegStep, ((TranslateTransform)indicator.RenderTransform!).X, 6);
            Assert.Equal(Transit.Resource("Color.Text.Primary"),
                Transit.ColorOf(Transit.Presenter((ListBoxItem)segments.ContainerFromIndex(2)!).Foreground));
            segments.SelectedIndex = -1;
            stage.Pixels(); // one frame: zero-duration transitions land on the next frame
            Assert.Equal(0, indicator.Opacity);
        }

        using (var stage = new Stage(() => new ListBox
               {
                   Theme = Transit.Theme("SegmentedControl"),
                   ItemsSource = new[] { "Dark", "System" },
                   SelectedIndex = 0,
               }, animations: false))
        {
            var list = (ListBox)stage.Content;
            list.SelectedIndex = 1;
            stage.Pixels(); // one frame: zero-duration transitions land on the next frame
            var presenter = Transit.Presenter((ListBoxItem)list.ContainerFromIndex(1)!);
            Assert.Equal(Transit.Resource("Color.Bg.Control"), Transit.ColorOf(presenter.Background));
            Assert.Equal(Transit.Resource("Color.Text.Primary"), Transit.ColorOf(presenter.Foreground));
        }
    }

    [AvaloniaFact]
    public void Dialog_and_popover_open_and_close_at_once()
    {
        using var shell = new AnimatedShell(AppView.Week, animations: false);
        shell.Vm.NewTask();
        NothingRuns(shell.Window);
        shell.Pump();
        var view = Assert.IsType<TaskDialogView>(shell.Overlays.OverlayView);
        Assert.True(view.TitleBox.IsFocused);

        shell.Click(view.Anchors[0].Children[0]);                                       // Date
        Assert.True(view.Anchors[0].IsShowing);
        NothingRuns(shell.Window);

        shell.Key(Key.Escape);                                                           // closes the popover
        Assert.False(view.Anchors[0].IsShowing);
        shell.Key(Key.Escape);                                                           // closes the dialog
        Assert.Null(shell.Vm.Overlay);
        Assert.False(shell.Overlays.IsVisible);
        NothingRuns(shell.Window);
    }

    [AvaloniaFact]
    public void Progress_and_hover_states_apply_at_once()
    {
        using (var stage = new Stage(() => new ProgressLine { Width = 200, Value = 0.25 }, animations: false))
        {
            var line = (ProgressLine)stage.Content;
            line.Value = 0.75;
            stage.Pixels(); // one frame: zero-duration transitions land on the next frame
            var fill = Transit.Part<Border>(line, "PART_Fill");
            Assert.Null(Animator.Running(fill));
            Assert.Equal(0.75, ((ScaleTransform)fill.RenderTransform!).ScaleX, 6);
        }

        using (var stage = new Stage(() => new Card { Width = 120, Height = 40 }, animations: false))
        {
            var card = (Card)stage.Content;
            stage.Hover(card);
            stage.Pixels(); // one frame: zero-duration transitions land on the next frame
            Assert.Equal(Transit.Resource("Color.Bg.CardHover"), Transit.ColorOf(Transit.Presenter(card).Background));
        }
    }
}
