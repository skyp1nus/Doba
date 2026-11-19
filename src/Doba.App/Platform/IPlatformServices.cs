using Avalonia.Controls;
using Doba.Bridge.Wire;

namespace Doba.App.Platform;

/// <summary>"Launch at startup" (Windows: HKCU\...\Run). No-op elsewhere.</summary>
public interface IAutostart
{
    bool IsSupported { get; }

    /// <summary>True when the Run entry exists and points at this executable.</summary>
    bool IsEnabled { get; }

    /// <summary>Writes or removes the Run entry. Failures are swallowed; re-read <see cref="IsEnabled"/> for the outcome.</summary>
    void SetEnabled(bool enabled);
}

/// <summary>System-wide quick-add hotkey Ctrl+Alt+N (Windows: RegisterHotKey). No-op elsewhere.</summary>
public interface IGlobalHotkey : IDisposable
{
    bool IsSupported { get; }

    /// <summary>Raised on the UI thread when the hotkey is pressed.</summary>
    event EventHandler? Pressed;

    /// <summary>
    /// Registers the hotkey for the given top-level window; returns false if another app owns it (or there is no
    /// native window). The window must stay alive (hide it for "keep running in the tray", do not close it) — the
    /// registration is tied to its HWND. Calling again with another window moves the registration.
    /// Must be called on the UI thread after the window's native handle exists (e.g. in <c>Opened</c>).
    /// </summary>
    bool Register(TopLevel owner);
}

/// <summary>Reads OS preferences that affect the UI.</summary>
public interface ISystemPreferences
{
    /// <summary>False when the OS animation effects are switched off (read live on every access).</summary>
    bool AnimationsEnabled { get; }

    /// <summary>True when the OS can blur the desktop behind the window (Mica / acrylic): Windows 10 1809+.</summary>
    bool SupportsBackdrop { get; }

    /// <summary>True on Windows 11 (build 22000+), where Mica is the preferred backdrop; acrylic blur otherwise.</summary>
    bool PrefersMica => false;

    /// <summary>
    /// Raised on the UI thread when the OS "Animation effects" setting changes. Only raised after
    /// <see cref="Watch"/> has been called with a live window.
    /// </summary>
    event EventHandler? AnimationsChanged { add { } remove { } }

    /// <summary>Starts listening for OS setting changes (WM_SETTINGCHANGE) through <paramref name="owner"/>'s window.</summary>
    void Watch(TopLevel owner) { }

    /// <summary>
    /// Raised on the UI thread when the system time or the time zone was changed (WM_TIMECHANGE). .NET caches the local
    /// time zone, so the handler clears that cache and re-reads "today" (otherwise "today", the now-line, midnight and
    /// carry-over stay on the old zone until a restart).
    /// </summary>
    event EventHandler? ClockChanged { add { } remove { } }
}

/// <summary>Shell integration: open a folder in the file manager, play the completion sound.</summary>
public interface IShellIntegration
{
    /// <summary>Opens <paramref name="path"/> in Explorer. Does nothing if the folder does not exist.</summary>
    void OpenFolder(string path);

    /// <summary>Plays the short soft completion click asynchronously (never blocks, never throws).</summary>
    void PlayCompletionSound();
}

/// <summary>
/// The notification-area (tray) icon with its menu: Open Doba / New task / Quit. Created lazily the first time it is
/// made visible; <see cref="IDisposable.Dispose"/> removes it. No-op where there is no tray (headless, tests).
/// </summary>
public interface ITrayIcon : IDisposable
{
    bool IsSupported { get; }

    /// <summary>Shows or removes the icon. Must be set on the UI thread.</summary>
    bool IsVisible { get; set; }

    /// <summary>Clicking the icon or "Open Doba". Raised on the UI thread.</summary>
    event EventHandler? OpenRequested;

    /// <summary>"New task". Raised on the UI thread.</summary>
    event EventHandler? NewTaskRequested;

    /// <summary>"Quit". Raised on the UI thread.</summary>
    event EventHandler? QuitRequested;
}

/// <summary>
/// The Claude bridge: the local named pipe Doba.Mcp.exe talks to while Settings → Data → Claude access is on
/// (Windows: <c>Doba.Bridge.BridgeHost</c>, current user only). Every request runs <c>execute</c> on the UI thread.
/// No-op where there is no pipe for it (headless, tests, demo, self-check).
/// </summary>
public interface IClaudeBridge : IDisposable
{
    bool IsSupported { get; }

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Opens the pipe for <paramref name="dataFolder"/> (<c>BridgeNames.PipeName</c>) and starts answering requests;
    /// each one runs <paramref name="execute"/> on the UI thread. Does nothing while running. Call on the UI thread.
    /// Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the pipe cannot be created.
    /// </summary>
    void Start(string dataFolder, Func<BridgeRequest, BridgeResponse> execute);

    /// <summary>
    /// Synchronous and bounded: requests already queued for the UI thread answer <c>closing</c> instead of running,
    /// then the pipe is closed. Never waits for queued work (safe on the UI thread while the app shuts down). Idempotent.
    /// </summary>
    void Stop();
}

/// <summary>Bundle handed to the composition root; <see cref="PlatformServices.Create()"/> picks the Windows or no-op set.</summary>
public sealed partial record PlatformServices(IAutostart Autostart, IGlobalHotkey Hotkey, ISystemPreferences Preferences, IShellIntegration Shell, ITrayIcon Tray, IClaudeBridge Bridge);
