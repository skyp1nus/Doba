using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Doba.App.Platform;
using Doba.Bridge.Wire;

namespace Doba.App.Tests.Platform;

public class PlatformServicesTests
{
    // PlatformServices.Create() with a plan that allows a machine-wide part builds the real Windows object for it, which
    // tests must never do (CLAUDE.md). The choice is tested on PlatformPlan; an all-off plan builds only no-ops for the
    // machine-wide parts, which is safe to check here.
    [Fact]
    public void An_all_off_plan_builds_no_machine_wide_part_on_any_OS()
    {
        var services = PlatformServices.Create(new PlatformPlan(Autostart: false, Hotkey: false, Tray: false, Bridge: false));
        var noOp = PlatformServices.NoOp();

        Assert.IsType(noOp.Autostart.GetType(), services.Autostart);
        Assert.IsType(noOp.Hotkey.GetType(), services.Hotkey);
        Assert.IsType(noOp.Tray.GetType(), services.Tray);
        Assert.IsType(noOp.Bridge.GetType(), services.Bridge);
        if (OperatingSystem.IsWindows())
        {
            // The read-only preferences and the click-only shell are the platform's own (no member is used here).
            Assert.NotEqual(noOp.Preferences.GetType(), services.Preferences.GetType());
            Assert.NotEqual(noOp.Shell.GetType(), services.Shell.GetType());
        }
    }

    [Fact]
    public void The_full_plan_allows_every_machine_wide_part() =>
        Assert.Equal(new PlatformPlan(Autostart: true, Hotkey: true, Tray: true, Bridge: true), PlatformPlan.Full);

    [Fact]
    public void No_op_autostart_is_unsupported_and_never_enabled()
    {
        var autostart = PlatformServices.NoOp().Autostart;

        autostart.SetEnabled(true);

        Assert.False(autostart.IsSupported);
        Assert.False(autostart.IsEnabled);
        autostart.SetEnabled(false);
        Assert.False(autostart.IsEnabled);
    }

    [AvaloniaFact]
    public void No_op_hotkey_refuses_registration_and_never_fires()
    {
        var hotkey = PlatformServices.NoOp().Hotkey;
        var window = new Window();
        var pressed = 0;
        EventHandler handler = (_, _) => pressed++;

        hotkey.Pressed += handler;
        Assert.False(hotkey.IsSupported);
        Assert.False(hotkey.Register(window));
        hotkey.Pressed -= handler;
        hotkey.Dispose();
        hotkey.Dispose(); // idempotent

        Assert.Equal(0, pressed);
        window.Close();
    }

    [Fact]
    public void Default_preferences_keep_animations_on_and_report_no_backdrop()
    {
        var preferences = PlatformServices.NoOp().Preferences;
        EventHandler handler = (_, _) => { };

        preferences.AnimationsChanged += handler;
        preferences.AnimationsChanged -= handler;

        Assert.True(preferences.AnimationsEnabled);
        Assert.False(preferences.SupportsBackdrop);
        Assert.False(preferences.PrefersMica);
    }

    [Fact]
    public void No_op_bridge_is_unsupported_and_never_runs()
    {
        var bridge = PlatformServices.NoOp().Bridge;
        var executed = 0;

        bridge.Start(Path.GetTempPath(), _ => { executed++; return BridgeResponse.Failure(BridgeErrorCodes.Internal, "x"); });

        Assert.False(bridge.IsSupported);
        Assert.False(bridge.IsRunning);
        bridge.Stop();
        bridge.Dispose();
        bridge.Dispose(); // idempotent
        Assert.Equal(0, executed);
    }

    [Fact]
    public void No_op_shell_ignores_requests()
    {
        var shell = PlatformServices.NoOp().Shell;

        shell.OpenFolder(Path.GetTempPath());
        shell.OpenFolder("");
        shell.PlayCompletionSound();
    }

    [Fact]
    public void New_preference_members_have_safe_defaults_for_existing_implementations()
    {
        // An implementation written against the original two-member interface still compiles and behaves sanely.
        ISystemPreferences preferences = new MinimalPreferences();
        EventHandler handler = (_, _) => { };

        preferences.AnimationsChanged += handler;
        preferences.AnimationsChanged -= handler;

        Assert.False(preferences.PrefersMica);
    }

    private sealed class MinimalPreferences : ISystemPreferences
    {
        public bool AnimationsEnabled => true;
        public bool SupportsBackdrop => true;
    }
}
