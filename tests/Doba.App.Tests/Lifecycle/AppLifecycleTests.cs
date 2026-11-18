using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Doba.App.Dialogs;
using Doba.App.Services;
using Doba.Core.Model;
using Doba.Core.Store;
using Doba.Core.Time;
using Doba.Storage;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Lifecycle;

public class AppLifecycleTests
{
    private static readonly DateOnly Day = new(2026, 11, 15);

    private static FixedTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));

    private static TaskItem Task(string title, DateOnly? date = null, bool done = false, DateOnly? end = null) =>
        new(0, title, string.Empty, date, end, null, 0, Priority.None, null, done);

    // ---- Carry-over and backup ------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Startup_runs_carry_over_and_backup_after_the_first_frame_when_both_settings_are_on()
    {
        using var folder = new TempFolder();
        var clock = Clock();
        TaskItem? overdue = null, span = null;
        using var h = LifecycleHarness.Disk(folder, clock, settings: s => s with { CarryOver = true, DailyBackup = true }, seed: app =>
        {
            overdue = app.Store.Add(Task("overdue", Day.AddDays(-2)));
            span = app.Store.Add(Task("span", Day.AddDays(-3), end: Day.AddDays(-1)));
        });

        h.Lifecycle.Maintenance.PendingBackup.Wait();

        Assert.Equal(Day, h.App.Store.Get(overdue!.Id)!.Date);
        Assert.Equal(Day.AddDays(-3), h.App.Store.Get(span!.Id)!.Date);
        Assert.True(File.Exists(Path.Combine(folder.Location.BackupFolder, Backup.FileName(Day))));
    }

    [AvaloniaFact]
    public void Nothing_is_carried_over_or_backed_up_before_the_first_frame()
    {
        using var folder = new TempFolder();
        var platform = new FakePlatform();
        var app = AppServices.Open(folder.Location, Clock(), platform.Services);
        var overdue = app.Store.Add(Task("overdue", Day.AddDays(-1)));
        var window = Doba.App.Shell.ShellComposition.CreateMainWindow(app, Avalonia.Application.Current!);
        var lifecycle = new AppLifecycle(app, window, new LifecycleOptions(), () => { });
        try
        {
            lifecycle.Start();

            Assert.Equal(Day.AddDays(-1), app.Store.Get(overdue.Id)!.Date);
            Assert.False(Directory.Exists(folder.Location.BackupFolder));
            Assert.False(platform.Tray.IsVisible);
        }
        finally
        {
            lifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Carry_over_and_backup_stay_off_when_their_settings_are_off()
    {
        using var folder = new TempFolder();
        using var h = LifecycleHarness.Disk(folder, Clock(), settings: s => s with { CarryOver = false, DailyBackup = false });
        var overdue = h.App.Store.Add(Task("overdue", Day.AddDays(-1)));

        h.Lifecycle.Maintenance.Run(Day);
        h.Settle();
        h.Lifecycle.Maintenance.PendingBackup.Wait();

        Assert.Equal(Day.AddDays(-1), h.App.Store.Get(overdue.Id)!.Date);
        Assert.False(Directory.Exists(folder.Location.BackupFolder));
    }

    [AvaloniaFact]
    public void Only_the_setting_that_is_on_runs()
    {
        using var folder = new TempFolder();
        using var h = LifecycleHarness.Disk(folder, Clock(), settings: s => s with { CarryOver = false, DailyBackup = true });
        var overdue = h.App.Store.Add(Task("overdue", Day.AddDays(-1)));

        h.Lifecycle.Maintenance.Run(Day);
        h.Lifecycle.Maintenance.PendingBackup.Wait();

        Assert.Equal(Day.AddDays(-1), h.App.Store.Get(overdue.Id)!.Date);
        Assert.True(File.Exists(Path.Combine(folder.Location.BackupFolder, Backup.FileName(Day))));
    }

    [AvaloniaFact]
    public void Demo_mode_never_carries_over_or_backs_up()
    {
        using var h = LifecycleHarness.Demo(settings: AppSettings.Default with { CarryOver = true, DailyBackup = true });
        var overdueBefore = CarryOver.Select(h.App.Store, h.App.DayClock.Today).Select(t => t.Id).ToList();
        Assert.NotEmpty(overdueBefore);

        h.Settle();
        h.Lifecycle.Maintenance.Run(h.App.DayClock.Today);

        Assert.False(h.Lifecycle.Maintenance.IsEnabled);
        Assert.Equal(overdueBefore, CarryOver.Select(h.App.Store, h.App.DayClock.Today).Select(t => t.Id));
        Assert.True(h.Lifecycle.Maintenance.PendingBackup.IsCompleted);
    }

    [AvaloniaFact]
    public void DayChanged_runs_carry_over_and_the_backup_for_the_new_day()
    {
        using var folder = new TempFolder();
        var clock = Clock();
        using var h = LifecycleHarness.Disk(folder, clock);
        var today = h.App.Store.Add(Task("left open today", Day));
        var finished = h.App.Store.Add(Task("finished today", Day, done: true));
        h.Settle();
        h.Lifecycle.Maintenance.PendingBackup.Wait();
        var batches = 0;
        h.App.Store.Changed += (_, e) => batches += e.Kind == TaskChangeKind.Batch ? 1 : 0;

        clock.Advance(TimeSpan.FromDays(1));
        h.App.DayClock.Check();
        h.Lifecycle.Maintenance.PendingBackup.Wait();

        Assert.Equal(Day.AddDays(1), h.App.Store.Get(today.Id)!.Date);
        Assert.Equal(Day, h.App.Store.Get(finished.Id)!.Date);
        Assert.Equal(1, batches);
        Assert.True(File.Exists(Path.Combine(folder.Location.BackupFolder, Backup.FileName(Day))));
        Assert.True(File.Exists(Path.Combine(folder.Location.BackupFolder, Backup.FileName(Day.AddDays(1)))));
    }

    [AvaloniaFact]
    public void Showing_the_window_after_a_night_asleep_rolls_the_day_over()
    {
        using var folder = new TempFolder();
        var clock = Clock();
        using var h = LifecycleHarness.Disk(folder, clock);
        var task = h.App.Store.Add(Task("yesterday's", Day));
        h.Window.Close(); // to the tray
        h.Settle();
        var days = new List<DateOnly>();
        h.App.DayClock.DayChanged += (_, d) => days.Add(d);

        clock.Advance(TimeSpan.FromDays(1));
        h.Platform.Tray.ClickIcon();
        h.Settle();

        Assert.Equal([Day.AddDays(1)], days);
        Assert.Equal(Day.AddDays(1), h.App.Store.Get(task.Id)!.Date);
    }

    [AvaloniaFact]
    public void Activating_the_window_checks_the_day_clock()
    {
        var clock = Clock();
        using var folder = new TempFolder();
        using var h = LifecycleHarness.Disk(folder, clock);
        var days = new List<DateOnly>();
        h.App.DayClock.DayChanged += (_, d) => days.Add(d);
        clock.Advance(TimeSpan.FromDays(1));

        h.Window.Activate();
        h.Settle();

        Assert.Equal([Day.AddDays(1)], days);
    }

    // ---- Flush ----------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Hiding_to_the_tray_flushes_pending_writes()
    {
        using var folder = new TempFolder();
        using var h = LifecycleHarness.Disk(folder, Clock(), settings: s => s with { KeepInTray = true });
        using var stall = WriterStall.Hold(folder.Location);
        h.App.Store.Add(Task("written on hide", Day));

        h.Window.Close();

        // The hide started a flush, and that flush is what waits for the held write (not a writer that happened to be quick).
        var flush = h.Lifecycle.LastFlush;
        Assert.True(h.Lifecycle.IsHiddenInTray);
        Assert.False(flush.IsCompleted);
        Assert.DoesNotContain("written on hide", folder.TitlesOnDisk());

        stall.Release();
        Assert.True(flush.Wait(TimeSpan.FromSeconds(20)));
        Assert.Contains("written on hide", folder.TitlesOnDisk());
    }

    [AvaloniaFact]
    public void A_save_still_in_its_exit_animation_is_written_before_the_hide_flush()
    {
        using var folder = new TempFolder();
        using var held = new HeldOverlayExit();
        using var h = LifecycleHarness.Disk(folder, Clock(), settings: s => s with { KeepInTray = true });
        h.Vm.OpenOverlay(new TaskDialogViewModel(h.Vm.Screens,
            Doba.Core.Editing.TaskDialog.NewDraft(Day, Day, Priority.None) with { Title = "saved, then hidden" }));
        h.Settle();
        var dialog = Assert.IsType<TaskDialogViewModel>(h.Vm.Overlay);
        using var stall = WriterStall.Hold(folder.Location);
        dialog.CommitCommand.Execute(null);
        Assert.True(held.IsRunning);                                                // the exit is still playing

        h.Window.Close();                                                           // to the tray at once

        Assert.Null(h.Vm.Overlay);
        Assert.Contains(h.App.Store.All, t => t.Title == "saved, then hidden");
        var flush = h.Lifecycle.LastFlush;
        Assert.False(flush.IsCompleted);
        stall.Release();
        Assert.True(flush.Wait(TimeSpan.FromSeconds(20)));
        Assert.Contains("saved, then hidden", folder.TitlesOnDisk());
    }

    [AvaloniaFact]
    public void A_save_still_in_its_exit_animation_survives_closing_the_window_and_quitting()
    {
        using var folder = new TempFolder();
        using var held = new HeldOverlayExit();
        using (var h = LifecycleHarness.Disk(folder, Clock(), settings: s => s with { KeepInTray = false }))
        {
            h.Vm.OpenOverlay(new TaskDialogViewModel(h.Vm.Screens,
                Doba.Core.Editing.TaskDialog.NewDraft(Day, Day, Priority.None) with { Title = "saved, then closed" }));
            h.Settle();
            Assert.IsType<TaskDialogViewModel>(h.Vm.Overlay).CommitCommand.Execute(null);
            Assert.True(held.IsRunning);

            h.Window.Close();                                                       // tray off: quits
            h.Settle();
            Assert.Equal(1, h.ShutdownCalls);
            h.Exit();
        }

        Assert.Contains("saved, then closed", folder.Reopen().Tasks.Select(t => t.Title));
    }

    [AvaloniaFact]
    public void A_dialog_that_is_merely_open_when_the_app_quits_writes_nothing()
    {
        using var folder = new TempFolder();
        using (var h = LifecycleHarness.Disk(folder, Clock(), settings: s => s with { KeepInTray = false }))
        {
            h.Vm.OpenOverlay(new TaskDialogViewModel(h.Vm.Screens,
                Doba.Core.Editing.TaskDialog.NewDraft(Day, Day, Priority.None) with { Title = "typed, never saved" }));
            h.Settle();

            h.Window.Close();
            h.Settle();
            Assert.Null(h.Vm.Overlay);
            h.Exit();
        }

        Assert.DoesNotContain("typed, never saved", folder.Reopen().Tasks.Select(t => t.Title));
    }

    [AvaloniaFact]
    public void Quit_from_the_tray_shuts_down_and_exit_flushes_everything()
    {
        using var folder = new TempFolder();
        using (var h = LifecycleHarness.Disk(folder, Clock()))
        {
            var added = h.App.Store.Add(Task("before quit", Day));
            h.App.Store.ToggleDone(added.Id);

            h.Platform.Tray.ClickQuit();
            Assert.Equal(1, h.ShutdownCalls);
            Assert.False(h.Platform.Tray.IsVisible);
            h.Exit();
        }

        var reopened = folder.Reopen();
        var task = Assert.Single(reopened.Tasks, t => t.Title == "before quit");
        Assert.True(task.Done);
    }

    [AvaloniaFact]
    public void Exit_flushes_and_closes_the_database_once()
    {
        using var folder = new TempFolder();
        var h = LifecycleHarness.Disk(folder, Clock());
        h.App.Store.Add(Task("on exit", Day));

        h.Exit();
        h.Lifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult(); // second Exit: nothing happens
        h.App.DisposeAsync().AsTask().GetAwaiter().GetResult();
        h.Dispose();

        Assert.Contains("on exit", folder.Reopen().Tasks.Select(t => t.Title));
        Assert.True(h.Platform.Tray.IsDisposed);
    }

    [AvaloniaFact]
    public void Session_end_flushes_synchronously()
    {
        using var folder = new TempFolder();
        using var h = LifecycleHarness.Disk(folder, Clock());
        using var stall = WriterStall.Hold(folder.Location);
        h.App.Store.Add(Task("session end", Day));
        Assert.DoesNotContain("session end", folder.TitlesOnDisk());

        // The write is held for a while; only a handler that waits for it finds it on disk when it returns.
        var release = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(400).ConfigureAwait(false);
            stall.Release();
        });
        h.Lifecycle.OnShutdownRequested(null, new Avalonia.Controls.ApplicationLifetimes.ShutdownRequestedEventArgs());

        Assert.Contains("session end", folder.TitlesOnDisk());
        release.Wait();
    }

    [AvaloniaFact]
    public void Settings_survive_a_restart()
    {
        using var folder = new TempFolder();
        var changed = AppSettings.Default with
        {
            Accent = Accent.Sage,
            WindowTransparency = 40,
            WeekStart = DayOfWeek.Sunday,
            LaunchView = LaunchView.Week,
            CarryOver = false,
            KeepInTray = false,
            LaunchAtStartup = false,
            DailyBackup = false,
            CompletionSound = true,
        };
        using (var h = LifecycleHarness.Disk(folder, Clock()))
        {
            h.App.Settings.Update(_ => changed);
            h.Exit();
        }

        Assert.Equal(changed, folder.Reopen().Settings);
    }

    // ---- Close, tray, start minimised ---------------------------------------------------------------------------

    [AvaloniaFact]
    public void Closing_with_the_tray_on_hides_the_window_and_keeps_everything_running()
    {
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Demo(platform, settings: AppSettings.Default with { KeepInTray = true });
        Assert.True(platform.Tray.IsVisible);
        var minutes = 0;
        h.App.DayClock.MinuteTick += (_, _) => minutes++;

        h.Window.Close();
        h.Settle();

        Assert.False(h.Window.IsVisible);
        Assert.True(h.Lifecycle.IsHiddenInTray);
        Assert.Equal(0, h.ShutdownCalls);
        Assert.True(platform.Tray.IsVisible);
        Assert.True(platform.Hotkey.IsRegistered);
        Assert.NotNull(h.Window.ViewModel);

        // The day clock still ticks and the hotkey still works while hidden.
        ((FixedTimeProvider)h.App.Clock).Advance(TimeSpan.FromMinutes(1));
        h.App.DayClock.Check();
        Assert.Equal(1, minutes);
        platform.Hotkey.Press();
        h.Settle();
        Assert.True(h.Window.IsVisible);
        Assert.False(h.Lifecycle.IsHiddenInTray);
    }

    [AvaloniaFact]
    public void Closing_with_the_tray_off_quits()
    {
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Demo(platform, settings: AppSettings.Default with { KeepInTray = false });
        Assert.False(platform.Tray.IsVisible);

        h.Window.Close();
        h.Settle();

        Assert.Equal(1, h.ShutdownCalls);
        Assert.False(h.Lifecycle.IsHiddenInTray);
    }

    [AvaloniaFact]
    public void Closing_without_a_tray_quits_even_with_the_setting_on()
    {
        var platform = new FakePlatform { Tray = new FakeTray(supported: false) };
        using var h = LifecycleHarness.Demo(platform, settings: AppSettings.Default with { KeepInTray = true });

        h.Window.Close();
        h.Settle();

        Assert.Equal(1, h.ShutdownCalls);
    }

    [AvaloniaFact]
    public void The_tray_setting_shows_and_removes_the_tray_icon()
    {
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Demo(platform, settings: AppSettings.Default with { KeepInTray = true });
        Assert.True(platform.Tray.IsVisible);

        h.App.Settings.Update(s => s with { KeepInTray = false });
        Assert.False(platform.Tray.IsVisible);

        h.App.Settings.Update(s => s with { KeepInTray = true });
        Assert.True(platform.Tray.IsVisible);
    }

    [AvaloniaFact]
    public void Minimized_start_with_the_tray_on_stays_hidden_in_the_tray()
    {
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Demo(platform, new LifecycleOptions(StartMinimized: true),
            AppSettings.Default with { KeepInTray = true });

        Assert.False(h.Lifecycle.ShowsWindowAtStart);
        Assert.False(h.Window.IsVisible);
        Assert.True(h.Lifecycle.IsHiddenInTray);
        Assert.True(platform.Tray.IsVisible);
        Assert.True(platform.Hotkey.IsRegistered);

        platform.Tray.ClickIcon();
        h.Settle();
        Assert.True(h.Window.IsVisible);
        Assert.Equal(WindowState.Normal, h.Window.WindowState);
        Assert.Null(h.Vm.Overlay);
    }

    [AvaloniaFact]
    public void Minimized_start_with_the_tray_off_starts_minimised()
    {
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Demo(platform, new LifecycleOptions(StartMinimized: true),
            AppSettings.Default with { KeepInTray = false });

        Assert.True(h.Lifecycle.ShowsWindowAtStart);
        Assert.Equal(WindowState.Minimized, h.Window.WindowState);

        h.Lifecycle.ShowWindow();
        Assert.Equal(WindowState.Normal, h.Window.WindowState);
    }

    // ---- Hotkey -------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Hotkey_is_registered_once_the_window_has_opened()
    {
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Demo(platform);

        Assert.Equal(1, platform.Hotkey.RegisterCalls);
        Assert.True(h.Lifecycle.IsHotkeyRegistered);

        h.Window.Close(); // hide
        platform.Tray.ClickIcon(); // show again: Opened again, no second registration
        h.Settle();
        Assert.Equal(1, platform.Hotkey.RegisterCalls);
    }

    [AvaloniaFact]
    public void Hotkey_press_shows_the_hidden_window_and_opens_New_task()
    {
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Demo(platform);
        h.Window.Close();
        h.Settle();
        Assert.False(h.Window.IsVisible);

        platform.Hotkey.Press();
        h.Settle();

        Assert.True(h.Window.IsVisible);
        Assert.IsType<TaskDialogViewModel>(h.Vm.Overlay);
    }

    [AvaloniaFact]
    public void Hotkey_press_keeps_an_open_task_dialog()
    {
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Demo(platform);
        h.Vm.NewTask();
        var open = h.Vm.Overlay;

        platform.Hotkey.Press();
        h.Settle();

        Assert.Same(open, h.Vm.Overlay);
    }

    [AvaloniaFact]
    public void Tray_New_task_shows_the_window_and_opens_New_task()
    {
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Demo(platform);
        h.Window.Close();

        platform.Tray.ClickNewTask();
        h.Settle();

        Assert.True(h.Window.IsVisible);
        Assert.IsType<TaskDialogViewModel>(h.Vm.Overlay);
    }

    [AvaloniaFact]
    public void A_refused_hotkey_does_not_stop_the_app()
    {
        var platform = new FakePlatform { Hotkey = new FakeHotkey(accepts: false) };
        using var h = LifecycleHarness.Demo(platform);

        Assert.False(h.Lifecycle.IsHotkeyRegistered);
        Assert.True(h.Window.IsVisible);
        Assert.True(platform.Tray.IsVisible);
    }

    // ---- Autostart ----------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Autostart_is_reconciled_on_the_default_data_location()
    {
        var platform = new FakePlatform { Autostart = new FakeAutostart(enabled: false) };
        using var h = LifecycleHarness.Demo(platform, new LifecycleOptions(ManageAutostart: true),
            AppSettings.Default with { LaunchAtStartup = true });

        Assert.Equal([true], platform.Autostart.Writes);

        h.App.Settings.Update(s => s with { LaunchAtStartup = false });
        Assert.Equal([true, false], platform.Autostart.Writes);

        // Already matching (e.g. the Settings row wrote it first): nothing more is written.
        platform.Autostart.IsEnabled = true;
        h.App.Settings.Update(s => s with { LaunchAtStartup = true });
        Assert.Equal([true, false], platform.Autostart.Writes);
    }

    [AvaloniaFact]
    public void Autostart_off_removes_an_entry_left_on()
    {
        var platform = new FakePlatform { Autostart = new FakeAutostart(enabled: true) };
        using var h = LifecycleHarness.Demo(platform, new LifecycleOptions(ManageAutostart: true),
            AppSettings.Default with { LaunchAtStartup = false });

        Assert.Equal([false], platform.Autostart.Writes);
    }

    [AvaloniaFact]
    public void Autostart_is_never_written_away_from_the_default_data_location()
    {
        var platform = new FakePlatform { Autostart = new FakeAutostart(enabled: false) };
        using var h = LifecycleHarness.Demo(platform, new LifecycleOptions(ManageAutostart: false),
            AppSettings.Default with { LaunchAtStartup = true });

        h.App.Settings.Update(s => s with { LaunchAtStartup = false });
        h.App.Settings.Update(s => s with { LaunchAtStartup = true });

        Assert.Empty(platform.Autostart.Writes);
    }

    // ---- OS animations and backdrop -------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Motion_follows_the_OS_animation_setting_and_its_changes()
    {
        var platform = new FakePlatform { Preferences = new FakePreferences(animations: false) };
        using var h = LifecycleHarness.Demo(platform);

        Assert.False(AppMotion.SystemAnimationsEnabled);
        Assert.Equal(1, platform.Preferences.Watches);

        platform.Preferences.Change(true);
        Assert.True(AppMotion.SystemAnimationsEnabled);

        platform.Preferences.Change(false);
        Assert.False(AppMotion.SystemAnimationsEnabled);
        Assert.Equal(TimeSpan.Zero, AppMotion.Ms(AppMotion.Section));
    }

    [AvaloniaFact]
    public void Without_a_system_backdrop_the_painted_fallback_stays()
    {
        var platform = new FakePlatform { Preferences = new FakePreferences(backdrop: false) };
        using var h = LifecycleHarness.Demo(platform);

        Assert.Empty(h.Window.TransparencyLevelHint);
        Assert.False(h.Window.HasSystemBackdrop);
        Assert.True(h.Window.PaintedBackdrop.IsVisible);
        Assert.NotNull(h.Window.Background);
    }

    [AvaloniaFact]
    public void A_backdrop_request_the_platform_cannot_honour_keeps_the_painted_fallback()
    {
        // Headless has no Mica: the hint is set, the actual level stays None, so the window renders as before.
        var platform = new FakePlatform { Preferences = new FakePreferences(backdrop: true, mica: true) };
        using var h = LifecycleHarness.Demo(platform);

        Assert.Equal([WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None],
            h.Window.TransparencyLevelHint);
        Assert.Equal(WindowTransparencyLevel.None, h.Window.ActualTransparencyLevel);
        Assert.False(h.Window.HasSystemBackdrop);
        Assert.True(h.Window.PaintedBackdrop.IsVisible);
        Assert.NotNull(h.Window.Background);
    }
}
