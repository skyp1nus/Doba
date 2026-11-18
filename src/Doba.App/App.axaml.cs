using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Doba.App.Platform;
using Doba.App.Services;
using Doba.App.Shell;

namespace Doba.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Grayscale text, as in the prototype (-webkit-font-smoothing: antialiased): every top level (windows, popups)
        // gets it before its first frame and its visuals inherit it. TextOptions is not a styled property, so a
        // ControlTheme setter cannot carry it. No hinting either: Chromium draws the outlines unhinted, and the default
        // (light) hinting stretches small glyphs to whole pixels (the ASAP "!" came out a pixel taller).
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TopLevel>((topLevel, _) =>
        {
            TextOptions.SetTextRenderingMode(topLevel, TextRenderingMode.Antialias);
            TextOptions.SetTextHintingMode(topLevel, TextHintingMode.None);
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var flags = AppStartup.Flags;
            if (flags.SelfCheck)
            {
                SelfCheck.Start(desktop, this);
            }
            else
            {
                StartApp(desktop, flags);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartApp(IClassicDesktopStyleApplicationLifetime desktop, StartupFlags flags)
    {
        var services = AppServices.FromStartup(flags);
        var window = ShellComposition.CreateMainWindow(services, this);
        window.Icon = AvaloniaTrayIcon.LoadIcon();

        // The lifecycle decides when the app ends (close-to-tray keeps it running with no visible window).
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var lifecycle = new AppLifecycle(services, window,
            new LifecycleOptions(StartMinimized: flags.Minimized, ManageAutostart: flags.UsesDefaultDataLocation),
            () => desktop.Shutdown());
        desktop.ShutdownRequested += lifecycle.OnShutdownRequested;
        desktop.Exit += (_, _) => lifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
        lifecycle.Start();

        // The lifetime shows MainWindow when its loop starts; a --minimized start into the tray leaves it unset.
        if (lifecycle.ShowsWindowAtStart)
        {
            desktop.MainWindow = window;
        }

        AppStartup.WatchFirstFrame(window, desktop);
    }
}
