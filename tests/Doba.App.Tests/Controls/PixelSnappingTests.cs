using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Doba.App.Controls;
using Doba.App.Tests.DesignSystem;
using Doba.Core.Model;

namespace Doba.App.Tests.Controls;

/// <summary>Shared controls snap to device pixels the way Chromium drew the reference renders.</summary>
public class PixelSnappingTests
{
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.25, 0.8)]       // 1.875 device px → 1
    [InlineData(1.5, 4.0 / 3.0)]  // 2.25 → 2
    [InlineData(2.0, 1.5)]        // 3 → 3
    public void Checkbox_ring_is_the_css_width_snapped_down_to_device_pixels(double scale, double expected) =>
        Assert.Equal(expected, TaskCheckBox.RingWidth(scale), 6);

    [Theory]
    [InlineData(381, 3.657, 376)]  // Month chip 11.5 px: baseline 381, strike row 376 (month.png)
    [InlineData(334, 3.975, 329)]  // Week card 12.5 px (week.png)
    [InlineData(818, 4.293, 813)]  // Today row 13.5 px (today.png)
    public void Strike_row_matches_the_reference_renders(double baseline, double strikePosition, double expectedTop) =>
        Assert.Equal(expectedTop, StrikeText.StrikeTop(baseline, strikePosition, 1));

    [AvaloniaFact]
    public void Open_checkbox_ring_is_one_pixel_wide_at_100_percent()
    {
        var box = new TaskCheckBox { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 40, Height = 40, Content = box };
        window.Show();
        try
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame()!;
            var ring = Pixels.Resource("Brush.Border.Check");
            var background = Pixels.Resource("Color.Bg.Window");

            // Middle of the left edge: column 0 is the ring, column 1 is already inside the box.
            Assert.True(Pixels.Near(ring, Pixels.At(frame, 0, 8), 6), $"ring {Pixels.At(frame, 0, 8)}");
            Assert.True(Pixels.Near(background, Pixels.At(frame, 1, 8), 6), $"inside {Pixels.At(frame, 1, 8)}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Priority_glyph_is_centred_in_its_18px_button_like_chromium()
    {
        // 13 px in 18 px: Chromium lands the glyph at +3 (half rounds up); the High bars start 1 px into the glyph.
        var button = new Button
        {
            Width = 18, Height = 18,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Content = new PriorityIcon { Priority = Priority.High },
        };
        var window = new Window { Width = 40, Height = 40, Content = button };
        window.Show();
        try
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame()!;
            var background = Pixels.Resource("Color.Bg.Window");

            // Bar 1 (x 1..4, y 8..12 in the glyph) → x 4..7, y 11..15 in the button.
            Assert.True(Pixels.Near(background, Pixels.At(frame, 3, 13), 6), "column left of the first bar is empty");
            Assert.False(Pixels.Near(background, Pixels.At(frame, 5, 13), 6), "first bar is drawn at +3 + 1");
            Assert.False(Pixels.Near(background, Pixels.At(frame, 13, 6), 6), "third bar (x 9..12, y 2..12) is at +3");
            Assert.True(Pixels.Near(background, Pixels.At(frame, 13, 4), 6), "row above the third bar is empty");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Glyph_outside_a_centred_presenter_is_not_shifted()
    {
        var icon = new PriorityIcon { Priority = Priority.High };
        var window = new Window
        {
            Width = 40, Height = 40,
            Content = new StackPanel { Children = { icon } },
        };
        window.Show();
        try
        {
            Assert.Equal(default, PixelCentring.Correction(icon));
        }
        finally
        {
            window.Close();
        }
    }
}
