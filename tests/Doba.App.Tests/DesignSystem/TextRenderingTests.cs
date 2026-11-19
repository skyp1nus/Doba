using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace Doba.App.Tests.DesignSystem;

/// <summary>Text is drawn like the prototype's Chromium render: the embedded Geist faces, grayscale antialiasing.</summary>
public class TextRenderingTests
{
    [AvaloniaTheory]
    [InlineData("Font.Sans", 300)]
    [InlineData("Font.Sans", 400)]
    [InlineData("Font.Sans", 500)]
    [InlineData("Font.Sans", 600)]
    [InlineData("Font.Sans", 700)]
    [InlineData("Font.Mono", 400)]
    [InlineData("Font.Mono", 500)]
    public void Every_design_weight_resolves_to_its_own_embedded_face(string family, int weight)
    {
        var font = Application.Current!.TryGetResource(family, null, out var value) && value is FontFamily f
            ? f
            : throw new KeyNotFoundException(family);

        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(font, FontStyle.Normal, (FontWeight)weight), out var face));
        Assert.StartsWith(family == "Font.Mono" ? "Geist Mono" : "Geist", face.FamilyName, StringComparison.Ordinal);
        Assert.Equal((FontWeight)weight, face.Weight);
    }

    [AvaloniaFact]
    public void Windows_draw_text_with_grayscale_antialiasing()
    {
        var window = new Window
        {
            Width = 200, Height = 40,
            Content = new TextBlock { Text = "Renew passport", Margin = new Thickness(8) },
        };
        window.Show();
        try
        {
            Assert.Equal(TextRenderingMode.Antialias, TextOptions.GetTextRenderingMode(window));

            // Subpixel (LCD) text leaves coloured fringes on glyph edges; grayscale leaves only neutral greys.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame()!;
            var maxChroma = 0;
            for (var y = 8; y < 30; y++)
            {
                for (var x = 8; x < 120; x++)
                {
                    var c = Pixels.At(frame, x, y);
                    maxChroma = Math.Max(maxChroma, Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)));
                }
            }

            Assert.True(maxChroma <= 8, $"max chroma {maxChroma}");
        }
        finally
        {
            window.Close();
        }
    }
}
