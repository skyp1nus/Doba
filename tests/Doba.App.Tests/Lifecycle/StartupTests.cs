using Avalonia.Headless.XUnit;
using Doba.App.Platform;
using Doba.App.Services;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Storage;

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
    public void Every_flag_is_parsed()
    {
        var flags = StartupFlags.Parse(["--perf", "--exit-after-first-frame", "--data", @"D:\x y", "--minimized", "--selfcheck", "--demo"]);

        Assert.Equal(new StartupFlags(Perf: true, ExitAfterFirstFrame: true, Demo: true, DataFolder: @"D:\x y", Minimized: true, SelfCheck: true), flags);
    }

    // A command line that is not understood must never fall through to the real data folder and Run key (the past
    // incident: a launch without the intended flag wrote %LOCALAPPDATA%\Doba and the HKCU Run value).
    [Theory]
    [InlineData("--whatever")]
    [InlineData("--dmeo")]
    [InlineData("-demo")]
    [InlineData("/demo")]
    [InlineData("demo")]
    [InlineData("--data=D:\\x")]
    [InlineData("")]
    public void Unknown_arguments_are_errors(string arg)
    {
        Assert.False(StartupFlags.TryParse([arg], out var flags, out var error));
        Assert.Contains(arg, error, StringComparison.Ordinal);
        Assert.Equal(StartupFlags.None, flags);
        Assert.Throws<ArgumentException>(() => StartupFlags.Parse([arg]));
    }

    public static TheoryData<string> DataWithoutFolder() => ["--data", "--data|", "--data|   ", "--data|--demo", "--minimized|--data"];

    [Theory]
    [MemberData(nameof(DataWithoutFolder))]
    public void Data_without_a_folder_is_an_error(string commandLine)
    {
        Assert.False(StartupFlags.TryParse(commandLine.Split('|'), out _, out var error));
        Assert.Contains("--data", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("--minimized", true)]
    [InlineData("--demo", false)]
    [InlineData("--selfcheck", false)]
    public void Only_launches_that_read_data_name_a_data_folder(string commandLine, bool readsData)
    {
        var flags = StartupFlags.Parse(commandLine.Length == 0 ? [] : commandLine.Split('|'));

        Assert.Equal(readsData, flags.DataFolderInUse is not null);
        if (readsData)
        {
            Assert.Equal(DataLocation.Default().Folder, flags.DataFolderInUse);
        }

        Assert.Equal(@"D:\x", StartupFlags.Parse(["--data", @"D:\x"]).DataFolderInUse);
    }

    [Theory]
    [InlineData("--demo")]
    [InlineData("--selfcheck")]
    public void Demo_and_selfcheck_are_not_the_default_location(string arg) =>
        Assert.False(StartupFlags.Parse([arg]).UsesDefaultDataLocation);

    [Fact]
    public void A_data_folder_is_not_the_default_location() =>
        Assert.False(StartupFlags.Parse(["--data", "x"]).UsesDefaultDataLocation);

    // The platform choice is checked on the plan: no test builds the Windows autostart, hotkey or tray (CLAUDE.md).
    [Fact]
    public void Demo_launch_uses_no_autostart_hotkey_tray_or_bridge() =>
        Assert.Equal(new PlatformPlan(Autostart: false, Hotkey: false, Tray: false, Bridge: false), PlatformPlan.For(demo: true, customDataFolder: false));

    [Fact]
    public void A_custom_data_folder_never_touches_the_registry_but_keeps_the_hotkey_tray_and_bridge() =>
        Assert.Equal(new PlatformPlan(Autostart: false, Hotkey: true, Tray: true, Bridge: true), PlatformPlan.For(demo: false, customDataFolder: true));

    [Fact]
    public void The_default_location_uses_everything() =>
        Assert.Equal(PlatformPlan.Full, PlatformPlan.For(demo: false, customDataFolder: false));

    [Fact]
    public void A_demo_platform_set_has_no_tray_so_closing_a_demo_window_quits()
    {
        // With an all-off plan only the read-only OS preferences and the click-only shell are the platform's own.
        var platform = PlatformServices.ForLaunch(demo: true, customDataFolder: false);

        Assert.False(platform.Autostart.IsSupported);
        Assert.False(platform.Hotkey.IsSupported);
        Assert.False(platform.Tray.IsSupported);
        Assert.Equal(PlatformServices.NoOp().Tray.GetType(), platform.Tray.GetType());
        Assert.False(platform.Bridge.IsSupported);
        Assert.Equal(PlatformServices.NoOp().Bridge.GetType(), platform.Bridge.GetType());
    }

    [AvaloniaFact]
    public void Demo_flag_builds_demo_services_with_the_demo_plan()
    {
        var plans = new List<PlatformPlan>();
        var app = AppServices.FromStartup(StartupFlags.Parse(["--demo"]), null, plan => { plans.Add(plan); return PlatformServices.NoOp(); });
        try
        {
            Assert.True(app.IsDemo);
            Assert.Equal(DemoData.Today, app.DayClock.Today);
            Assert.Equal([PlatformPlan.For(demo: true, customDataFolder: false)], plans);
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
        var plans = new List<PlatformPlan>();
        var app = AppServices.FromStartup(StartupFlags.Parse(["--data", folder.Path]), null, plan => { plans.Add(plan); return PlatformServices.NoOp(); });
        try
        {
            Assert.False(app.IsDemo);
            Assert.Equal(folder.Path, app.Database!.Location.Folder);
            Assert.Equal([new PlatformPlan(Autostart: false, Hotkey: true, Tray: true, Bridge: true)], plans);
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
