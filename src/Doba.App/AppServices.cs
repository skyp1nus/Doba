using System.Diagnostics;
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
    private bool _disposed;
    private bool _platformReleased;

    private AppServices(TimeProvider clock, ITaskStore store, ISettingsService settings, PlatformServices platform, DobaDatabase? database)
    {
        Clock = clock;
        Store = store;
        Settings = settings;
        Platform = platform;
        Database = database;
        DayClock = new DayClock(clock);
        if (database is not null)
        {
            database.WriteFailed += OnWriteFailed;
        }
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
    public static AppServices Open(DataLocation location, TimeProvider? clock = null, PlatformServices? platform = null) =>
        FromLoaded(LoadedStore.Load(location), clock, platform);

    /// <summary>
    /// From command-line flags: --demo (and --selfcheck), or --data &lt;folder&gt;, else %LOCALAPPDATA%\Doba. Demo mode
    /// gets no-op autostart and hotkey; --data never touches the registry (see <see cref="PlatformServices.ForLaunch"/>).
    /// <paramref name="preload"/> is the database already being opened and loaded off the UI thread (see
    /// <see cref="StartPreload"/>); null opens it here.
    /// </summary>
    internal static AppServices FromStartup(StartupFlags flags, Task<LoadedStore>? preload = null)
    {
        ArgumentNullException.ThrowIfNull(flags);
        var demo = IsDemoLaunch(flags);
        var platform = PlatformServices.ForLaunch(demo, flags.DataFolder is not null);
        if (demo)
        {
            return CreateDemo(platform: platform);
        }

        // GetResult rethrows the loader's own exception, exactly as a synchronous Open would have thrown it.
        var loaded = preload?.GetAwaiter().GetResult() ?? LoadedStore.Load(DataLocationOf(flags));
        return FromLoaded(loaded, null, platform);
    }

    /// <summary>
    /// Starts opening and loading the database of this launch on a thread-pool thread, so the disk read and the store's
    /// indexes are built while Avalonia initialises the platform (called from <c>Program.Main</c> before Avalonia starts).
    /// Null for --demo / --selfcheck, which read nothing from disk. Only the timing changes: the same database is opened
    /// with the same code, and a failure surfaces from <see cref="FromStartup"/> as before.
    /// </summary>
    internal static Task<LoadedStore>? StartPreload(StartupFlags flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        if (IsDemoLaunch(flags))
        {
            return null;
        }

        var location = DataLocationOf(flags);
        return Task.Run(() => LoadedStore.Load(location));
    }

    private static bool IsDemoLaunch(StartupFlags flags) => flags.Demo || flags.SelfCheck;

    private static DataLocation DataLocationOf(StartupFlags flags) =>
        flags.DataFolder is { } f ? new DataLocation(f) : DataLocation.Default();

    private static AppServices FromLoaded(LoadedStore loaded, TimeProvider? clock, PlatformServices? platform) =>
        new(clock ?? TimeProvider.System,
            loaded.Store,
            new SettingsService(loaded.Data.Settings, loaded.Database.Settings),
            platform ?? PlatformServices.Create(),
            loaded.Database);

    /// <summary>Waits until every queued task and settings write is on disk (no-op in demo mode). Never throws: a
    /// failed write is reported through <see cref="Trace"/> (the design has no UI for it).</summary>
    public async Task FlushAsync()
    {
        if (Database is not { } db || _disposed)
        {
            return;
        }

        try
        {
            await db.Tasks.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is StorageWriteException or ObjectDisposedException or IOException)
        {
            Trace.WriteLine("Doba: flushing pending writes failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Stops the day clock and releases the global hotkey and the tray icon (once). These are UI-thread objects, so a
    /// caller that awaits something before <see cref="DisposeAsync"/> (and may resume on a pool thread) calls this
    /// first, on the UI thread.
    /// </summary>
    internal void ReleasePlatform()
    {
        if (_platformReleased)
        {
            return;
        }

        _platformReleased = true;
        DayClock.Dispose();
        Platform.Hotkey.Dispose();
        Platform.Tray.Dispose();
    }

    /// <summary>Flushes pending writes and closes the database (once; later calls do nothing).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleasePlatform();
        if (Database is not null)
        {
            Database.WriteFailed -= OnWriteFailed;
            try
            {
                await Database.DisposeAsync().ConfigureAwait(false);
            }
            catch (StorageWriteException ex)
            {
                Trace.WriteLine("Doba: writes failed before closing the database: " + ex.Message);
            }
        }
    }

    private static void OnWriteFailed(object? sender, StorageWriteFailedEventArgs e) =>
        Trace.WriteLine("Doba: a background write failed: " + e.Error.Message);
}

/// <summary>An opened database, what it held and the in-memory store built from it (no UI objects: may be built on
/// any thread).</summary>
internal sealed record LoadedStore(DobaDatabase Database, LoadedData Data, TaskStore Store)
{
    /// <summary>Opens (or creates) the database in <paramref name="location"/>, reads everything and indexes it.</summary>
    public static LoadedStore Load(DataLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var db = DobaDatabase.Open(location);
        try
        {
            var data = db.Load();
            return new LoadedStore(db, data, new TaskStore(data.Tasks, data.Tags, db.Tasks));
        }
        catch
        {
            db.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }
}
