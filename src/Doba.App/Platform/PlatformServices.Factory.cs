using System.Runtime.Versioning;
using Avalonia.Controls;
using Doba.Bridge.Wire;
using Doba.App.Platform.Windows;

namespace Doba.App.Platform;

public sealed partial record PlatformServices
{
    /// <summary>Windows implementations on Windows, no-op fallbacks elsewhere (headless tests, Linux).</summary>
    public static PlatformServices Create() => Create(PlatformPlan.Full);

    /// <summary>
    /// The set <paramref name="plan"/> allows: the Windows implementation of each machine-wide part it permits, the no-op
    /// one of the others (and the no-op set off Windows). OS preferences (read only) and the shell (open a folder, the
    /// completion sound; only on a click) are always the platform's own.
    /// </summary>
    public static PlatformServices Create(PlatformPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return OperatingSystem.IsWindows() ? CreateWindows(plan) : NoOp();
    }

    /// <summary>The platform set for a launch with these flags (see <see cref="PlatformPlan.For"/>).</summary>
    public static PlatformServices ForLaunch(bool demo, bool customDataFolder) =>
        Create(PlatformPlan.For(demo, customDataFolder));

    public static PlatformServices NoOp() =>
        new(new NoOpAutostart(), new NoOpHotkey(), new DefaultPreferences(), new NoOpShell(), new NoOpTray(), new NoOpBridge());

    [SupportedOSPlatform("windows")]
    private static PlatformServices CreateWindows(PlatformPlan plan) =>
        new(plan.Autostart ? new WindowsAutostart() : new NoOpAutostart(),
            plan.Hotkey ? new WindowsHotkey() : new NoOpHotkey(),
            new WindowsPreferences(),
            new WindowsShell(),
            plan.Tray ? new AvaloniaTrayIcon() : new NoOpTray(),
            plan.Bridge ? new ClaudeBridge() : new NoOpBridge());

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

    private sealed class NoOpBridge : IClaudeBridge
    {
        public bool IsSupported => false;
        public bool IsRunning => false;
        public void Start(string dataFolder, Func<BridgeRequest, BridgeResponse> execute) { }
        public void Stop() { }
        public void Dispose() { }
    }
}

/// <summary>
/// Which machine-wide parts a launch may use. Decided from the flags alone (no platform object is built to decide it),
/// so tests check the choice without touching the registry, the global hotkey or the notification area.
/// </summary>
/// <param name="Autostart">The HKCU Run value (only the default data location).</param>
/// <param name="Hotkey">The global Ctrl+Alt+N (not in demo).</param>
/// <param name="Tray">The notification-area icon and close-to-tray (not in demo: closing a demo window quits, so a demo
/// never lingers as a hidden process).</param>
/// <param name="Bridge">The Claude bridge pipe, opened only while Claude access is on (not in demo: a demo has no data
/// folder of its own, so Doba.Mcp treats it as "not running").</param>
public sealed record PlatformPlan(bool Autostart, bool Hotkey, bool Tray, bool Bridge)
{
    /// <summary>Everything: the default data location.</summary>
    public static PlatformPlan Full { get; } = new(true, true, true, true);

    /// <summary><c>--demo</c> / <c>--selfcheck</c> use none; <c>--data</c> never touches the Run key but keeps the
    /// hotkey, the tray and the bridge; the default location uses all four.</summary>
    public static PlatformPlan For(bool demo, bool customDataFolder) =>
        new(Autostart: !demo && !customDataFolder, Hotkey: !demo, Tray: !demo, Bridge: !demo);
}
