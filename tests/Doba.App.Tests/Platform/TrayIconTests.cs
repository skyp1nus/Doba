using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Doba.App.Platform;

namespace Doba.App.Tests.Platform;

/// <summary>
/// The tray icon must never claim to exist when the platform gave it no native icon: the lifecycle hides the window
/// "to the tray" only while <see cref="ITrayIcon.IsSupported"/> is true, and a hidden window with no icon cannot be
/// brought back. (On Windows the native icon exists only once the TrayIcon is in the application's collection; the
/// real icon is checked by <c>Doba.exe --selfcheck</c>.)
/// </summary>
public class TrayIconTests
{
    [AvaloniaFact]
    public void Without_a_native_tray_the_icon_reports_unsupported_and_stays_hidden()
    {
        using var tray = new AvaloniaTrayIcon();
        Assert.True(tray.IsSupported); // not tried yet

        tray.IsVisible = true; // the headless platform has no notification area

        Assert.False(tray.IsNative);
        Assert.False(tray.IsVisible);
        Assert.False(tray.IsCreated);
        Assert.False(tray.IsSupported);
        Assert.Empty(TrayIcon.GetIcons(Application.Current!) ?? []);
    }

    [AvaloniaFact]
    public void An_unavailable_tray_ignores_later_requests_and_disposes_cleanly()
    {
        var tray = new AvaloniaTrayIcon();
        tray.IsVisible = true;
        tray.IsVisible = true;
        tray.IsVisible = false;

        Assert.False(tray.IsVisible);
        tray.Dispose();
        tray.Dispose();
        Assert.False(tray.IsVisible);
    }
}
