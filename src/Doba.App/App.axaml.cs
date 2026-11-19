using System.Globalization;
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
    /// <summary>
    /// How long desktop Exit waits for the lifecycle to flush and close the database before the process ends anyway.
    /// A normal flush takes milliseconds; a database held busy by another process makes the writer wait up to SQLite's
    /// 30 s busy timeout, and a process lingering that long after its window closed looks hung (and a new launch on the
    /// same folder only signals it). Windows session end has its own 3 s bound (AppLifecycle).
    /// </summary>
    internal static readonly TimeSpan ExitDrainTimeout = TimeSpan.FromMilliseconds(ExitDrainTimeoutMs);

    private const int ExitDrainTimeoutMs = 5000;

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
        desktop.Exit += (_, _) => DrainOnExit(lifecycle, ExitDrainTimeout, services.LogProblem);

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
    /// Desktop Exit: disposes <paramref name="lifecycle"/> (backup wait, flush, database close) and waits for it at most
    /// <paramref name="timeout"/>. On a timeout the unwritten changes are given up: <paramref name="log"/> records it and
    /// the process ends (SQLite's journal keeps the file consistent; what was committed stays). Returns false on a
    /// timeout. A failure inside the dispose is rethrown, as before the bound.
    /// </summary>
    internal static bool DrainOnExit(IAsyncDisposable lifecycle, TimeSpan timeout, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(log);
        var drain = lifecycle.DisposeAsync().AsTask();
        try
        {
            drain.WaitAsync(timeout).GetAwaiter().GetResult();
            return true;
        }
        catch (TimeoutException) when (!drain.IsCompleted)
        {
            log(string.Create(CultureInfo.InvariantCulture,
                $"exit: pending changes were not written within {timeout.TotalSeconds:0.#} s (the database stayed busy); quitting without them."));
            return false;
        }
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
