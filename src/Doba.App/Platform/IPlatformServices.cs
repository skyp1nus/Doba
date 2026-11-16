using Avalonia.Controls;

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
}

/// <summary>Shell integration: open a folder in the file manager, play the completion sound.</summary>
public interface IShellIntegration
{
    /// <summary>Opens <paramref name="path"/> in Explorer. Does nothing if the folder does not exist.</summary>
    void OpenFolder(string path);

    /// <summary>Plays the short soft completion click asynchronously (never blocks, never throws).</summary>
    void PlayCompletionSound();
}

/// <summary>Bundle handed to the composition root; <see cref="PlatformServices.Create"/> picks the Windows or no-op set.</summary>
public sealed partial record PlatformServices(IAutostart Autostart, IGlobalHotkey Hotkey, ISystemPreferences Preferences, IShellIntegration Shell);
