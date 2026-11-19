using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.Core.Navigation;

namespace Doba.App.Shell;

/// <summary>One settings-sidebar tab (Appearance … Shortcuts). The six instances live as long as the sidebar.</summary>
public sealed partial class SidebarTabViewModel : ObservableObject
{
    private readonly Action<SettingsTab> _select;

    public SidebarTabViewModel(SettingsTab tab, Action<SettingsTab> select)
    {
        ArgumentNullException.ThrowIfNull(select);
        Tab = tab;
        Label = LabelOf(tab);
        _select = select;
    }

    public SettingsTab Tab { get; }

    /// <summary>Sidebar label (the page title differs for Shortcuts: "Keyboard shortcuts").</summary>
    public string Label { get; }

    /// <summary>This tab is open (<see cref="NavState.Tab"/>).</summary>
    [ObservableProperty] public partial bool IsActive { get; internal set; }

    /// <summary>The tab's 16 px icon (Theme/Icons.axaml Icon.Tab.*) for the view.</summary>
    public static IValueConverter IconOf { get; } = new FuncValueConverter<SettingsTab, Geometry?>(tab =>
        Application.Current is { } app && app.TryFindResource(IconKey(tab), out var icon) ? icon as Geometry : null);

    /// <summary>The prototype's TABS labels (design-reference.dc.html lines 1687–1694).</summary>
    public static string LabelOf(SettingsTab tab) => tab switch
    {
        SettingsTab.Appearance => "Appearance",
        SettingsTab.Calendar => "Calendar",
        SettingsTab.Tasks => "Tasks",
        SettingsTab.Windows => "Windows",
        SettingsTab.Data => "Data",
        _ => "Shortcuts",
    };

    private static string IconKey(SettingsTab tab) => tab switch
    {
        SettingsTab.Appearance => "Icon.Tab.Appearance",
        SettingsTab.Calendar => "Icon.Tab.Calendar",
        SettingsTab.Tasks => "Icon.Tab.Tasks",
        SettingsTab.Windows => "Icon.Tab.Windows",
        SettingsTab.Data => "Icon.Tab.Data",
        _ => "Icon.Tab.Shortcuts",
    };

    [RelayCommand]
    private void Select() => _select(Tab);
}
