using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Doba.App.Theme.Gallery;

/// <summary>Design-system gallery (every control theme in its states); rendered by the controls-gallery screenshot test.</summary>
public partial class ControlsGallery : UserControl
{
    public ControlsGallery() => AvaloniaXamlLoader.Load(this);
}
