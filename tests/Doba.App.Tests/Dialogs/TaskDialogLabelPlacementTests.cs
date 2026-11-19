using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Doba.App.Tests.Support;
using Doba.Core.Navigation;

namespace Doba.App.Tests.Dialogs;

/// <summary>
/// Single-line labels of the task dialog sit on the reference's pixel rows: the pill labels, the footer hint and Cancel,
/// and the Priority / Tag popover rows (glyph, label, tick). Compared with docs/design/screens/dialog-priority.png and
/// dialog-tag.png by the last row that carries much of the ink (the baseline) inside each label's box.
/// </summary>
public class TaskDialogLabelPlacementTests
{
    public static TheoryData<string, int, string, int, int, int, int> Labels => new()
    {
        { "dialog-priority", 2, "Tomorrow pill", 530, 324, 595, 343 },
        { "dialog-priority", 2, "14:00 · 1h pill", 638, 324, 702, 343 },
        { "dialog-priority", 2, "footer hint", 498, 386, 640, 402 },
        { "dialog-priority", 2, "Cancel", 925, 385, 980, 404 },
        { "dialog-priority", 2, "No priority row", 754, 367, 830, 386 },
        { "dialog-priority", 2, "High row", 754, 463, 800, 482 },
        { "dialog-priority", 2, "High row glyph", 728, 463, 750, 482 },
        { "dialog-tag", 3, "No tag row", 830, 367, 890, 386 },
        { "dialog-tag", 3, "work row", 830, 399, 880, 418 },
        { "dialog-tag", 3, "learning row", 830, 463, 900, 482 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Labels))]
    public void Single_line_labels_sit_on_the_reference_rows(string reference, int pill, string label, int x0, int y0, int x1, int y1)
    {
        using var shell = ShellFixture.Open(AppView.Week);
        var dialog = DialogKit.Open(shell, DialogKit.DemoDraft());
        dialog.ClickPill(dialog.View.Anchors[pill]);
        shell.Settle();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = shell.Window.CaptureRenderedFrame()!;
        using var design = new Bitmap(ReferencePath(reference));

        var expected = StrongRows(design, x0, y0, x1, y1);
        var actual = StrongRows(frame, x0, y0, x1, y1);
        // The baseline row decides the placement (the x-height top can differ by a row: our stems carry a little more ink).
        Assert.True(expected.Last == actual.Last, $"{label}: ink rows {actual}, reference {expected}");
    }

    private static string ReferencePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Doba.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Doba.sln not found."), "docs", "design", "screens",
            name + ".png");
    }

    /// <summary>The first and last row of the box whose ink (|grey − background| summed over the row) is at least half
    /// of the inkiest row's: for a single line of text, the x-height top and the baseline.</summary>
    private static (int First, int Last) StrongRows(Bitmap bitmap, int x0, int y0, int x1, int y1)
    {
        var width = bitmap.PixelSize.Width;
        var stride = width * 4;
        var buffer = new byte[stride * bitmap.PixelSize.Height];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), handle.AddrOfPinnedObject(), buffer.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        double Grey(int x, int y)
        {
            var i = (y * stride) + (x * 4);
            return (buffer[i] + buffer[i + 1] + buffer[i + 2]) / 3.0;
        }

        var background = Grey(x0, y0);
        var rows = new double[y1 - y0 + 1];
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                rows[y - y0] += Math.Abs(Grey(x, y) - background);
            }
        }

        var half = rows.Max() / 2;
        var first = Array.FindIndex(rows, r => r >= half);
        var last = Array.FindLastIndex(rows, r => r >= half);
        return (y0 + first, y0 + last);
    }
}
