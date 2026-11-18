using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Doba.App.Platform;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.App.Tests.Lifecycle;
using Doba.App.Theme;
using Doba.Core.Model;
using Doba.Core.Store;
using Doba.Core.Time;
using Doba.Storage;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Performance;

/// <summary>
/// The startup path in the order App.axaml.cs runs it, on recording fakes: nothing that can wait (tray icon, autostart
/// reconcile, carry-over, backup) happens before the first frame, and all of it happens right after. Also the
/// database preload that overlaps Avalonia's start, and the UI-thread release of platform objects on exit.
/// </summary>
public sealed class StartupOrderTests
{
    private static readonly DateOnly Day = new(2026, 11, 15);

    private static FixedTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));

    [AvaloniaFact]
    public void Deferred_startup_work_runs_only_after_the_first_frame()
    {
        using var folder = new TempFolder();
        var log = new List<string>();
        var platform = new RecordingPlatform(log, autostartEnabled: true);
        var app = AppServices.Open(folder.Location, Clock(), platform.Services);
        app.Settings.Update(s => s with { CarryOver = true, DailyBackup = true, KeepInTray = true, LaunchAtStartup = false });
        var overdue = app.Store.Add(new TaskItem(0, "overdue", string.Empty, Day.AddDays(-1), null, null, 0, Priority.None, null, false));
        app.Store.Changed += (_, e) => log.Add("store:" + e.Kind);
        var animationsWere = AppMotion.SystemAnimationsEnabled;

        var window = ShellComposition.CreateMainWindow(app, Application.Current!);
        var lifecycle = new AppLifecycle(app, window, new LifecycleOptions(ManageAutostart: true), () => { });
        try
        {
            lifecycle.Start();
            log.Add("started");
            window.Show();
            log.Add("shown");
            window.RequestAnimationFrame(_ => log.Add("first-frame"));

            // Start and Show did nothing that can wait: the only platform call so far is the hotkey, registered when
            // the window opened (as decided), and the data is untouched.
            Assert.Equal(new[] { "started", "hotkey", "shown" }, log);
            Assert.Equal(Day.AddDays(-1), app.Store.Get(overdue.Id)!.Date);
            Assert.False(Directory.Exists(folder.Location.BackupFolder));

            // The first frame: its callbacks run, then the work posted behind it.
            for (var i = 0; i < 3; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }

            lifecycle.Maintenance.PendingBackup.Wait(TestContext.Current.CancellationToken);

            string[] deferred = ["tray:True", "autostart:False", "store:Batch"];
            var frame = log.IndexOf("first-frame");
            Assert.True(frame > log.IndexOf("shown"), string.Join(", ", log));
            Assert.All(deferred, entry => Assert.True(log.IndexOf(entry) > frame, entry + " in " + string.Join(", ", log)));
            Assert.Equal(Day, app.Store.Get(overdue.Id)!.Date);
            Assert.True(File.Exists(Path.Combine(folder.Location.BackupFolder, Backup.FileName(Day))));
        }
        finally
        {
            lifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            window.Close();
            Dispatcher.UIThread.RunJobs();
            new ThemeService(Application.Current!).Apply(AppSettings.Default);
            AppMotion.SystemAnimationsEnabled = animationsWere;
        }
    }

    [AvaloniaFact]
    public void Quitting_while_a_backup_runs_releases_the_tray_and_hotkey_on_the_UI_thread()
    {
        using var folder = new TempFolder();
        var platform = new RecordingPlatform([], autostartEnabled: false);
        var app = AppServices.Open(folder.Location, Clock(), platform.Services);
        app.Settings.Update(s => s with { CarryOver = false, DailyBackup = true, KeepInTray = true });
        var animationsWere = AppMotion.SystemAnimationsEnabled;

        // The writer cannot commit, so the backup (queued behind a write) is still running when the app quits.
        using var stall = WriterStall.Hold(folder.Location);
        app.Store.Add(new TaskItem(0, "queued", string.Empty, Day, null, null, 0, Priority.None, null, false));
        var window = ShellComposition.CreateMainWindow(app, Application.Current!);
        var lifecycle = new AppLifecycle(app, window, new LifecycleOptions(), () => { });
        try
        {
            lifecycle.Start();
            window.Show();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.False(lifecycle.Maintenance.PendingBackup.IsCompleted);

            var release = Task.Run(async () =>
            {
                await Task.Delay(200);
                stall.Release();
            }, TestContext.Current.CancellationToken);

            // What the desktop lifetime's Exit does, on the UI thread: wait for the backup, flush, close.
            lifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            release.GetAwaiter().GetResult();

            Assert.True(platform.Tray.IsDisposed);
            Assert.True(platform.Tray.DisposedOnUiThread);
            Assert.True(platform.Hotkey.DisposedOnUiThread);
            Assert.Equal(new[] { "queued" }, folder.TitlesOnDisk());
        }
        finally
        {
            stall.Release();
            window.Close();
            Dispatcher.UIThread.RunJobs();
            new ThemeService(Application.Current!).Apply(AppSettings.Default);
            AppMotion.SystemAnimationsEnabled = animationsWere;
        }
    }

    [Theory]
    [InlineData("--demo")]
    [InlineData("--selfcheck")]
    public void Demo_launches_preload_nothing(string flag) =>
        Assert.Null(AppServices.StartPreload(StartupFlags.Parse([flag])));

    [AvaloniaFact]
    public void The_preloaded_database_is_the_one_the_app_uses()
    {
        using var folder = new TempFolder();
        var flags = StartupFlags.Parse(["--data", folder.Path]);

        var preload = AppServices.StartPreload(flags);
        Assert.NotNull(preload);
        var loaded = preload.GetAwaiter().GetResult();
        var app = AppServices.FromStartup(flags, preload);
        try
        {
            Assert.Same(loaded.Database, app.Database);
            Assert.Same(loaded.Store, app.Store);
            Assert.False(app.Platform.Autostart.IsSupported);
            Assert.True(File.Exists(folder.Location.DatabasePath));
        }
        finally
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void A_failed_preload_surfaces_the_original_exception_at_startup()
    {
        using var folder = new TempFolder();
        // A file where the data folder should be: Directory.CreateDirectory fails.
        var blocked = Path.Combine(folder.Path, "not-a-folder");
        File.WriteAllText(blocked, "x");
        var flags = StartupFlags.Parse(["--data", blocked]);

        var preload = AppServices.StartPreload(flags);

        Assert.ThrowsAny<IOException>(() => AppServices.FromStartup(flags, preload));
    }

    /// <summary>Platform fakes that log what they are asked to do and on which thread they are released.</summary>
    private sealed class RecordingPlatform(List<string> log, bool autostartEnabled)
    {
        public LoggingTray Tray { get; } = new(log);

        public LoggingHotkey Hotkey { get; } = new(log);

        public PlatformServices Services => PlatformServices.NoOp() with
        {
            Autostart = new LoggingAutostart(log, autostartEnabled),
            Hotkey = Hotkey,
            Tray = Tray,
        };
    }

    private sealed class LoggingAutostart(List<string> log, bool enabled) : IAutostart
    {
        public bool IsSupported => true;

        public bool IsEnabled { get; private set; } = enabled;

        public void SetEnabled(bool value)
        {
            log.Add("autostart:" + value);
            IsEnabled = value;
        }
    }

    internal sealed class LoggingHotkey(List<string> log) : IGlobalHotkey
    {
        public bool DisposedOnUiThread { get; private set; }

        public bool IsSupported => true;

        public event EventHandler? Pressed { add { } remove { } }

        public bool Register(TopLevel owner)
        {
            log.Add("hotkey");
            return true;
        }

        public void Dispose() => DisposedOnUiThread = Dispatcher.UIThread.CheckAccess();
    }

    internal sealed class LoggingTray(List<string> log) : ITrayIcon
    {
        private bool _visible;

        public bool IsDisposed { get; private set; }

        public bool DisposedOnUiThread { get; private set; }

        public bool IsSupported => true;

        public bool IsVisible
        {
            get => _visible;
            set
            {
                _visible = value;
                log.Add("tray:" + value);
            }
        }

        public event EventHandler? OpenRequested { add { } remove { } }

        public event EventHandler? NewTaskRequested { add { } remove { } }

        public event EventHandler? QuitRequested { add { } remove { } }

        public void Dispose()
        {
            IsDisposed = true;
            DisposedOnUiThread = Dispatcher.UIThread.CheckAccess();
        }
    }
}
