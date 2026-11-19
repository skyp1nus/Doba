using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Doba.App.Platform.Windows;

/// <summary>
/// Ctrl+Alt+N via <c>RegisterHotKey</c> on Doba's hidden <see cref="PlatformWindow"/>, so the hotkey keeps working
/// while the main window is hidden in the tray and does not depend on the main window's WndProc.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsHotkey : IGlobalHotkey
{
    /// <summary>Application-defined hotkey id (0x0000–0xBFFF is the range for applications).</summary>
    internal const int HotkeyId = 0x0D0B;

    private PlatformWindow? _window;
    private bool _disposed;

    public bool IsSupported => true;

    public event EventHandler? Pressed;

    /// <remarks>
    /// <paramref name="owner"/> is only a lifetime/threading hint: the registration lives on the hidden platform
    /// window. Must be called on the UI thread. Idempotent.
    /// </remarks>
    public bool Register(TopLevel owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_window is not null)
        {
            return true;
        }

        if (PlatformWindow.GetOrCreate() is not { } window)
        {
            return false;
        }

        window.MessageReceived += OnMessage;
        const uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
        if (NativeMethods.RegisterHotKey(window.Handle, HotkeyId, modifiers, NativeMethods.VK_N) == 0)
        {
            // Typically ERROR_HOTKEY_ALREADY_REGISTERED: another app owns Ctrl+Alt+N.
            window.MessageReceived -= OnMessage;
            return false;
        }

        _window = window;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_window is { } window)
        {
            _ = NativeMethods.UnregisterHotKey(window.Handle, HotkeyId);
            window.MessageReceived -= OnMessage;
            _window = null;
        }

        Pressed = null;
    }

    private void OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam == HotkeyId)
        {
            // Already on the UI thread (window procedure); posting keeps handlers (which open a window) out of the
            // native callback and satisfies the "raised on the UI thread" contract.
            Dispatcher.UIThread.Post(RaisePressed);
        }
    }

    private void RaisePressed()
    {
        if (!_disposed)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
    }
}
