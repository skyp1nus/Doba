using Doba.App.Screens;
using Doba.App.Shell;

namespace Doba.App.Dialogs.Search;

/// <summary>
/// The Ctrl+K search palette: query box and hits from the Core search projection; picking a hit always opens
/// that task's edit dialog (<c>IAppCommands.EditTask</c>, which replaces this overlay); it never navigates. Placeholder until the Search package.
/// </summary>
public sealed partial class SearchPaletteViewModel : OverlayViewModel
{
    public SearchPaletteViewModel(ScreenServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>Store, settings, clock and shell commands.</summary>
    public ScreenServices Services { get; }
}
