using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Doba.App.Dialogs;
using Doba.App.Platform;
using Doba.Core.Model;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Services;

/// <summary>How this launch treats the window and the machine.</summary>
/// <param name="StartMinimized"><c>--minimized</c>: start hidden in the tray (tray setting on and a tray exists),
/// otherwise minimised.</param>
/// <param name="ManageAutostart">Reconcile the Run entry with "Launch at startup": only on the default data location
/// (no <c>--demo</c>, no <c>--data</c>).</param>
public sealed record LifecycleOptions(bool StartMinimized = false, bool ManageAutostart = false);

/// <summary>
/// Makes Doba a resident Windows app around the main window: tray icon and close-to-tray, the global quick-add hotkey,
/// launch-at-startup reconcile, OS animations and backdrop, carry-over and daily backup after the first frame and at
/// midnight (<see cref="DataMaintenance"/>), <see cref="DayClock.Check"/> on activation (sleep through midnight), and
/// flushing pending writes on hide-to-tray, Quit, Exit and Windows session end. Everything goes through
/// <see cref="AppServices.Platform"/>, so tests and <c>--demo</c> run it on no-op or fake platform services.
/// </summary>
/// <remarks>
/// Order: construct → <see cref="Start"/> (before the window is shown; nothing slow happens here) → the window is shown
/// unless <see cref="ShowsWindowAtStart"/> is false → after the first frame: maintenance, autostart, tray icon →
/// <see cref="Quit"/> / desktop Exit → <see cref="DisposeAsync"/> (flushes and closes the database once).
/// </remarks>
public sealed class AppLifecycle : IAsyncDisposable
{
    /// <summary>How long a Windows session end waits for pending writes before letting Windows go on.</summary>
    private const int SessionEndFlushTimeoutMs = 3000;

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly LifecycleOptions _options;
    private readonly Action _shutdown;
    private AppSettings _settings;
    private WindowState _restoreState = WindowState.Normal;
    private bool _started;
    private bool _opened;
    private bool _afterFirstFrame;
    private bool _hotkeyRegistered;
    private bool _quitting;
    private bool _disposed;

    /// <param name="shutdown">Ends the process's main loop (desktop: <c>lifetime.Shutdown()</c>), which raises Exit.</param>
    public AppLifecycle(AppServices app, MainWindow window, LifecycleOptions options, Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(shutdown);
        _app = app;
        _window = window;
        _options = options;
        _shutdown = shutdown;
        _settings = app.Settings.Current;
        Maintenance = new DataMaintenance(app);
    }

    /// <summary>Carry-over and daily backup.</summary>
    public DataMaintenance Maintenance { get; }

    /// <summary>False when <c>--minimized</c> starts the app hidden in the tray (the window is not shown at all).</summary>
    public bool ShowsWindowAtStart { get; private set; } = true;

    /// <summary>The window is hidden in the tray (closed with "Keep running in the tray" on, or started there).</summary>
    public bool IsHiddenInTray { get; private set; }

    /// <summary>The global hotkey was registered (false in demo, or when another app owns Ctrl+Alt+N).</summary>
    public bool IsHotkeyRegistered => _hotkeyRegistered;

    /// <summary>The last flush started by a hide to the tray (completed when none ran). For tests.</summary>
    public Task LastFlush { get; private set; } = Task.CompletedTask;

    private ISettingsService Settings => _app.Settings;

    private PlatformServices Platform => _app.Platform;

    private bool CanHideToTray => _settings.KeepInTray && Platform.Tray.IsSupported;

    /// <summary>
    /// Wires everything before the window is shown: OS animations, the backdrop request, event handlers and the start
    /// state (<c>--minimized</c>). Work that is not needed for the first frame is posted for after it.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        var preferences = Platform.Preferences;
        AppMotion.SystemAnimationsEnabled = preferences.AnimationsEnabled;
        preferences.AnimationsChanged += OnAnimationsChanged;
        _window.ApplyBackdrop(preferences);

        _window.Opened += OnOpened;
        _window.Closing += OnClosing;
        _window.Closed += OnClosed;
        _window.Activated += OnActivated;
        _window.PropertyChanged += OnWindowPropertyChanged;
        Settings.Changed += OnSettingsChanged;
        Platform.Hotkey.Pressed += OnHotkeyPressed;
        Platform.Tray.OpenRequested += OnTrayOpen;
        Platform.Tray.NewTaskRequested += OnTrayNewTask;
        Platform.Tray.QuitRequested += OnTrayQuit;

        if (_options.StartMinimized)
        {
            if (CanHideToTray)
            {
                // Hidden in the tray: no window, so nothing waits for a first frame.
                ShowsWindowAtStart = false;
                IsHiddenInTray = true;
                Dispatcher.UIThread.Post(() =>
                {
                    RegisterHotkey();
                    AfterFirstFrame();
                }, DispatcherPriority.Background);
            }
            else
            {
                _window.WindowState = WindowState.Minimized;
            }
        }
    }

    /// <summary>Shows the window (from the tray, a hidden start or minimised), restores it and brings it to the front.</summary>
    public void ShowWindow()
    {
        if (_disposed || _quitting)
        {
            return;
        }

        IsHiddenInTray = false;
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = _restoreState;
        }

        _window.Activate();
        _app.DayClock.Check();
    }

    /// <summary>The quick-add path (global hotkey, tray "New task"): show the window and open New task, unless a task
    /// dialog is already open (it is kept, not replaced).</summary>
    public void QuickAdd()
    {
        ShowWindow();
        if (_window.ViewModel is { } vm && vm.Overlay is not TaskDialogViewModel)
        {
            vm.NewTask();
        }
    }

    /// <summary>Hides the window to the tray and flushes pending writes in the background.</summary>
    public void HideToTray()
    {
        if (_disposed || _quitting)
        {
            return;
        }

        IsHiddenInTray = true;
        _window.Hide();
        LastFlush = _app.FlushAsync();
    }

    /// <summary>Tray "Quit" (and closing with the tray off): ends the app; Exit then flushes and closes the database.</summary>
    public void Quit()
    {
        if (_quitting || _disposed)
        {
            return;
        }

        _quitting = true;
        Platform.Tray.IsVisible = false;
        _shutdown();
    }

    /// <summary>
    /// Windows session end (log off / shut down; <see cref="IClassicDesktopStyleApplicationLifetime.ShutdownRequested"/>):
    /// the process may be ended without an Exit, so pending writes are flushed here, synchronously and bounded.
    /// </summary>
    public void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // A save still inside the dialog's exit animation must reach the store before the flush.
        _window.ViewModel?.CompletePendingOverlayClose();
        _ = _app.FlushAsync().Wait(SessionEndFlushTimeoutMs);
    }

    /// <summary>Desktop Exit: stops everything, waits for a running backup, flushes and closes the database (once).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _quitting = true;
        Unsubscribe();
        await Maintenance.DisposeAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private void Unsubscribe()
    {
        if (!_started)
        {
            return;
        }

        Platform.Preferences.AnimationsChanged -= OnAnimationsChanged;
        _window.Opened -= OnOpened;
        _window.Closing -= OnClosing;
        _window.Closed -= OnClosed;
        _window.Activated -= OnActivated;
        _window.PropertyChanged -= OnWindowPropertyChanged;
        Settings.Changed -= OnSettingsChanged;
        Platform.Hotkey.Pressed -= OnHotkeyPressed;
        Platform.Tray.OpenRequested -= OnTrayOpen;
        Platform.Tray.NewTaskRequested -= OnTrayNewTask;
        Platform.Tray.QuitRequested -= OnTrayQuit;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_opened)
        {
            return;
        }

        _opened = true;
        Platform.Preferences.Watch(_window);
        RegisterHotkey();
        if (_window.WindowState == WindowState.Minimized)
        {
            // A minimised window may never render a frame; the chores must not wait for one.
            Dispatcher.UIThread.Post(AfterFirstFrame, DispatcherPriority.Background);
        }
        else
        {
            _window.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(AfterFirstFrame, DispatcherPriority.Background));
        }
    }

    /// <summary>Everything that must not delay the first frame: carry-over, backup, autostart, tray icon.</summary>
    private void AfterFirstFrame()
    {
        if (_afterFirstFrame || _disposed || _quitting)
        {
            return;
        }

        _afterFirstFrame = true;
        Maintenance.Start();
        ReconcileAutostart();
        Platform.Tray.IsVisible = _settings.KeepInTray;
    }

    private void RegisterHotkey()
    {
        if (!_hotkeyRegistered && !_disposed && Platform.Hotkey.IsSupported)
        {
            // False when another app owns Ctrl+Alt+N: the app carries on without it.
            _hotkeyRegistered = Platform.Hotkey.Register(_window);
        }
    }

    /// <summary>Makes the Run entry match "Launch at startup" (default data location only; written only on a mismatch).</summary>
    private void ReconcileAutostart()
    {
        var autostart = Platform.Autostart;
        if (!_options.ManageAutostart || !autostart.IsSupported)
        {
            return;
        }

        var wanted = _settings.LaunchAtStartup;
        if (autostart.IsEnabled != wanted)
        {
            autostart.SetEnabled(wanted);
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        {
            // The app is already going down (Quit, Exit, Windows session end): let the window close and do not
            // request another shutdown from Closed.
            _quitting = true;
            return;
        }

        if (_quitting)
        {
            return;
        }

        if (CanHideToTray)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    // Closed with the tray off (or no tray): quit. Posted so the shutdown does not start inside the window's own close.
    private void OnClosed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Quit);

    private void OnActivated(object? sender, EventArgs e) => _app.DayClock.Check();

    private void OnWindowPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && e.NewValue is WindowState state && state != WindowState.Minimized)
        {
            _restoreState = state;
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings next)
    {
        var previous = _settings;
        _settings = next;
        if (previous.KeepInTray != next.KeepInTray && _afterFirstFrame)
        {
            Platform.Tray.IsVisible = next.KeepInTray;
        }

        if (previous.LaunchAtStartup != next.LaunchAtStartup)
        {
            ReconcileAutostart();
        }
    }

    private void OnAnimationsChanged(object? sender, EventArgs e) =>
        AppMotion.SystemAnimationsEnabled = Platform.Preferences.AnimationsEnabled;

    private void OnHotkeyPressed(object? sender, EventArgs e) => QuickAdd();

    private void OnTrayOpen(object? sender, EventArgs e) => ShowWindow();

    private void OnTrayNewTask(object? sender, EventArgs e) => QuickAdd();

    private void OnTrayQuit(object? sender, EventArgs e) => Quit();
}
