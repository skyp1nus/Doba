using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Motion;
using Doba.App.Tests.DesignSystem;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Controls;

public class SegmentedSwitchTests
{
    private static (Window Window, SegmentedSwitch Segments, Border Indicator) Open(int selected)
    {
        var segments = new SegmentedSwitch { SelectedIndex = selected, ItemsSource = new[] { "Year", "Month", "Week", "Day" } };
        var window = new Window { Width = 400, Height = 100, Content = segments };
        window.Show();
        window.UpdateLayout();
        Pixels.At(window, 0, 0);
        var indicator = segments.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
        return (window, segments, indicator);
    }

    [AvaloniaFact]
    public void Coming_back_from_no_level_places_the_indicator_without_sliding()
    {
        // Inbox → calendar (Week): the switch had no level; the prototype builds a fresh switch, so the indicator does not
        // travel from Year to Week — it is placed under Week and only fades in.
        var (window, segments, indicator) = Open(-1);
        Assert.Equal(0, ((TranslateTransform)indicator.RenderTransform!).X, 1);

        segments.SelectedIndex = 2;
        Assert.False(Animator.IsAnimating(indicator));
        Assert.Equal(2 * AppMotion.SegStep, ((TranslateTransform)indicator.RenderTransform!).X, 1);
        Assert.True(Pump.Until(() => indicator.Opacity == 1, TimeSpan.FromSeconds(2))); // fades in where it is
        Assert.Equal(2 * AppMotion.SegStep, ((TranslateTransform)indicator.RenderTransform!).X, 1);

        // Between levels it still slides.
        segments.SelectedIndex = 1;
        Assert.True(Animator.IsAnimating(indicator));
        Animator.Running(indicator)!.Complete();

        // Settings and back to Month: placed again, no slide.
        segments.SelectedIndex = -1;
        segments.SelectedIndex = 1;
        Assert.False(Animator.IsAnimating(indicator));
        Assert.Equal(AppMotion.SegStep, ((TranslateTransform)indicator.RenderTransform!).X, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void Leaving_mid_slide_and_coming_back_ends_the_running_slide()
    {
        var (window, segments, indicator) = Open(0);
        segments.SelectedIndex = 3;
        Assert.True(Animator.IsAnimating(indicator));

        segments.SelectedIndex = -1;
        segments.SelectedIndex = 2;
        Assert.False(Animator.IsAnimating(indicator));
        Assert.Equal(2 * AppMotion.SegStep, ((TranslateTransform)indicator.RenderTransform!).X, 1);
        window.Close();
    }
}
