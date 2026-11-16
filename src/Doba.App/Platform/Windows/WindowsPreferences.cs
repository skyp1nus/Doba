using System.Runtime.Versioning;
using Avalonia.Threading;

namespace Doba.App.Platform.Windows;

/// <summary>
/// "Animation effects" (Settings → Accessibility → Visual effects) via SPI_GETCLIENTAREAANIMATION, and backdrop
/// availability from the OS build. Change notifications come from the WM_SETTINGCHANGE broadcast.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPreferences : ISystemPreferences
{
    private EventHandler? _animationsChanged;
    private PlatformWindow? _window;
    private bool _lastAnimations;

    public bool AnimationsEnabled => ReadAnimationsEnabled();

    /// <summary>Windows 10 1809 (build 17763): acrylic blur behind a window is usable.</summary>
    public bool SupportsBackdrop => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    /// <summary>Windows 11 (build 22000): Mica is available and preferred over acrylic.</summary>
    public bool PrefersMica => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    /// <remarks>The first subscription (on the UI thread) starts listening for WM_SETTINGCHANGE.</remarks>
    public event EventHandler? AnimationsChanged
    {
        add
        {
            _animationsChanged += value;
            EnsureWatching();
        }
        remove => _animationsChanged -= value;
    }

    private void EnsureWatching()
    {
        if (_window is not null || PlatformWindow.GetOrCreate() is not { } window)
        {
            return;
        }

        _window = window;
        _lastAnimations = ReadAnimationsEnabled();
        window.MessageReceived += OnMessage;
    }

    private void OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Re-read on every WM_SETTINGCHANGE (some senders pass wParam = 0); raise only on an actual change.
        if (msg != NativeMethods.WM_SETTINGCHANGE)
        {
            return;
        }

        var now = ReadAnimationsEnabled();
        if (now == _lastAnimations)
        {
            return;
        }

        _lastAnimations = now;
        Dispatcher.UIThread.Post(() => _animationsChanged?.Invoke(this, EventArgs.Empty));
    }

    private static bool ReadAnimationsEnabled()
    {
        var enabled = 1;
        return NativeMethods.SystemParametersInfoBool(NativeMethods.SPI_GETCLIENTAREAANIMATION, 0, ref enabled, 0) == 0
            || enabled != 0; // If the query fails, keep animations on.
    }
}
