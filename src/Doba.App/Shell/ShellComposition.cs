using Avalonia;

namespace Doba.App.Shell;

/// <summary>
/// Shell wiring that later packages plug into without editing each other's files. The Shell package builds the real
/// main window here (MainViewModel, ThemeService, keyboard, overlays); the Motion package replaces
/// <see cref="Transitions"/> at startup.
/// </summary>
public static class ShellComposition
{
    /// <summary>The content-transition player used by the shell. Defaults to <see cref="InstantContentTransitions"/>
    /// (also what tests use); set once at startup, before <see cref="CreateMainWindow"/>.</summary>
    public static IContentTransitions Transitions { get; set; } = new InstantContentTransitions();

    /// <summary>Builds the main window for <paramref name="app"/>. Placeholder: returns an empty
    /// <see cref="MainWindow"/> until the Shell package composes MainViewModel and the theme here.</summary>
    public static MainWindow CreateMainWindow(AppServices app, Application application)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(application);
        return new MainWindow();
    }
}
