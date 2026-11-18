using CommunityToolkit.Mvvm.ComponentModel;

namespace Doba.App.Shell;

/// <summary>
/// The app sidebar (sections, tags with counts, collapse) and the settings sidebar (tabs). Renders the Core sidebar
/// projection; every action goes through <see cref="IShellNavigation"/>. Placeholder until the Sidebar package.
/// </summary>
public sealed partial class SidebarViewModel : ObservableObject
{
    public SidebarViewModel(AppServices app, IShellNavigation shell)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(shell);
        App = app;
        Shell = shell;
    }

    /// <summary>The composition root (store, settings, clock).</summary>
    public AppServices App { get; }

    /// <summary>Where sidebar clicks go.</summary>
    public IShellNavigation Shell { get; }

    /// <summary>Re-projects counts and selection. The shell calls it after navigation, filter and store changes.</summary>
    public void Refresh() =>
        OnPropertyChanged(string.Empty); // placeholder: "everything may have changed" until the Sidebar package projects
}
