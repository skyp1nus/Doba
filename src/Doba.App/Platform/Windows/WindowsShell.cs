using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Doba.App.Platform.Windows;

/// <summary>Explorer for "Open folder", winmm PlaySound for the completion click.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsShell : IShellIntegration
{
    // The WAV image on the pinned object heap: PlaySound(SND_MEMORY | SND_ASYNC) keeps reading it after the call
    // returns, so its address must never move and it must never be collected (static, process lifetime).
    private static byte[]? s_wav;

    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string folder;
        try
        {
            folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (!Directory.Exists(folder))
        {
            return;
        }

        try
        {
            // Full path to explorer.exe (no PATH/current-directory lookup); folder quoted for spaces. A trailing
            // backslash was trimmed above so it cannot escape the closing quote. Explorer always exits with 1, so the
            // exit code is meaningless and we do not wait.
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var _ = Process.Start(new ProcessStartInfo(explorer, $"\"{folder}\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Explorer missing or blocked by policy: nothing useful to do.
        }
    }

    public void PlayCompletionSound()
    {
        var wav = LazyInitializer.EnsureInitialized(ref s_wav, CreatePinnedWav);
        _ = NativeMethods.PlaySound(
            Marshal.UnsafeAddrOfPinnedArrayElement(wav, 0),
            IntPtr.Zero,
            NativeMethods.SND_MEMORY | NativeMethods.SND_ASYNC | NativeMethods.SND_NODEFAULT);
    }

    private static byte[] CreatePinnedWav()
    {
        var source = CompletionSound.Wav;
        var pinned = GC.AllocateUninitializedArray<byte>(source.Length, pinned: true);
        source.CopyTo(pinned, 0);
        return pinned;
    }
}
