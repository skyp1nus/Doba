using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Doba.App.Theme;
using Doba.Core.Model;

namespace Doba.App.Tests.DesignSystem;

public class ThemeServiceTests
{
    private static ThemeService NewService() => new(Application.Current!);

    [AvaloniaFact]
    public void Defaults_match_the_design()
    {
        Assert.Equal(AccentPalette.Mist, Pixels.Resource("Brush.Accent"));
        Assert.Equal(Color.Parse("#0B0B0C"), Pixels.Resource("Color.Bg.Window"));
        Assert.Equal(Color.Parse("#0F0F10"), Pixels.Resource("Brush.Bg.Main"));
        Assert.Equal(Color.Parse("#17171A"), Pixels.Resource("Brush.Bg.Card"));
        Assert.Equal(Color.Parse("#EDEDEF"), Pixels.Resource("Brush.Text.Primary"));
        Assert.Equal(Color.Parse("#D9A066"), Pixels.Resource("Brush.Warning"));
        Assert.Equal(Color.Parse("#B8161618"), Pixels.Resource("Brush.Chrome"));
        Assert.Equal(13.5, Resource<double>("Density.TitleSize"));
    }

    [AvaloniaFact]
    public void ApplyAccent_updates_accent_brushes_and_alpha_variants()
    {
        var service = NewService();
        try
        {
            service.ApplyAccent(Accent.Sage);

            Assert.Equal(Accent.Sage, service.Accent);
            Assert.Equal(AccentPalette.Sage, Pixels.Resource("Color.Accent"));
            Assert.Equal(AccentPalette.Sage, Pixels.Resource("Brush.Accent"));
            Assert.Equal(Color.FromArgb(0x1A, 0xA3, 0xB8, 0xA0), Pixels.Resource("Brush.Accent10"));
            Assert.Equal(Color.FromArgb(0x26, 0xA3, 0xB8, 0xA0), Pixels.Resource("Brush.Accent15"));
            Assert.Equal(Color.FromArgb(0x40, 0xA3, 0xB8, 0xA0), Pixels.Resource("Brush.Accent25"));
        }
        finally
        {
            service.ApplyAccent(Accent.Mist);
        }
    }

    [AvaloniaFact]
    public void ApplyAccent_repaints_controls_already_on_screen()
    {
        var service = NewService();
        var swatch = new Border { Width = 40, Height = 40 };
        swatch.Bind(Border.BackgroundProperty, swatch.GetResourceObservable("Brush.Accent"));
        var check = new Doba.App.Controls.TaskCheckBox { IsChecked = true, Margin = new Thickness(60, 0, 0, 0) };
        var window = new Window
        {
            Width = 120, Height = 60,
            Content = new Panel { Children = { swatch, check } },
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        window.Show();
        try
        {
            Assert.True(Pixels.Near(AccentPalette.Mist, Pixels.At(window, 20, 20)));
            service.ApplyAccent(Accent.Sand);
            Assert.True(Pixels.Near(AccentPalette.Sand, Pixels.At(window, 20, 20)));

            // The checked TaskCheckBox (16 px, vertically centred at x 60..76, y 22..38) follows too: sample its fill.
            var fill = Pixels.At(window, 62, 24);
            Assert.True(Pixels.Near(AccentPalette.Sand, fill, 12), $"checkbox fill {fill}");
        }
        finally
        {
            service.ApplyAccent(Accent.Mist);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ApplyDensity_switches_every_density_resource()
    {
        var service = NewService();
        try
        {
            service.ApplyDensity(Density.Compact);
            Assert.Equal(new Thickness(4, 5), Resource<Thickness>("Density.RowPadding"));
            Assert.Equal(13.0, Resource<double>("Density.TitleSize"));
            Assert.Equal(new Thickness(11, 6), Resource<Thickness>("Density.CardPadding"));
            Assert.Equal(30.0, Resource<double>("Density.CardMinHeight"));
            Assert.Equal(3.0, Resource<double>("Density.CardGap"));
            Assert.Equal(new Thickness(12, 6), Resource<Thickness>("Density.DayCardPadding"));
            Assert.Equal(3.0, Resource<double>("Density.DayCardGap"));

            service.ApplyDensity(Density.Comfortable);
            Assert.Equal(new Thickness(4, 11), Resource<Thickness>("Density.RowPadding"));
            Assert.Equal(13.5, Resource<double>("Density.TitleSize"));
            Assert.Equal(new Thickness(11, 9), Resource<Thickness>("Density.CardPadding"));
            Assert.Equal(36.0, Resource<double>("Density.CardMinHeight"));
        }
        finally
        {
            service.ApplyDensity(Density.Comfortable);
        }
    }

    [AvaloniaFact]
    public void Density_change_relayouts_dynamic_consumers()
    {
        var service = NewService();
        var card = new Doba.App.Controls.Card { Content = new TextBlock { Text = "Laundry" } };
        var window = new Window { Width = 300, Height = 100, Content = card };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.Equal(new Thickness(11, 9), card.Padding);
            service.ApplyDensity(Density.Compact);
            window.UpdateLayout();
            Assert.Equal(new Thickness(11, 6), card.Padding);
        }
        finally
        {
            service.ApplyDensity(Density.Comfortable);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ApplyChromeOpacity_sets_the_tint_alpha()
    {
        var service = NewService();
        try
        {
            service.ApplyChromeOpacity(50);
            Assert.Equal(Color.FromArgb(128, 22, 22, 24), Pixels.Resource("Brush.Chrome"));
            service.ApplyChromeOpacity(0);
            Assert.Equal(Color.FromArgb(0, 22, 22, 24), Pixels.Resource("Brush.Chrome"));
            service.ApplyChromeOpacity(140);
            Assert.Equal(100, service.ChromeOpacity);
            Assert.Equal(Color.FromArgb(255, 22, 22, 24), Pixels.Resource("Brush.Chrome"));
        }
        finally
        {
            service.ApplyChromeOpacity(ThemeService.DefaultChromeOpacity);
        }
    }

    [AvaloniaFact]
    public void Animated_accent_change_is_instant_when_os_animations_are_off()
    {
        var service = NewService();
        var animations = Doba.App.Motion.Motion.SystemAnimationsEnabled;
        Doba.App.Motion.Motion.SystemAnimationsEnabled = false;
        try
        {
            service.ApplyAccent(Accent.Gray, animate: true);
            Assert.Equal(AccentPalette.Gray, Pixels.Resource("Brush.Accent"));
        }
        finally
        {
            Doba.App.Motion.Motion.SystemAnimationsEnabled = animations;
            service.ApplyAccent(Accent.Mist);
        }
    }

    [AvaloniaFact]
    public void Animated_accent_change_reaches_the_target()
    {
        var service = NewService();
        try
        {
            service.ApplyAccent(Accent.Sand, animate: true);
            Assert.NotEqual(AccentPalette.Sand, Pixels.Resource("Brush.Accent")); // cross-fades, not instant
            Pump.Until(() => Pixels.Resource("Brush.Accent") == AccentPalette.Sand, TimeSpan.FromSeconds(3));
            Assert.Equal(AccentPalette.Sand, Pixels.Resource("Brush.Accent"));
        }
        finally
        {
            service.ApplyAccent(Accent.Mist);
        }
    }

    private static T Resource<T>(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) && value is T t
            ? t
            : throw new KeyNotFoundException(key);
}
