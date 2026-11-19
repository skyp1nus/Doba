using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Doba.App.Platform;
using Doba.App.Services;
using Doba.Bridge.Wire;
using Doba.Core.Model;
using Doba.Core.Time;

namespace Doba.App.Tests.Lifecycle;

/// <summary>
/// The Claude bridge follows Settings → Data → Claude access (ClaudeAccessService inside AppLifecycle), on a
/// recording bridge: no pipe is opened here. The real pipe is covered by Doba.Bridge.Tests.
/// </summary>
public class ClaudeAccessTests
{
    private static readonly DateOnly Day = new(2026, 11, 15);

    private static FixedTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));

    private static TaskItem Task(string title, DateOnly? date = null) =>
        new(0, title, string.Empty, date, null, null, 0, Priority.None, null, false);

    private static Func<AppSettings, AppSettings> AccessOn => s => s with { ClaudeAccess = true };

    [AvaloniaFact]
    public void Access_on_opens_the_bridge_on_the_data_folder_after_the_first_frame_and_after_carry_over()
    {
        using var folder = new TempFolder();
        var platform = new FakePlatform();
        TaskItem? overdue = null;
        DateOnly? dateWhenStarted = null;
        using var h = LifecycleHarness.Disk(folder, Clock(), platform, settings: s => AccessOn(s) with { CarryOver = true }, seed: app =>
        {
            overdue = app.Store.Add(Task("overdue", Day.AddDays(-2)));
            platform.Bridge.OnStart = () => dateWhenStarted = app.Store.Get(overdue.Id)!.Date;
        });

        Assert.Equal([folder.Path], platform.Bridge.Starts);
        Assert.True(platform.Bridge.IsRunning);
        Assert.True(h.Lifecycle.ClaudeAccess.IsListening);
        Assert.Equal(Day, dateWhenStarted); // Claude never sees yesterday's overdue task still in place
    }

    [AvaloniaFact]
    public void Nothing_listens_before_the_first_frame()
    {
        using var folder = new TempFolder();
        var platform = new FakePlatform();
        var app = AppServices.Open(folder.Location, Clock(), platform.Services);
        app.Settings.Update(AccessOn);
        var window = Doba.App.Shell.ShellComposition.CreateMainWindow(app, Avalonia.Application.Current!);
        var lifecycle = new AppLifecycle(app, window, new LifecycleOptions(), () => { });
        try
        {
            lifecycle.Start();
            Assert.Empty(platform.Bridge.Starts);
        }
        finally
        {
            lifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            window.Close();
        }

        Assert.Empty(platform.Bridge.Starts);
    }

    [AvaloniaFact]
    public void Access_off_by_default_opens_nothing()
    {
        using var folder = new TempFolder();
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Disk(folder, Clock(), platform);

        Assert.False(h.App.Settings.Current.ClaudeAccess);
        Assert.Empty(platform.Bridge.Starts);
        Assert.False(h.Lifecycle.ClaudeAccess.IsListening);
    }

    [AvaloniaFact]
    public void Turning_access_on_and_off_opens_and_closes_the_bridge()
    {
        using var folder = new TempFolder();
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Disk(folder, Clock(), platform);

        h.App.Settings.Update(AccessOn);
        Assert.True(platform.Bridge.IsRunning);
        Assert.Single(platform.Bridge.Starts);

        // Other settings (the level included: it is read per request) leave the pipe alone.
        h.App.Settings.Update(s => s with { ClaudeAccessLevel = ClaudeAccessLevel.ReadWrite, Accent = Accent.Sage });
        Assert.Single(platform.Bridge.Starts);
        Assert.Equal(0, platform.Bridge.Stops);

        h.App.Settings.Update(s => s with { ClaudeAccess = false });
        Assert.False(platform.Bridge.IsRunning);
        Assert.Equal(1, platform.Bridge.Stops);

        h.App.Settings.Update(AccessOn);
        Assert.True(platform.Bridge.IsRunning);
        Assert.Equal([folder.Path, folder.Path], platform.Bridge.Starts);
    }

    [AvaloniaFact]
    public void A_settings_change_from_another_thread_is_applied_on_the_ui_thread()
    {
        using var folder = new TempFolder();
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Disk(folder, Clock(), platform);
        var startedOnUiThread = false;
        platform.Bridge.OnStart = () => startedOnUiThread = Dispatcher.UIThread.CheckAccess();

        var other = new Thread(() => h.App.Settings.Update(AccessOn));
        other.Start();
        other.Join();
        Assert.Empty(platform.Bridge.Starts);
        h.Settle();

        Assert.Single(platform.Bridge.Starts);
        Assert.True(startedOnUiThread);
    }

    [AvaloniaFact]
    public void Demo_never_listens()
    {
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Demo(platform, settings: AppSettings.Default with { ClaudeAccess = true });

        h.App.Settings.Update(s => s with { ClaudeAccess = false });
        h.App.Settings.Update(AccessOn);
        h.Settle();

        Assert.Empty(platform.Bridge.Starts);
        Assert.False(h.Lifecycle.ClaudeAccess.IsListening);
    }

    [AvaloniaFact]
    public void A_plan_without_a_bridge_never_listens()
    {
        using var folder = new TempFolder();
        var platform = new FakePlatform { Bridge = new FakeBridge(supported: false) };
        using var h = LifecycleHarness.Disk(folder, Clock(), platform, settings: AccessOn);

        h.App.Settings.Update(s => s with { ClaudeAccess = false });
        h.App.Settings.Update(AccessOn);

        Assert.Empty(platform.Bridge.Starts);
        Assert.False(h.Lifecycle.ClaudeAccess.IsListening);
    }

    [AvaloniaFact]
    public void The_no_op_platform_set_never_listens()
    {
        using var folder = new TempFolder();
        var app = AppServices.Open(folder.Location, Clock(), PlatformServices.NoOp());
        app.Settings.Update(AccessOn);
        var window = Doba.App.Shell.ShellComposition.CreateMainWindow(app, Avalonia.Application.Current!);
        var lifecycle = new AppLifecycle(app, window, new LifecycleOptions(), () => { });
        try
        {
            lifecycle.Start();
            window.Show();
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }

            Dispatcher.UIThread.RunJobs();
            Assert.False(lifecycle.ClaudeAccess.IsListening);
            Assert.False(app.Platform.Bridge.IsRunning);
        }
        finally
        {
            lifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Exit_closes_the_bridge_first_while_everything_else_is_still_open()
    {
        using var folder = new TempFolder();
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Disk(folder, Clock(), platform, settings: AccessOn);
        bool? trayDisposed = null, hotkeyDisposed = null;
        platform.Bridge.OnStop = () =>
        {
            trayDisposed = platform.Tray.IsDisposed;
            hotkeyDisposed = platform.Hotkey.IsDisposed;
        };

        h.Exit();

        Assert.False(platform.Bridge.IsRunning);
        Assert.False(trayDisposed);
        Assert.False(hotkeyDisposed);
        Assert.True(platform.Bridge.IsDisposed); // released with the other platform parts afterwards

        // Nothing re-opens it once the app is going down.
        h.Lifecycle.ClaudeAccess.Start();
        Assert.Single(platform.Bridge.Starts);
    }

    [AvaloniaFact]
    public void Session_end_closes_the_bridge_before_flushing()
    {
        using var folder = new TempFolder();
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Disk(folder, Clock(), platform, settings: AccessOn);
        Assert.True(platform.Bridge.IsRunning);

        h.Lifecycle.OnShutdownRequested(null, new ShutdownRequestedEventArgs());

        Assert.False(platform.Bridge.IsRunning);
        Assert.False(h.Lifecycle.ClaudeAccess.IsListening);
        h.App.Settings.Update(s => s with { ClaudeAccess = false });
        h.App.Settings.Update(AccessOn);
        Assert.Single(platform.Bridge.Starts);
    }

    [AvaloniaFact]
    public void A_bridge_that_cannot_open_its_pipe_is_logged_and_the_app_carries_on()
    {
        using var folder = new TempFolder();
        var platform = new FakePlatform();
        platform.Bridge.OnStart = () => throw new IOException("All pipe instances are busy.");
        using var h = LifecycleHarness.Disk(folder, Clock(), platform, settings: AccessOn);

        Assert.False(h.Lifecycle.ClaudeAccess.IsListening);
        Assert.True(h.Window.IsVisible);
        Assert.Contains("claude: the bridge pipe could not be opened: All pipe instances are busy.",
            File.ReadAllText(DataLog.PathIn(folder.Path)), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_request_never_throws_on_the_ui_thread()
    {
        using var folder = new TempFolder();
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Disk(folder, Clock(), platform, settings: AccessOn);

        var response = platform.Bridge.Execute!(new BridgeRequest { Protocol = 1, Client = "test", Tool = "no_such_tool" });

        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
    }

    [AvaloniaFact]
    public void The_open_edit_id_is_the_task_in_the_edit_dialog_only()
    {
        using var folder = new TempFolder();
        using var h = LifecycleHarness.Disk(folder, Clock());
        var task = h.App.Store.Add(Task("open me", Day));
        Assert.Null(h.Lifecycle.OpenEditId());

        h.Vm.NewTask();
        h.Settle();
        Assert.Null(h.Lifecycle.OpenEditId());

        h.Vm.EditTask(task.Id);
        h.Settle();
        Assert.Equal(task.Id, h.Lifecycle.OpenEditId());

        h.Vm.CloseOverlay();
        Assert.Null(h.Lifecycle.OpenEditId());
    }
}
