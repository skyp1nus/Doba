using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Doba.App.Controls;

/// <summary>
/// Chromium paints a fixed-size glyph centred in a box at the centring offset rounded half up to a device pixel
/// (a 13 px glyph in 18 px lands at +3, a 15 px icon in a 28 px button at +7). Avalonia's ContentPresenter rounds the
/// same offset down (+2, +6), so icons in odd-slack buttons sat a pixel left of and above the design. Glyph controls
/// (<see cref="Icon"/>, <see cref="PriorityIcon"/>) shift their drawing by <see cref="Correction"/>; layout and hit
/// testing are unchanged.
/// </summary>
internal static class PixelCentring
{
    /// <summary>
    /// The render-time shift that moves <paramref name="glyph"/>, when it is the centred child of a ContentPresenter
    /// (a button's content), to where Chromium would paint it; zero otherwise.
    /// </summary>
    public static Vector Correction(Control glyph)
    {
        if (glyph.GetVisualParent() is not ContentPresenter presenter)
        {
            return default;
        }

        var scale = TopLevel.GetTopLevel(glyph)?.RenderScaling ?? 1;
        var box = glyph.Bounds;
        var margin = glyph.Margin;
        var pad = presenter.Padding;
        var border = presenter.BorderThickness;
        var dx = presenter.HorizontalContentAlignment == HorizontalAlignment.Center
            ? Shift(box.X, box.Width, margin.Left, margin.Right, presenter.Bounds.Width, pad.Left + border.Left, pad.Right + border.Right, scale)
            : 0;
        var dy = presenter.VerticalContentAlignment == VerticalAlignment.Center
            ? Shift(box.Y, box.Height, margin.Top, margin.Bottom, presenter.Bounds.Height, pad.Top + border.Top, pad.Bottom + border.Bottom, scale)
            : 0;
        return new Vector(dx, dy);
    }

    /// <summary>Chromium's snapped centring offset of <paramref name="size"/> in <paramref name="extent"/>.</summary>
    public static double Offset(double extent, double size, double scale = 1) => RoundHalfUp((extent - size) / 2, scale);

    private static double Shift(double actual, double size, double marginStart, double marginEnd, double extent,
        double insetStart, double insetEnd, double scale)
    {
        var slot = extent - insetStart - insetEnd;
        var ideal = insetStart + marginStart + Offset(slot, size + marginStart + marginEnd, scale);
        var shift = ideal - actual;

        // Only ever a rounding difference; anything larger means the presenter is not laid out as assumed.
        return Math.Abs(shift) <= 1 ? shift : 0;
    }

    private static double RoundHalfUp(double value, double scale)
    {
        var s = scale > 0 ? scale : 1;
        return Math.Floor((value * s) + 0.5) / s;
    }
}
