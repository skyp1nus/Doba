using Avalonia.Headless.XUnit;
using Doba.App.Platform;
using Doba.App.Services;
using Doba.Core.Demo;
using Doba.Core.Model;

namespace Doba.App.Tests.Lifecycle;

public class StartupTests
{
    [Fact]
    public void No_arguments_mean_the_default_data_location()
    {
        var flags = StartupFlags.Parse([]);

        Assert.Equal(StartupFlags.None, flags);
        Assert.True(flags.UsesDefaultDataLocation);
        Assert.False(flags.Minimized);
    }

    [Fact]
    public void Minimized_is_what_the_Run_entry_passes()
    {
        var command = AutostartCommand.Build(@"C:\Program Files\Doba\Doba.exe");
        var args = command[(command.LastIndexOf('"') + 2)..].Split(' ');

        var flags = StartupFlags.Parse(args);

        Assert.True(flags.Minimized);
        Assert.True(flags.UsesDefaultDataLocation);
    }

    [Theory]
    [InlineData("--minimized")]
    [InlineData("--MINIMIZED")]
    public void Minimized_is_case_insensitive(string arg) => Assert.True(StartupFlags.Parse([arg]).Minimized);

    [Fact]
    public void Every_flag_is_parsed_and_unknown_ones_are_ignored()
    {
        var flags = StartupFlags.Parse(["--perf", "--whatever", "--exit-after-first-frame", "--data", @"D:\x y", "--minimized", "--selfcheck", "--demo"]);

        Assert.Equal(new StartupFlags(Perf: true, ExitAfterFirstFrame: true, Demo: true, DataFolder: @"D:\x y", Minimized: true, SelfCheck: true), flags);
    }

    [Fact]
    public void Data_without_a_folder_is_ignored() => Assert.Null(StartupFlags.Parse(["--data"]).DataFolder);

    [Theory]
    [InlineData("--demo")]
    [InlineData("--selfcheck")]
    public void Demo_and_selfcheck_are_not_the_default_location(string arg) =>
        Assert.False(StartupFlags.Parse([arg]).UsesDefaultDataLocation);

    [Fact]
    public void A_data_folder_is_not_the_default_location() =>
        Assert.False(StartupFlags.Parse(["--data", "x"]).UsesDefaultDataLocation);

    [Fact]
    public void Demo_launch_gets_no_op_autostart_and_hotkey()
    {
        var platform = PlatformServices.ForLaunch(demo: true, customDataFolder: false);

        Assert.False(platform.Autostart.IsSupported);
        Assert.False(platform.Hotkey.IsSupported);
        Assert.Equal(OperatingSystem.IsWindows(), platform.Tray.IsSupported);
        platform.Hotkey.Dispose();
        platform.Tray.Dispose();
    }

    [Fact]
    public void A_custom_data_folder_never_touches_the_registry_but_keeps_the_hotkey()
    {
        var platform = PlatformServices.ForLaunch(demo: false, customDataFolder: true);

        Assert.False(platform.Autostart.IsSupported);
        Assert.Equal(OperatingSystem.IsWindows(), platform.Hotkey.IsSupported);
        platform.Hotkey.Dispose();
        platform.Tray.Dispose();
    }

    [Fact]
    public void The_default_location_gets_the_real_autostart_on_Windows()
    {
        var platform = PlatformServices.ForLaunch(demo: false, customDataFolder: false);

        Assert.Equal(OperatingSystem.IsWindows() && Environment.ProcessPath is not null, platform.Autostart.IsSupported);
        platform.Hotkey.Dispose();
        platform.Tray.Dispose();
    }

    [AvaloniaFact]
    public void Demo_flag_builds_demo_services_with_no_op_autostart_and_hotkey()
    {
        var app = AppServices.FromStartup(StartupFlags.Parse(["--demo"]));
        try
        {
            Assert.True(app.IsDemo);
            Assert.Equal(DemoData.Today, app.DayClock.Today);
            Assert.False(app.Platform.Autostart.IsSupported);
            Assert.False(app.Platform.Hotkey.IsSupported);
        }
        finally
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void Data_flag_opens_that_folder_without_registry_access()
    {
        using var folder = new TempFolder();
        var app = AppServices.FromStartup(StartupFlags.Parse(["--data", folder.Path]));
        try
        {
            Assert.False(app.IsDemo);
            Assert.Equal(folder.Path, app.Database!.Location.Folder);
            Assert.False(app.Platform.Autostart.IsSupported);
        }
        finally
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        Assert.True(File.Exists(folder.Location.DatabasePath));
    }

    [Fact]
    public void No_op_tray_is_unsupported_and_stays_hidden()
    {
        var tray = PlatformServices.NoOp().Tray;
        EventHandler handler = (_, _) => { };
        tray.OpenRequested += handler;
        tray.NewTaskRequested += handler;
        tray.QuitRequested += handler;

        tray.IsVisible = true;

        Assert.False(tray.IsSupported);
        Assert.False(tray.IsVisible);
        tray.Dispose();
        tray.Dispose();
    }

    [Fact]
    public void Lifecycle_options_default_to_a_normal_start_without_autostart()
    {
        var options = new LifecycleOptions();

        Assert.False(options.StartMinimized);
        Assert.False(options.ManageAutostart);
    }

    [Fact]
    public void Backups_kept_matches_the_settings_copy()
    {
        // "Keeps the last 7 days of backups next to your data."
        Assert.Equal(7, DataMaintenance.BackupsKept);
        Assert.True(AppSettings.Default.DailyBackup);
    }
}
