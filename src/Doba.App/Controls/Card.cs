using Avalonia.Controls;

namespace Doba.App.Controls;

/// <summary>
/// The task card surface (.card): Brush.Bg.Card, radius 8, hover Brush.Bg.CardHover (180 ms `ease`).
/// Padding/min height come from density: week cards use <c>Padding="{DynamicResource Density.CardPadding}"</c> and
/// <c>MinHeight="{DynamicResource Density.CardMinHeight}"</c>; day cards <c>Density.DayCardPadding</c>.
/// Template: Theme/Controls/Surfaces.axaml.
/// </summary>
public class Card : ContentControl
{
}
