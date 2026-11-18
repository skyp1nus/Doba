using System.Globalization;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Doba.App.Platform.Windows;
using Doba.App.Shell;
using Microsoft.Win32;

namespace Doba.App.Platform;

/// <summary>
/// <c>--selfcheck</c>: exercises the Windows platform code on a real demo window and prints one line per check to
/// stdout, then exits with 0 when every check passed (1 otherwise). Nothing is read from or written to the data folder;
/// the registry check uses <c>HKCU\Software\Doba\SelfCheck</c> (never the Run key, which is only read); the hotkey and
/// tray icon are created and removed again. A watchdog ends the run after <see cref="Watchdog"/>.
/// </summary>
internal static class SelfCheck
{
    internal const string TestKeyPath = @"Software\Doba\SelfCheck";
    private const string ParentKeyPath = @"Software\Doba";

    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HotkeyEcho = TimeSpan.FromSeconds(2);

    public static void Start(IClassicDesktopStyleApplicationLifetime desktop, Application application)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(application);
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("selfcheck: FAIL not running on Windows");
            desktop.MainWindow = null;
            Dispatcher.UIThread.Post(() => desktop.Shutdown(1));
            return;
        }

        StartWindows(desktop, application);
    }

    [SupportedOSPlatform("windows")]
    private static void StartWindows(IClassicDesktopStyleApplicationLifetime desktop, Application application)
    {
        var preferences = new WindowsPreferences();
        var app = AppServices.CreateDemo(platform: PlatformServices.NoOp() with { Preferences = preferences });
        var window = ShellComposition.CreateMainWindow(app, application);
        var report = new Report();
        report.Line("window-icon", Try(() => window.Icon = AvaloniaTrayIcon.LoadIcon()), "app icon loaded from " + AvaloniaTrayIcon.IconUri);
        window.ApplyBackdrop(preferences);
        desktop.MainWindow = window;
        desktop.Exit += (_, _) => app.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var watchdog = new DispatcherTimer { Interval = Watchdog };
        watchdog.Tick += (_, _) =>
        {
            watchdog.Stop();
            Console.WriteLine("selfcheck: FAIL watchdog — checks did not finish in time");
            Console.Out.Flush();
            desktop.Shutdown(1);
        };
        watchdog.Start();

        var started = false;
        window.Opened += (_, _) =>
        {
            if (started)
            {
                return;
            }

            started = true;
            window.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(async () =>
            {
                await RunAsync(window, preferences, report).ConfigureAwait(true);
                watchdog.Stop();
                Console.WriteLine(report.Summary);
                Console.Out.Flush();
                desktop.Shutdown(report.Failed == 0 ? 0 : 1);
            }, DispatcherPriority.Background));
        };
    }

    [SupportedOSPlatform("windows")]
    private static async Task RunAsync(MainWindow window, WindowsPreferences preferences, Report report)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        report.Line("window", window.IsVisible && handle != IntPtr.Zero,
            string.Create(CultureInfo.InvariantCulture, $"created and shown, hwnd=0x{handle:X}, {window.Bounds.Width:F0}x{window.Bounds.Height:F0}"));

        var level = window.ActualTransparencyLevel;
        var wanted = !preferences.SupportsBackdrop ? "none (OS has no backdrop)" : preferences.PrefersMica ? "Mica" : "AcrylicBlur";
        report.Line("backdrop", !preferences.SupportsBackdrop || !level.Equals(WindowTransparencyLevel.None),
            $"requested {wanted}, ActualTransparencyLevel={level}, painted fallback {(window.HasSystemBackdrop ? "hidden" : "shown")}");

        var readOk = WindowsPreferences.TryReadAnimationsEnabled(out var animations);
        report.Line("os-animations", readOk, readOk
            ? "SystemParametersInfo(SPI_GETCLIENTAREAANIMATION) = " + (animations ? "on" : "off")
            : "SystemParametersInfo(SPI_GETCLIENTAREAANIMATION) failed");

        CheckSettingChange(preferences, report);
        await CheckHotkeyAsync(window, report).ConfigureAwait(true);
        CheckTray(report);
        CheckAutostart(report);
    }

    /// <summary>The hidden platform window receives WM_SETTINGCHANGE through its WndProc without failing.</summary>
    [SupportedOSPlatform("windows")]
    private static void CheckSettingChange(WindowsPreferences preferences, Report report)
    {
        var changed = 0;
        EventHandler handler = (_, _) => changed++;
        preferences.AnimationsChanged += handler; // creates the platform window and starts listening
        var platformWindow = PlatformWindow.GetOrCreate();
        var received = 0;
        Action<uint, IntPtr, IntPtr>? probe = (msg, _, _) =>
        {
            if (msg == NativeMethods.WM_SETTINGCHANGE)
            {
                received++;
            }
        };
        if (platformWindow is not null)
        {
            platformWindow.MessageReceived += probe;
            _ = NativeMethods.SendMessage(platformWindow.Handle, NativeMethods.WM_SETTINGCHANGE, IntPtr.Zero, IntPtr.Zero);
            platformWindow.MessageReceived -= probe;
        }

        preferences.AnimationsChanged -= handler;
        report.Line("platform-window", platformWindow is not null && received == 1,
            platformWindow is null
                ? "hidden platform window could not be created"
                : string.Create(CultureInfo.InvariantCulture, $"hwnd=0x{platformWindow.Handle:X}, WM_SETTINGCHANGE reached the managed WndProc {received}x (no setting changed: {changed} events)"));
    }

    [SupportedOSPlatform("windows")]
    private static async Task CheckHotkeyAsync(MainWindow window, Report report)
    {
        using var hotkey = new WindowsHotkey();
        var pressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hotkey.Pressed += (_, _) => pressed.TrySetResult();
        if (!hotkey.Register(window))
        {
            report.Line("hotkey", true, "Ctrl+Alt+N is in use by another app (registration refused; Doba carries on without it)");
            return;
        }

        var platformWindow = PlatformWindow.GetOrCreate()!;
        _ = NativeMethods.SendMessage(platformWindow.Handle, NativeMethods.WM_HOTKEY, WindowsHotkey.HotkeyId, IntPtr.Zero);
        bool delivered;
        try
        {
            await pressed.Task.WaitAsync(HotkeyEcho).ConfigureAwait(true);
            delivered = true;
        }
        catch (TimeoutException)
        {
            delivered = false;
        }

        hotkey.Dispose();

        // Released: the same id can be registered again on the platform window, then is released once more.
        const uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
        var released = NativeMethods.RegisterHotKey(platformWindow.Handle, WindowsHotkey.HotkeyId, modifiers, NativeMethods.VK_N) != 0;
        if (released)
        {
            _ = NativeMethods.UnregisterHotKey(platformWindow.Handle, WindowsHotkey.HotkeyId);
        }

        report.Line("hotkey", delivered && released,
            $"Ctrl+Alt+N registered; simulated WM_HOTKEY {(delivered ? "raised Pressed" : "did NOT raise Pressed")}; {(released ? "unregistered" : "NOT released")}");
    }

    private static void CheckTray(Report report)
    {
        var ok = Try(() =>
        {
            using var tray = new AvaloniaTrayIcon();
            tray.IsVisible = true;
            if (!tray.IsCreated || !tray.IsVisible)
            {
                throw new InvalidOperationException("the tray icon was not created");
            }

            tray.IsVisible = false;
        });
        report.Line("tray", ok, ok ? "icon created, shown, hidden and removed" : "tray icon failed");
    }

    [SupportedOSPlatform("windows")]
    private static void CheckAutostart(Report report)
    {
        var exe = Environment.ProcessPath;
        bool parentExisted;
        using (var parent = Registry.CurrentUser.OpenSubKey(ParentKeyPath, writable: false))
        {
            parentExisted = parent is not null;
        }

        var written = false;
        var removed = false;
        var stored = (string?)null;
        try
        {
            var test = new WindowsAutostart(exe, TestKeyPath, AutostartCommand.ValueName);
            test.SetEnabled(true);
            written = test.IsEnabled;
            using (var key = Registry.CurrentUser.OpenSubKey(TestKeyPath, writable: false))
            {
                stored = key?.GetValue(AutostartCommand.ValueName) as string;
            }

            test.SetEnabled(false);
            removed = !test.IsEnabled;
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(TestKeyPath, throwOnMissingSubKey: false);
            if (!parentExisted)
            {
                using var parent = Registry.CurrentUser.OpenSubKey(ParentKeyPath, writable: false);
                if (parent is { SubKeyCount: 0, ValueCount: 0 })
                {
                    parent.Dispose();
                    Registry.CurrentUser.DeleteSubKey(ParentKeyPath, throwOnMissingSubKey: false);
                }
            }
        }

        bool leftover;
        using (var key = Registry.CurrentUser.OpenSubKey(TestKeyPath, writable: false))
        {
            leftover = key is not null;
        }

        var expected = exe is null ? null : AutostartCommand.Build(exe);
        report.Line("autostart", exe is not null && written && removed && stored == expected && !leftover,
            $"HKCU\\{TestKeyPath}: wrote {stored ?? "(nothing)"}, read back {(written ? "ok" : "FAILED")}, removed {(removed ? "ok" : "FAILED")}, test key {(leftover ? "LEFT BEHIND" : "deleted")}");

        // The real Run entry is only read, never written.
        var run = new WindowsAutostart();
        report.Line("run-key", true, $"HKCU\\{WindowsAutostart.RunKeyPath}\\{AutostartCommand.ValueName} {(run.IsEnabled ? "points at this exe" : "does not point at this exe")} (read only)");
    }

    private static bool Try(Action action)
    {
        try
        {
            action();
            return true;
        }
#pragma warning disable CA1031 // A diagnostic reports every failure as a failed line instead of crashing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.WriteLine("selfcheck:   error: " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    private sealed class Report
    {
        private int _passed;

        public int Failed { get; private set; }

        public string Summary => string.Create(CultureInfo.InvariantCulture,
            $"selfcheck: {(Failed == 0 ? "PASS" : "FAIL")} {_passed}/{_passed + Failed} checks passed");

        public void Line(string name, bool ok, string detail)
        {
            if (ok)
            {
                _passed++;
            }
            else
            {
                Failed++;
            }

            Console.WriteLine($"selfcheck: {(ok ? "ok  " : "FAIL")} {name,-16} {detail}");
        }
    }
}
