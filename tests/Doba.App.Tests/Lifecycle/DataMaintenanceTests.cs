using Avalonia.Headless.XUnit;
using Doba.App.Platform;
using Doba.App.Services;
using Doba.Core.Model;
using Doba.Core.Store;
using Doba.Core.Time;

namespace Doba.App.Tests.Lifecycle;

public class DataMaintenanceTests
{
    private static readonly DateOnly Day = new(2026, 11, 15);

    private static TaskItem Task(string title, DateOnly? date) => new(0, title, string.Empty, date, null, null, 0, Priority.None, null, false);

    private sealed class Rig : IDisposable
    {
        public Rig(Func<AppSettings, AppSettings>? settings = null, bool failingBackup = false)
        {
            Clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 15, 23, 59, 0, TimeSpan.Zero));
            App = AppServices.Open(Folder.Location, Clock, PlatformServices.NoOp());
            if (settings is not null)
            {
                App.Settings.Update(settings);
            }

            Maintenance = new DataMaintenance(App, (day, _) =>
            {
                lock (Backups)
                {
                    Backups.Add(day);
                }

                return failingBackup ? System.Threading.Tasks.Task.FromException(new IOException("disk full")) : System.Threading.Tasks.Task.CompletedTask;
            });
        }

        public TempFolder Folder { get; } = new();

        public FixedTimeProvider Clock { get; }

        public AppServices App { get; }

        public DataMaintenance Maintenance { get; }

        public List<DateOnly> Backups { get; } = [];

        public void Dispose()
        {
            Maintenance.DisposeAsync().AsTask().GetAwaiter().GetResult();
            App.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Folder.Dispose();
        }
    }

    [AvaloniaFact]
    public void Start_runs_both_chores_once_for_today()
    {
        using var rig = new Rig();
        var overdue = rig.App.Store.Add(Task("overdue", Day.AddDays(-1)));

        rig.Maintenance.Start();
        rig.Maintenance.Start(); // idempotent
        rig.Maintenance.PendingBackup.Wait();

        Assert.Equal([Day], rig.Backups);
        Assert.Equal(Day, rig.App.Store.Get(overdue.Id)!.Date);
    }

    [AvaloniaFact]
    public void Midnight_runs_both_chores_for_the_new_day()
    {
        using var rig = new Rig();
        rig.Maintenance.Start();
        var open = rig.App.Store.Add(Task("open", Day));

        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        rig.App.DayClock.Check();
        rig.Maintenance.PendingBackup.Wait();

        Assert.Equal([Day, Day.AddDays(1)], rig.Backups);
        Assert.Equal(Day.AddDays(1), rig.App.Store.Get(open.Id)!.Date);
    }

    [AvaloniaFact]
    public void Settings_are_read_at_each_run()
    {
        using var rig = new Rig(s => s with { CarryOver = false, DailyBackup = false });
        rig.Maintenance.Start();
        var open = rig.App.Store.Add(Task("open", Day.AddDays(-1)));
        rig.Maintenance.PendingBackup.Wait();
        Assert.Empty(rig.Backups);
        Assert.Equal(Day.AddDays(-1), rig.App.Store.Get(open.Id)!.Date);

        rig.App.Settings.Update(s => s with { CarryOver = true, DailyBackup = true });
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        rig.App.DayClock.Check();
        rig.Maintenance.PendingBackup.Wait();

        Assert.Equal([Day.AddDays(1)], rig.Backups);
        Assert.Equal(Day.AddDays(1), rig.App.Store.Get(open.Id)!.Date);
    }

    [AvaloniaFact]
    public void A_failed_backup_is_swallowed_and_the_next_day_tries_again()
    {
        using var rig = new Rig(failingBackup: true);
        rig.Maintenance.Start();
        rig.Maintenance.PendingBackup.Wait();
        Assert.True(rig.Maintenance.PendingBackup.IsCompletedSuccessfully);

        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        rig.App.DayClock.Check();
        rig.Maintenance.PendingBackup.Wait();

        Assert.Equal([Day, Day.AddDays(1)], rig.Backups);
    }

    [AvaloniaFact]
    public void After_dispose_the_day_clock_no_longer_triggers_anything()
    {
        using var rig = new Rig();
        rig.Maintenance.Start();
        rig.Maintenance.PendingBackup.Wait();
        rig.Maintenance.DisposeAsync().AsTask().GetAwaiter().GetResult();

        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        rig.App.DayClock.Check();

        Assert.Equal([Day], rig.Backups);
    }

    [AvaloniaFact]
    public void Demo_services_are_never_maintained()
    {
        var app = AppServices.CreateDemo(AppSettings.Default with { CarryOver = true, DailyBackup = true });
        var calls = 0;
        var maintenance = new DataMaintenance(app, (_, _) =>
        {
            calls++;
            return System.Threading.Tasks.Task.CompletedTask;
        });
        var before = CarryOver.Select(app.Store, app.DayClock.Today).Count;

        maintenance.Start();
        maintenance.Run(app.DayClock.Today);

        Assert.False(maintenance.IsEnabled);
        Assert.Equal(0, calls);
        Assert.Equal(before, CarryOver.Select(app.Store, app.DayClock.Today).Count);
        maintenance.DisposeAsync().AsTask().GetAwaiter().GetResult();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [AvaloniaFact]
    public void Demo_services_have_no_database_to_back_up()
    {
        var app = AppServices.CreateDemo();
        var maintenance = new DataMaintenance(app);

        Assert.True(app.IsDemo);
        Assert.False(maintenance.IsEnabled);
        maintenance.DisposeAsync().AsTask().GetAwaiter().GetResult();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
