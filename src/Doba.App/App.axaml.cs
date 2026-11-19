using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Doba.App.Diagnostics;
using Doba.App.Platform;
using Doba.App.Services;
using Doba.App.Shell;

namespace Doba.App;

public partial class App : Application
{
    public override void Initialize()
    {
        PerfProbe.Mark("platform");
        AvaloniaXamlLoader.Load(this);
        PerfProbe.Mark("app-xaml");

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
        PerfProbe.Mark("framework");
        // A data folder that cannot be opened ends the launch with a message (StartupFailedException → Program.Main).
        var services = AppStartup.OpenServices(flags, AppStartup.TakePreload());
        PerfProbe.Mark("services");

        // The lifecycle decides when the app ends (close-to-tray keeps it running with no visible window).
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var (window, lifecycle) = Launch(services, this, flags, () => desktop.Shutdown());
        desktop.ShutdownRequested += lifecycle.OnShutdownRequested;
        desktop.Exit += (_, _) => lifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();

        // Another launch on the same data folder brings this window forward instead of opening the database twice.
        AppStartup.Instance?.ListenForShowRequests(() => Dispatcher.UIThread.Post(lifecycle.ShowWindow));

        // The lifetime shows MainWindow when its loop starts; a --minimized start into the tray leaves it unset.
        if (lifecycle.ShowsWindowAtStart)
        {
            desktop.MainWindow = window;
        }

        AppStartup.WatchFirstFrame(window, desktop);
    }

    /// <summary>
    /// The part of a launch that runs before the first frame, in its real order (tests run this same method on fake
    /// platform services): the main window, its icon and opening size, then the lifecycle's <see cref="AppLifecycle.Start"/>.
    /// Everything that can wait (tray icon, autostart, carry-over, backup) is left to the lifecycle's after-first-frame step.
    /// </summary>
    internal static (MainWindow Window, AppLifecycle Lifecycle) Launch(AppServices services, Application application,
        StartupFlags flags, Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(flags);
        var window = ShellComposition.CreateMainWindow(services, application);
        PerfProbe.Mark("window");
        window.Icon = AvaloniaTrayIcon.LoadIcon();
        PerfProbe.Mark("icon");
        window.FitToScreen();

        var lifecycle = new AppLifecycle(services, window,
            new LifecycleOptions(StartMinimized: flags.Minimized, ManageAutostart: flags.UsesDefaultDataLocation),
            shutdown);
        lifecycle.Start();
        PerfProbe.Mark("lifecycle");
        return (window, lifecycle);
    }
}
