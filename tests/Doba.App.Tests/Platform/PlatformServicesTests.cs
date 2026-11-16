using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Doba.App.Platform;

namespace Doba.App.Tests.Platform;

public class PlatformServicesTests
{
    [Fact]
    public void Create_returns_the_no_op_set_off_Windows_and_the_Windows_set_on_Windows()
    {
        var services = PlatformServices.Create();
        var noOp = PlatformServices.NoOp();

        if (OperatingSystem.IsWindows())
        {
            Assert.True(services.Autostart.IsSupported || Environment.ProcessPath is null);
            Assert.True(services.Hotkey.IsSupported);
            Assert.NotEqual(noOp.Preferences.GetType(), services.Preferences.GetType());
            Assert.NotEqual(noOp.Shell.GetType(), services.Shell.GetType());
        }
        else
        {
            Assert.IsType(noOp.Autostart.GetType(), services.Autostart);
            Assert.IsType(noOp.Hotkey.GetType(), services.Hotkey);
            Assert.IsType(noOp.Preferences.GetType(), services.Preferences);
            Assert.IsType(noOp.Shell.GetType(), services.Shell);
            Assert.False(services.Autostart.IsSupported);
            Assert.False(services.Hotkey.IsSupported);
        }

        services.Hotkey.Dispose();
    }

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
