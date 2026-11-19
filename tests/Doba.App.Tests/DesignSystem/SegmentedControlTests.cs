using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Doba.App.Tests.DesignSystem;

/// <summary>Where the segment labels of the shared SegmentedControl themes (Switches.axaml) sit in their boxes.</summary>
public class SegmentedControlTests
{
    /// <summary>
    /// The Time popover's "Any time | Set time" (SegmentedControl.Fill) is centred 1 px high to match dialog-time.png;
    /// the Settings rows (SegmentedControl) and the duration chips (SegmentedControl.Mono) stay centred, as their
    /// references have them.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("SegmentedControl", 0)]
    [InlineData("SegmentedControl.Fill", 1)]
    [InlineData("SegmentedControl.Mono", 0)]
    public void Labels_sit_at_their_measured_height_in_the_segment(string theme, double lift)
    {
        var list = new ListBox
        {
            Theme = (ControlTheme)Application.Current!.FindResource(theme)!,
            Width = 300,
            Items = { new ListBoxItem { Content = "Any time" }, new ListBoxItem { Content = "Set time" } },
            SelectedIndex = 1,
        };
        var window = new Window { Width = 400, Height = 100, Content = list };
        window.Show();
        try
        {
            window.UpdateLayout();
            foreach (var item in list.GetVisualDescendants().OfType<ListBoxItem>())
            {
                var text = item.GetVisualDescendants().OfType<TextBlock>().Single();
                var top = text.TranslatePoint(default, item)!.Value.Y;
                var centre = top + (text.Bounds.Height / 2);
                var expected = (item.Bounds.Height / 2) - lift;
                Assert.True(Math.Abs(centre - expected) <= 0.5,
                    $"{theme} '{item.Content}': label centre {centre:F2} in a {item.Bounds.Height:F0} px box, expected {expected:F2}");
            }
        }
        finally
        {
            window.Close();
        }
    }
}
