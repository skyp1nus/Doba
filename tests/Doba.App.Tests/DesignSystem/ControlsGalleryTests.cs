using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Theme.Gallery;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.DesignSystem;

public class ControlsGalleryTests
{
    /// <summary>Renders every design-system control in its states to docs/screenshots/controls-gallery.png.</summary>
    [AvaloniaFact]
    public void Controls_gallery_renders_to_png()
    {
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false; // hover states and checks render at their end values
        try
        {
            var gallery = new ControlsGallery();
            var window = new Window { Width = 1600, Height = 1000, Content = gallery };
            window.Show();

            foreach (var control in gallery.GetLogicalDescendants().OfType<Control>())
            {
                if (control.Classes.Contains("demo-hover"))
                {
                    ((IPseudoClasses)control.Classes).Add(":pointerover");
                }
            }

            var focus = gallery.FindControl<Button>("FocusDemo");
            Assert.NotNull(focus);
            Assert.True(focus.Focus(NavigationMethod.Tab));

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            var path = Screenshots.Capture(window, "controls-gallery");
            Assert.True(new FileInfo(path).Length > 10_000);

            // Every templated control in the gallery got one of our templates (no Fluent/Simple fallback exists).
            var untemplated = gallery.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TemplatedControl>()
                .Where(t => t is not ControlsGallery && t.Template is null)
                .Select(t => t.GetType().Name)
                .Distinct()
                .ToList();
            Assert.Empty(untemplated);

            // A keyboard-focused control shows the focus ring adorner; nothing else does.
            Assert.Single(window.GetVisualDescendants().OfType<FocusRing>());
            window.Close();
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
        }
    }

    [AvaloniaFact]
    public void Focus_ring_is_shown_for_keyboard_focus_only()
    {
        var button = new Button { Content = "Today", Theme = (Avalonia.Styling.ControlTheme)Avalonia.Application.Current!.FindResource("Button.Outline")! };
        var other = new Button { Content = "Other" };
        var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { button, other } } };
        window.Show();

        button.Focus(NavigationMethod.Pointer);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.Empty(window.GetVisualDescendants().OfType<FocusRing>());

        other.Focus(NavigationMethod.Tab);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.Single(window.GetVisualDescendants().OfType<FocusRing>());
        window.Close();
    }
}
