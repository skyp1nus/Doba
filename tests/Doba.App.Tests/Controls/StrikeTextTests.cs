using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Doba.App.Controls;
using Doba.App.Tests.DesignSystem;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Controls;

public class StrikeTextTests
{
    private static readonly Color s_muted = Color.FromArgb(255, 0x6E, 0x6E, 0x76);

    private static (Window Window, StrikeText Text) Open(bool struck = false)
    {
        var text = new StrikeText { Text = "Water the plants", Struck = struck, StrikeColor = s_muted };
        var window = new Window { Width = 300, Height = 60, Content = text };
        window.Show();
        window.UpdateLayout();
        Pixels.At(window, 0, 0);
        return (window, text);
    }

    [AvaloniaFact]
    public void The_strike_fades_while_animations_are_on()
    {
        var (window, text) = Open();
        text.Struck = true;
        Assert.True(text.StrokeColor.A < 255);
        Assert.True(Pump.Until(() => text.StrokeColor == s_muted, TimeSpan.FromSeconds(2)));
        window.Close();
    }

    [AvaloniaFact]
    public void Turning_OS_animations_off_while_running_makes_the_strike_instant()
    {
        var (window, text) = Open();
        try
        {
            // The text was shown (its fade built) while animations were on; then Windows "Animation effects" goes off.
            AppMotion.SystemAnimationsEnabled = false;
            text.Struck = true;
            Assert.Equal(s_muted, text.StrokeColor);
            text.Struck = false;
            Assert.Equal(0, text.StrokeColor.A);

            // … and back on: it fades again.
            AppMotion.SystemAnimationsEnabled = true;
            text.Struck = true;
            Assert.True(text.StrokeColor.A < 255);
            Assert.True(Pump.Until(() => text.StrokeColor == s_muted, TimeSpan.FromSeconds(2)));
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = true;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Shown_while_animations_are_off_it_fades_once_they_come_back()
    {
        AppMotion.SystemAnimationsEnabled = false;
        try
        {
            var (window, text) = Open();
            text.Struck = true;
            Assert.Equal(s_muted, text.StrokeColor);

            AppMotion.SystemAnimationsEnabled = true;
            text.Struck = false;
            Assert.True(text.StrokeColor.A > 0); // fading out, not snapped
            Assert.True(Pump.Until(() => text.StrokeColor.A == 0, TimeSpan.FromSeconds(2)));
            window.Close();
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = true;
        }
    }

    [AvaloniaFact]
    public void Re_pointing_at_another_item_snaps_the_strike()
    {
        // A StrikeText whose data context changes (a recycled container) shows the new item's state at once.
        var text = new StrikeText { StrikeColor = s_muted };
        text.Bind(StrikeText.StruckProperty, Avalonia.Data.CompiledBinding.Create<Item, bool>(i => i.Done));
        text.DataContext = new Item(false);
        var window = new Window { Width = 300, Height = 60, Content = text };
        window.Show();
        window.UpdateLayout();
        Assert.Equal(0, text.StrokeColor.A);

        text.DataContext = new Item(true);
        Assert.Equal(s_muted, text.StrokeColor);
        text.DataContext = new Item(false);
        Assert.Equal(0, text.StrokeColor.A);
        window.Close();
    }

    [AvaloniaFact]
    public void A_snap_ends_a_running_fade_at_the_new_target()
    {
        var (window, text) = Open();
        text.Struck = true;
        Assert.True(text.StrokeColor.A < 255); // fading in
        text.Snapping = true;
        text.Struck = false;
        text.Snapping = false;
        Assert.Equal(0, text.StrokeColor.A);
        Pixels.At(window, 0, 0);
        Assert.Equal(0, text.StrokeColor.A);
        window.Close();
    }

    public sealed record Item(bool Done);
}
