using System.Runtime.Versioning;
using Avalonia.Controls;
using Doba.App.Platform.Windows;

namespace Doba.App.Platform;

public sealed partial record PlatformServices
{
    /// <summary>Windows implementations on Windows, no-op fallbacks elsewhere (headless tests, Linux).</summary>
    public static PlatformServices Create() => Create(autostart: true, hotkey: true);

    /// <summary>
    /// Like <see cref="Create()"/>, but with the no-op autostart and/or hotkey where the machine-wide parts must not be
    /// touched: <c>--demo</c> gets neither, <c>--data</c> keeps the hotkey but never touches the registry.
    /// </summary>
    public static PlatformServices Create(bool autostart, bool hotkey) =>
        OperatingSystem.IsWindows() ? CreateWindows(autostart, hotkey) : NoOp();

    /// <summary>The platform set for a launch with these flags (see <see cref="Create(bool, bool)"/>).</summary>
    public static PlatformServices ForLaunch(bool demo, bool customDataFolder) =>
        Create(autostart: !demo && !customDataFolder, hotkey: !demo);

    public static PlatformServices NoOp() =>
        new(new NoOpAutostart(), new NoOpHotkey(), new DefaultPreferences(), new NoOpShell(), new NoOpTray());

    [SupportedOSPlatform("windows")]
    private static PlatformServices CreateWindows(bool autostart, bool hotkey) =>
        new(autostart ? new WindowsAutostart() : new NoOpAutostart(),
            hotkey ? new WindowsHotkey() : new NoOpHotkey(),
            new WindowsPreferences(),
            new WindowsShell(),
            new AvaloniaTrayIcon());

    private sealed class NoOpAutostart : IAutostart
    {
        public bool IsSupported => false;
        public bool IsEnabled => false;
        public void SetEnabled(bool enabled) { }
    }

    private sealed class NoOpHotkey : IGlobalHotkey
    {
        public bool IsSupported => false;
        public event EventHandler? Pressed { add { } remove { } }
        public bool Register(TopLevel owner) => false;
        public void Dispose() { }
    }

    private sealed class DefaultPreferences : ISystemPreferences
    {
        public bool AnimationsEnabled => true;
        public bool SupportsBackdrop => false;
        public bool PrefersMica => false;
        public event EventHandler? AnimationsChanged { add { } remove { } }
        public void Watch(TopLevel owner) { }
    }

    private sealed class NoOpShell : IShellIntegration
    {
        public void OpenFolder(string path) { }
        public void PlayCompletionSound() { }
    }

    private sealed class NoOpTray : ITrayIcon
    {
        public bool IsSupported => false;
        public bool IsVisible { get => false; set { } }
        public event EventHandler? OpenRequested { add { } remove { } }
        public event EventHandler? NewTaskRequested { add { } remove { } }
        public event EventHandler? QuitRequested { add { } remove { } }
        public void Dispose() { }
    }
}
