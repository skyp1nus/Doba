using Avalonia.Controls;
using Doba.App.Shell;

namespace Doba.App.Dialogs.Search;

/// <summary>The search palette. DataContext: <see cref="SearchPaletteViewModel"/>.</summary>
public partial class SearchPaletteView : UserControl, IOverlayView
{
    public SearchPaletteView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    public void FocusInitial()
    {
    }
}
