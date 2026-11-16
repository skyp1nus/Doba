using System.Runtime.Versioning;
using Avalonia.Controls;
using Doba.App.Platform.Windows;

namespace Doba.App.Platform;

public sealed partial record PlatformServices
{
    /// <summary>Windows implementations on Windows, no-op fallbacks elsewhere (headless tests, Linux).</summary>
    public static PlatformServices Create() => OperatingSystem.IsWindows() ? CreateWindows() : NoOp();

    public static PlatformServices NoOp() =>
        new(new NoOpAutostart(), new NoOpHotkey(), new DefaultPreferences(), new NoOpShell());

    [SupportedOSPlatform("windows")]
    private static PlatformServices CreateWindows() =>
        new(new WindowsAutostart(), new WindowsHotkey(), new WindowsPreferences(), new WindowsShell());

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
}
