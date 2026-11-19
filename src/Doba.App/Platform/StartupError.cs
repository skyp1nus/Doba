using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Doba.App.Platform.Windows;

namespace Doba.App.Platform;

/// <summary>
/// Tells the owner why Doba did not start, before any window exists: a line on stderr always, and on Windows a native
/// message box (the app is a WinExe, so stderr reaches nobody unless a script captured it). Never throws.
/// </summary>
internal static class StartupError
{
    /// <summary>Writes <paramref name="message"/> to stderr and, when <paramref name="showDialog"/> is set on Windows,
    /// shows it in a modal error box and waits for OK.</summary>
    public static void Report(string message, bool showDialog)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            Console.Error.WriteLine("Doba: " + message.ReplaceLineEndings(" "));
            Console.Error.Flush();
        }
        catch (IOException)
        {
            // No usable stderr: the dialog below is the report.
        }

        if (showDialog && OperatingSystem.IsWindows())
        {
            ShowWindowsDialog(message);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ShowWindowsDialog(string message)
    {
        var text = Marshal.StringToHGlobalUni(message);
        var caption = Marshal.StringToHGlobalUni("Doba");
        try
        {
            _ = NativeMethods.MessageBox(IntPtr.Zero, text, caption,
                NativeMethods.MB_OK | NativeMethods.MB_ICONERROR | NativeMethods.MB_SETFOREGROUND);
        }
        finally
        {
            Marshal.FreeHGlobal(text);
            Marshal.FreeHGlobal(caption);
        }
    }
}
