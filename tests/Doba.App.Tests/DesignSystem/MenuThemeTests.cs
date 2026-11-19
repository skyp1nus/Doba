using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Doba.App.Tests.DesignSystem;

/// <summary>
/// The tray icon's context menu is drawn by Avalonia as a window holding a MenuFlyoutPresenter with MenuItems and a
/// Separator. Doba has no stock theme, so these three controls need our own templates — without them the menu opens
/// with nothing in it (found on a real run: right-click on the tray icon showed nothing).
/// </summary>
public class MenuThemeTests
{
    private static (Window Window, MenuFlyoutPresenter Presenter, MenuItem[] Items, Separator Rule) Show()
    {
        var items = new[] { new MenuItem { Header = "Open Doba" }, new MenuItem { Header = "New task" }, new MenuItem { Header = "Quit" } };
        var rule = new Separator();
        var presenter = new MenuFlyoutPresenter { ItemsSource = new Control[] { items[0], items[1], rule, items[2] } };
        var window = new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = presenter };
        window.Show();
        window.UpdateLayout();
        return (window, presenter, items, rule);
    }

    [AvaloniaFact]
    public void Menu_controls_have_templates_and_take_the_popover_geometry()
    {
        var (window, presenter, items, rule) = Show();

        Assert.NotEmpty(presenter.GetVisualChildren());
        Assert.True(presenter.Bounds.Width >= 180, $"menu width {presenter.Bounds.Width}");
        Assert.All(items, item =>
        {
            Assert.NotEmpty(item.GetVisualChildren());
            Assert.Equal(32, item.Bounds.Height);
            Assert.True(item.Bounds.Width > 100, $"item width {item.Bounds.Width}");
        });
        Assert.Equal(1, rule.Bounds.Height);
        Assert.Equal(presenter.Bounds.Width - 2, rule.Bounds.Width); // the rule spans the menu inside the 1 px ring

        // ring 1 + padding 5 + 3 × 32 + rule 1 + 2 × 4 + padding 5 + ring 1
        Assert.Equal(117, presenter.Bounds.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void Menu_draws_the_popover_surface_its_ring_and_the_item_text()
    {
        var (window, presenter, items, _) = Show();
        var path = Screenshots.Capture(window, "tray-menu-content");
        Assert.True(new FileInfo(path).Length > 500);

        var w = (int)presenter.Bounds.Width;
        Assert.True(Pixels.Near(Pixels.At(window, w / 2, 0), Pixels.Resource("Color.Border.Popover")), "top ring");
        Assert.True(Pixels.Near(Pixels.At(window, w - 20, 20), Pixels.Resource("Color.Bg.Popover")), "surface");

        // "Open Doba" is drawn in Text.Primary somewhere in the first row (y 6..38).
        var text = Pixels.Resource("Color.Text.Primary");
        var found = false;
        for (var y = 10; y < 34 && !found; y++)
        {
            for (var x = 10; x < 100 && !found; x++)
            {
                found = Pixels.Near(Pixels.At(window, x, y), text, 40);
            }
        }

        Assert.True(found, "no item text was drawn");
        Assert.Equal("Open Doba", items[0].Header);
        window.Close();
    }

    [AvaloniaFact]
    public void The_selected_item_shows_the_hover_background()
    {
        var (window, presenter, items, _) = Show();
        var before = Pixels.At(window, (int)presenter.Bounds.Width - 20, 20);

        items[0].IsSelected = true;
        Assert.True(
            Pump.Until(() => !Pixels.Near(Pixels.At(window, (int)presenter.Bounds.Width - 20, 20), before, 1), TimeSpan.FromSeconds(5)),
            "the selected row did not change its background");
        window.Close();
    }
}
