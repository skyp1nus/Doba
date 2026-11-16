namespace Doba.App.Platform;

/// <summary>"Launch at startup" (Windows: HKCU\...\Run). No-op elsewhere.</summary>
public interface IAutostart
{
    bool IsSupported { get; }
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

/// <summary>System-wide quick-add hotkey Ctrl+Alt+N (Windows: RegisterHotKey). No-op elsewhere.</summary>
public interface IGlobalHotkey : IDisposable
{
    bool IsSupported { get; }

    /// <summary>Raised on the UI thread when the hotkey is pressed.</summary>
    event EventHandler? Pressed;

    /// <summary>Registers the hotkey for the given top-level window; returns false if another app owns it.</summary>
    bool Register(Avalonia.Controls.TopLevel owner);
}

/// <summary>Reads OS preferences that affect the UI.</summary>
public interface ISystemPreferences
{
    /// <summary>False when the OS animation effects are switched off.</summary>
    bool AnimationsEnabled { get; }

    /// <summary>True when the OS can blur the desktop behind the window (Mica / acrylic).</summary>
    bool SupportsBackdrop { get; }
}

/// <summary>Shell integration: open a folder in the file manager, play the completion sound.</summary>
public interface IShellIntegration
{
    void OpenFolder(string path);
    void PlayCompletionSound();
}

/// <summary>Bundle handed to the composition root; <see cref="PlatformServices.Create"/> picks the Windows or no-op set.</summary>
public sealed partial record PlatformServices(IAutostart Autostart, IGlobalHotkey Hotkey, ISystemPreferences Preferences, IShellIntegration Shell);
