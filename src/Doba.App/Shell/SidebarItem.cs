using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace Doba.App.Shell;

/// <summary>
/// One sidebar row (the prototype's <c>.nv</c> button): icon (or tag badge) · label · optional mono count. Looks:
/// the default theme (32 px section / settings tab rows, Theme/Controls/Sidebar.axaml) and <c>SidebarItem.Tag</c>
/// (30 px tag rows with the 16 px badge).
/// </summary>
/// <remarks>
/// Pseudo-classes: <c>:current</c> — the open section; text only, the background is the sliding highlight;
/// <c>:selected</c> — own Overlay.Active background (settings tab, active tag filter); <c>:rail</c> — the sidebar is
/// the 56 px rail: label and count fade out (Motion.RailLabels), the tag badge shows the initial.
/// <see cref="IsRailProperty"/> is inherited, so the sidebar sets it once on its root.
/// </remarks>
public class SidebarItem : Button
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<SidebarItem, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<double> IconStrokeWidthProperty =
        AvaloniaProperty.Register<SidebarItem, double>(nameof(IconStrokeWidth), 1.6);

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<SidebarItem, string?>(nameof(Label));

    /// <summary>Mono count on the right; null = none (Calendar, Collapse, Settings, tabs).</summary>
    public static readonly StyledProperty<string?> CountProperty =
        AvaloniaProperty.Register<SidebarItem, string?>(nameof(Count));

    /// <summary>Tag rows: the upper-case first letter shown in the rail badge.</summary>
    public static readonly StyledProperty<string?> InitialProperty =
        AvaloniaProperty.Register<SidebarItem, string?>(nameof(Initial));

    public static readonly StyledProperty<bool> IsCurrentProperty =
        AvaloniaProperty.Register<SidebarItem, bool>(nameof(IsCurrent));

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<SidebarItem, bool>(nameof(IsSelected));

    /// <summary>Inherited: true while the sidebar is the 56 px rail.</summary>
    public static readonly AttachedProperty<bool> IsRailProperty =
        AvaloniaProperty.RegisterAttached<SidebarItem, Control, bool>("IsRail", inherits: true);

    /// <summary>What the tag badge shows: "#" expanded, the initial in the rail (computed; do not set).</summary>
    public static readonly StyledProperty<string> BadgeTextProperty =
        AvaloniaProperty.Register<SidebarItem, string>(nameof(BadgeText), Hash);

    private const string Hash = "#";

    protected override Type StyleKeyOverride => typeof(SidebarItem);

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double IconStrokeWidth
    {
        get => GetValue(IconStrokeWidthProperty);
        set => SetValue(IconStrokeWidthProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public string? Initial
    {
        get => GetValue(InitialProperty);
        set => SetValue(InitialProperty, value);
    }

    public bool IsCurrent
    {
        get => GetValue(IsCurrentProperty);
        set => SetValue(IsCurrentProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool IsRail
    {
        get => GetValue(IsRailProperty);
        set => SetValue(IsRailProperty, value);
    }

    public string BadgeText
    {
        get => GetValue(BadgeTextProperty);
        private set => SetValue(BadgeTextProperty, value);
    }

    public static bool GetIsRail(Control element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(IsRailProperty);
    }

    public static void SetIsRail(Control element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsRailProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCurrentProperty)
        {
            PseudoClasses.Set(":current", IsCurrent);
        }
        else if (change.Property == IsSelectedProperty)
        {
            PseudoClasses.Set(":selected", IsSelected);
        }
        else if (change.Property == IsRailProperty || change.Property == InitialProperty)
        {
            PseudoClasses.Set(":rail", IsRail);
            BadgeText = IsRail && !string.IsNullOrEmpty(Initial) ? Initial : Hash;
        }
        else if (change.Property == LabelProperty)
        {
            // The visible label is the accessible name unless the view sets one ("Collapse sidebar").
            SetValue(AutomationProperties.NameProperty, Label, BindingPriority.Style);
        }
    }
}
