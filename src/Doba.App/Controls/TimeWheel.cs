using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace Doba.App.Controls;

/// <summary>
/// The Time popover's scroll wheel (hours or minutes column): a vertical strip of <see cref="Items"/> with the
/// selected one under a highlight band. Scrolling/dragging moves <see cref="SelectedIndex"/> live; when the wheel
/// settles it raises <see cref="Committed"/> with the final index, which the dialog feeds to
/// <c>TaskDialog.CommitWheel</c>. Template: Theme/Controls/TimeWheel.axaml. Placeholder until the TimeWheel package.
/// </summary>
public class TimeWheel : TemplatedControl
{
    /// <summary>Defines the <see cref="Items"/> property.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>> ItemsProperty =
        AvaloniaProperty.Register<TimeWheel, IReadOnlyList<string>>(nameof(Items), []);

    /// <summary>Defines the <see cref="SelectedIndex"/> property (two-way by default).</summary>
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<TimeWheel, int>(nameof(SelectedIndex), 0, false, BindingMode.TwoWay);

    /// <summary>The labels shown on the wheel ("00".."23" or "00".."55"), top to bottom.</summary>
    public IReadOnlyList<string> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>Index of the item under the highlight band. Updates live while scrolling.</summary>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>Raised once when the wheel comes to rest on an index after user input (not after
    /// <see cref="ScrollToIndex"/>).</summary>
    public event EventHandler<int>? Committed;

    /// <summary>Brings <paramref name="index"/> under the band, animated (Motion tokens) or instantly. Sets
    /// <see cref="SelectedIndex"/>; does not raise <see cref="Committed"/>.</summary>
    public void ScrollToIndex(int index, bool animate)
    {
        _ = animate;
        SelectedIndex = index;
    }

    /// <summary>Raises <see cref="Committed"/>; for the implementation's settle logic.</summary>
    protected virtual void OnCommitted(int index) => Committed?.Invoke(this, index);
}
