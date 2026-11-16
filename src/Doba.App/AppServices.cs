using Doba.App.Platform;
using Doba.App.Services;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Store;
using Doba.Storage;

namespace Doba.App;

/// <summary>
/// The hand-written composition root (no DI container): one instance per process, built before the main window.
/// Demo mode uses DemoData + a fixed clock and touches no disk; normal mode loads the SQLite database once.
/// </summary>
public sealed class AppServices : IAsyncDisposable
{
    private AppServices(TimeProvider clock, ITaskStore store, ISettingsService settings, PlatformServices platform, DobaDatabase? database)
    {
        Clock = clock;
        Store = store;
        Settings = settings;
        Platform = platform;
        Database = database;
        DayClock = new DayClock(clock);
    }

    public TimeProvider Clock { get; }

    public ITaskStore Store { get; }

    public ISettingsService Settings { get; }

    public PlatformServices Platform { get; }

    public DayClock DayClock { get; }

    /// <summary>Null in demo mode.</summary>
    public DobaDatabase? Database { get; }

    public bool IsDemo => Database is null;

    /// <summary>DemoData with the fixed 2026-11-15 22:26 clock; nothing persisted. Used by --demo and the screenshot tests.</summary>
    public static AppServices CreateDemo(AppSettings? settings = null, PlatformServices? platform = null) =>
        new(DemoData.CreateClock(),
            new TaskStore(DemoData.Tasks(), DemoData.Tags, NullTaskPersistence.Instance),
            new SettingsService(settings ?? AppSettings.Default, null),
            platform ?? PlatformServices.NoOp(),
            null);

    /// <summary>Opens (or creates) the database in <paramref name="location"/> and loads everything into memory.</summary>
    public static AppServices Open(DataLocation location, TimeProvider? clock = null, PlatformServices? platform = null)
    {
        ArgumentNullException.ThrowIfNull(location);
        var db = DobaDatabase.Open(location);
        var data = db.Load();
        return new AppServices(
            clock ?? TimeProvider.System,
            new TaskStore(data.Tasks, data.Tags, db.Tasks),
            new SettingsService(data.Settings, db.Settings),
            platform ?? PlatformServices.Create(),
            db);
    }

    /// <summary>From command-line flags: --demo, or --data &lt;folder&gt;, else %LOCALAPPDATA%\Doba.</summary>
    public static AppServices FromStartup() =>
        AppStartup.Demo
            ? CreateDemo(platform: PlatformServices.Create())
            : Open(AppStartup.DataFolder is { } f ? new DataLocation(f) : DataLocation.Default());

    /// <summary>Flushes pending writes and closes the database.</summary>
    public async ValueTask DisposeAsync()
    {
        DayClock.Dispose();
        Platform.Hotkey.Dispose();
        if (Database is not null)
        {
            await Database.DisposeAsync().ConfigureAwait(false);
        }
    }
}
