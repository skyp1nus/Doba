using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Doba.App.Controls;
using Doba.App.Motion;

namespace Doba.App.Screens.Week;

/// <summary>
/// The week day header's <see cref="ProgressLine"/>. Cells are reused when the week changes, so the line would sweep
/// (500 ms) from the previous week's value to the new day's; when <see cref="Date"/> changes (bound after
/// <c>Value</c>, which the cell view-model updates first) the fill jumps to its new value instead. Styled as a
/// <see cref="ProgressLine"/>.
/// </summary>
public sealed class DayProgressLine : ProgressLine
{
    public static readonly StyledProperty<DateOnly> DateProperty =
        AvaloniaProperty.Register<DayProgressLine, DateOnly>(nameof(Date));

    private Visual? _fill;

    /// <summary>The day the line belongs to.</summary>
    public DateOnly Date
    {
        get => GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ProgressLine);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _fill = e.NameScope.Find<Control>("PART_Fill");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DateProperty && _fill is { } fill)
        {
            Animator.Running(fill)?.Complete();
        }
    }
}
