using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Doba.App.Shell;
using Doba.App.Tests.Support;
using Doba.Core.Demo;
using Doba.Core.Navigation;

namespace Doba.App.Tests.Shell;

/// <summary>Text that does not fit the shell's chrome ends in an ellipsis instead of being cut or drawn over a neighbour.</summary>
public sealed class ShellOverflowTests
{
    private static double Right(Visual v, Visual root) => v.TranslatePoint(new Point(v.Bounds.Width, 0), root)!.Value.X;

    private static double Left(Visual v, Visual root) => v.TranslatePoint(default, root)!.Value.X;

    [AvaloniaFact]
    public void At_the_minimum_size_the_Day_subtitle_stops_before_the_view_switch()
    {
        using var shell = ShellFixture.Open(AppView.Day, DemoData.Today, width: 1100, height: 700);
        var header = shell.Window.Header;
        var subtitle = header.HeadingSubtitle;
        Assert.Contains("Today", subtitle.Text, StringComparison.Ordinal);

        Assert.True(Right(subtitle, shell.Window) <= Left(header.ZoomSwitch, shell.Window),
            $"subtitle ends at {Right(subtitle, shell.Window)}, the switch starts at {Left(header.ZoomSwitch, shell.Window)}");
        Assert.Equal(TextTrimming.CharacterEllipsis, subtitle.TextTrimming);
        Assert.True(subtitle.TextLayout.WidthIncludingTrailingWhitespace <= subtitle.Bounds.Width + 0.5);
    }

    [AvaloniaFact]
    public void At_the_design_size_the_header_title_block_is_as_wide_as_its_text()
    {
        using var shell = ShellFixture.Open(AppView.Day, DemoData.Today);
        var header = shell.Window.Header;

        // Nothing is trimmed where it fits, and the h1 block hugs its text (its entrance moves exactly the text).
        Assert.Equal(header.HeadingTitle.Bounds.Width + header.HeadingSubtitle.Bounds.Width + header.HeadingSubtitle.Margin.Left,
            header.Heading.Bounds.Width, 1);
        Assert.True(header.HeadingSubtitle.TextLayout.WidthIncludingTrailingWhitespace <= header.HeadingSubtitle.Bounds.Width + 0.5);
        Assert.DoesNotContain("…", string.Concat(header.HeadingSubtitle.TextLayout.TextLines.SelectMany(l => l.TextRuns).Select(r => r.Text.ToString())), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_long_tag_name_ends_in_an_ellipsis_before_its_count()
    {
        using var shell = ShellFixture.Open(AppView.Week);
        const string name = "extremely-long-tag-name-for-layout";
        shell.App.Store.AddTag(name);
        shell.Settle();

        var row = shell.Window.AppSidebar.GetVisualDescendants().OfType<SidebarItem>().Single(r => r.Label == name);
        var label = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
        var count = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Count");

        Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
        Assert.True(label.TextLayout.WidthIncludingTrailingWhitespace <= label.Bounds.Width + 0.5,
            $"label text {label.TextLayout.WidthIncludingTrailingWhitespace} in {label.Bounds.Width}");
        Assert.True(Right(label, row) <= Left(count, row));
        Assert.Equal(name, ToolTip.GetTip(row)); // the full name stays available
    }
}
