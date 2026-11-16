using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Doba.App.Platform.Windows;

/// <summary>
/// Win32 entry points used by the platform services.
/// </summary>
/// <remarks>
/// These are <see cref="DllImportAttribute"/> rather than <c>LibraryImport</c> because the LibraryImport source
/// generator emits <c>unsafe</c> code and Doba.App does not set <c>AllowUnsafeBlocks</c>. Every signature is strictly
/// blittable (<see cref="IntPtr"/>, <see cref="int"/>, <see cref="uint"/>, by-ref <see cref="int"/>; BOOL is returned
/// as <see cref="int"/>; no strings, no <c>SetLastError</c>), so the runtime/NativeAOT calls the export directly with
/// no marshalling stub — the same code LibraryImport would generate. Switching later is mechanical: set
/// <c>AllowUnsafeBlocks</c>, make the class/methods <c>partial</c>, replace DllImport with LibraryImport.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    private const string User32 = "user32.dll";
    private const string Kernel32 = "kernel32.dll";
    private const string WinMM = "winmm.dll";

    // --- Hidden platform window ---------------------------------------------------------------------------------

    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;
    internal const uint WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>WNDPROC. Marshalled with <see cref="Marshal.GetFunctionPointerForDelegate{TDelegate}"/>; all parameters are blittable.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>WNDCLASSEXW with every pointer as <see cref="IntPtr"/> so the struct is blittable.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport(Kernel32, ExactSpelling = true, EntryPoint = "GetModuleHandleW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr GetModuleHandle(IntPtr lpModuleName);

    /// <returns>The class atom, or 0 on failure.</returns>
    [DllImport(User32, ExactSpelling = true, EntryPoint = "RegisterClassExW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern ushort RegisterClassEx(ref WNDCLASSEXW lpwcx);

    /// <param name="lpClassName">A class atom in the low word (MAKEINTATOM) or a pointer to a UTF-16 class name.</param>
    [DllImport(User32, ExactSpelling = true, EntryPoint = "CreateWindowExW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr CreateWindowEx(
        uint dwExStyle, IntPtr lpClassName, IntPtr lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport(User32, ExactSpelling = true, EntryPoint = "DefWindowProcW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // --- Hotkey -------------------------------------------------------------------------------------------------

    internal const uint WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const uint VK_N = 0x4E;

    /// <returns>Non-zero on success; zero when the combination is already registered by another app.</returns>
    [DllImport(User32, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport(User32, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int UnregisterHotKey(IntPtr hWnd, int id);

    // --- System parameters --------------------------------------------------------------------------------------

    internal const uint WM_SETTINGCHANGE = 0x001A;
    internal const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
    internal const uint SPI_SETCLIENTAREAANIMATION = 0x1043;

    /// <summary>SystemParametersInfoW for parameters whose pvParam is a BOOL*.</summary>
    [DllImport(User32, ExactSpelling = true, EntryPoint = "SystemParametersInfoW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int SystemParametersInfoBool(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    // --- Sound --------------------------------------------------------------------------------------------------

    internal const uint SND_ASYNC = 0x0001;
    internal const uint SND_NODEFAULT = 0x0002;
    internal const uint SND_MEMORY = 0x0004;

    /// <summary>PlaySoundW with SND_MEMORY: <paramref name="pszSound"/> points at an in-memory WAV image.</summary>
    [DllImport(WinMM, ExactSpelling = true, EntryPoint = "PlaySoundW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);
}
