using Avalonia;
using Doba.App.Motion;
using Doba.App.Theme;
using Doba.Core.Navigation;

namespace Doba.App.Shell;

/// <summary>
/// Shell wiring that later packages plug into without editing each other's files. The Shell package builds the real
/// main window here (MainViewModel, ThemeService, keyboard, overlays); the Motion package provides
/// <see cref="Transitions"/> and re-times the window's XAML transitions when the OS animation setting changes
/// (<see cref="MotionSync"/>).
/// </summary>
public static class ShellComposition
{
    /// <summary>The content-transition player used by the shell: the motion spec's navigation transitions
    /// (<see cref="ContentTransitions"/>). Instant whenever OS animations are off (tests open the shell that way); a test
    /// may swap in a recorder.</summary>
    public static IContentTransitions Transitions { get; set; } = new ContentTransitions();

    /// <summary>Builds the main window for <paramref name="app"/>, starting on the "Open on launch" screen.</summary>
    public static MainWindow CreateMainWindow(AppServices app, Application application) =>
        CreateMainWindow(app, application, null);

    /// <summary>
    /// Builds the main window: applies the appearance settings once (<see cref="ThemeService.Apply"/>), starts the
    /// <c>DayClock</c>, creates <see cref="MainViewModel"/> at <paramref name="start"/> (null = "Open on launch") and
    /// connects the window (content transitions, overlays, keyboard, launch entrance).
    /// </summary>
    public static MainWindow CreateMainWindow(AppServices app, Application application, NavState? start)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(application);
        var theme = new ThemeService(application);
        theme.Apply(app.Settings.Current);
        app.DayClock.Start();
        var vm = new MainViewModel(app, theme, start);
        var window = new MainWindow();
        window.Attach(vm);
        MotionSync.Track(window);
        return window;
    }
}
