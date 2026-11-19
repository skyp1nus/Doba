using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;

namespace Doba.App.Platform.Windows;

/// <summary>
/// One hidden, never-shown top-level window owned by Doba's platform services. It receives WM_HOTKEY for the
/// global hotkey and the WM_SETTINGCHANGE broadcast (a message-only HWND_MESSAGE window would not get broadcasts).
/// </summary>
/// <remarks>
/// Why not <c>Win32Properties.AddWndProcHookCallback</c> on the main window: that hook is one multicast delegate
/// whose <em>last</em> subscriber's return value wins (Avalonia 12.1.6 <c>WindowImpl.WndProcMessageHandler</c>), so
/// an extra subscriber can clobber another hook's result (e.g. WM_NCHITTEST for custom chrome); it also ties the
/// hotkey to the main window's lifetime. This is the same pattern Avalonia uses for its own platform window
/// (<c>Win32Platform.CreateMessageWindow</c>). Created on the UI thread, so Avalonia's message loop pumps it.
/// Lives until the process exits.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class PlatformWindow
{
    private const string ClassName = "Doba.PlatformWindow";

    private static PlatformWindow? s_instance;

    // Kept alive for the life of the process: the native window holds the thunk's address.
    private static NativeMethods.WndProc? s_wndProc;
    private static IntPtr s_className;

    private PlatformWindow(IntPtr hwnd) => Handle = hwnd;

    public IntPtr Handle { get; }

    /// <summary>Raised on the UI thread (inside the window procedure) for every message the window receives.</summary>
    public event Action<uint, IntPtr, IntPtr>? MessageReceived;

    /// <summary>The shared window, created on first use. Null when not called on the UI thread or creation failed.</summary>
    public static PlatformWindow? GetOrCreate()
    {
        if (s_instance is not null)
        {
            return s_instance;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            return null;
        }

        s_wndProc ??= WndProc;
        if (s_className == IntPtr.Zero)
        {
            s_className = Marshal.StringToHGlobalUni(ClassName);
        }

        var hInstance = NativeMethods.GetModuleHandle(IntPtr.Zero);
        var wc = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            hInstance = hInstance,
            lpszClassName = s_className,
        };

        var atom = NativeMethods.RegisterClassEx(ref wc);
        if (atom == 0)
        {
            return null;
        }

        var hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
            (IntPtr)atom, IntPtr.Zero, NativeMethods.WS_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        s_instance = new PlatformWindow(hwnd);
        return s_instance;
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (s_instance is { } window && hWnd == window.Handle)
        {
            try
            {
                window.MessageReceived?.Invoke(msg, wParam, lParam);
            }
#pragma warning disable CA1031 // An exception must not unwind through the native window procedure (it would kill the process).
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Dispatcher.UIThread.Post(() => throw new InvalidOperationException("Platform window message handler failed.", ex));
            }
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
