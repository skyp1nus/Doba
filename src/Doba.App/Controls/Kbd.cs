using Avalonia.Controls;

namespace Doba.App.Controls;

/// <summary>
/// A keyboard hint in Geist Mono. Looks (Theme/Controls/Inputs.axaml), chosen with a class:
/// <list type="bullet">
/// <item>(none) — the title bar's "Ctrl K": 10.5 px Brush.Text.Tertiary, 1 px Brush.Overlay.KbdBorder, radius 4, 0 5, 18 px tall;</item>
/// <item><c>muted</c> — "N" in Add task rows: Brush.Text.Muted, Brush.Border.Strong border, 20 px tall;</item>
/// <item><c>onPrimary</c> — "N" / "↵" inside the primary button: Medium, Brush.Text.OnPrimary on Brush.Overlay.OnPrimary, no border, 18 px;</item>
/// <item><c>keycap</c> — Settings key caps: 11 px Brush.Text.Bright on Brush.Bg.Card, Brush.Border.Strong with a 2 px bottom edge, radius 5, 1 7.</item>
/// </list>
/// </summary>
public class Kbd : ContentControl
{
}
