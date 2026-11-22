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
        DataFolder = database?.Location.Folder;
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

    /// <summary>The data folder (null in demo mode).</summary>
    public string? DataFolder { get; }

    /// <summary>
    /// Raised (on the storage writer's thread, or the caller's) with a sentence for the owner each time a change could
    /// not be written to disk. The in-memory store keeps the change, so what the app shows and what a restart would
    /// load now differ; the shell turns this into a visible notice. Every failure is also appended to
    /// <see cref="DataLog"/> in the data folder.
    /// </summary>
    public event EventHandler<string>? SaveFailed;

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
    /// From command-line flags: --demo (and --selfcheck), or --data &lt;folder&gt;, else %LOCALAPPDATA%\Doba. The
    /// platform set follows <see cref="PlatformPlan.For"/> (demo: no autostart, hotkey or tray; --data: no registry).
    /// <paramref name="preload"/> is the database already being opened and loaded off the UI thread (see
    /// <see cref="StartPreload"/>); null opens it here. <paramref name="platforms"/> builds the platform set for the
    /// plan (default <see cref="PlatformServices.Create(PlatformPlan)"/>; tests pass a recorder that returns no-ops).
    /// Nothing platform-side is built when the data cannot be loaded.
    /// </summary>
    internal static AppServices FromStartup(StartupFlags flags, Task<LoadedStore>? preload = null,
        Func<PlatformPlan, PlatformServices>? platforms = null)
    {
        ArgumentNullException.ThrowIfNull(flags);
        platforms ??= PlatformServices.Create;
        var demo = IsDemoLaunch(flags);
        var plan = PlatformPlan.For(demo, flags.DataFolder is not null);
        if (demo)
        {
            return CreateDemo(platform: platforms(plan));
        }

        // GetResult rethrows the loader's own exception, exactly as a synchronous Open would have thrown it.
        var loaded = preload?.GetAwaiter().GetResult() ?? LoadedStore.Load(DataLocationOf(flags));
        return FromLoaded(loaded, null, platforms(plan));
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

    /// <summary>Waits until every queued task and settings write is on disk (no-op in demo mode). Never throws: each
    /// failed write was already reported through <see cref="SaveFailed"/>; the flush's summary goes to the log.</summary>
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
        catch (StorageWriteException ex)
        {
            Log("flush: " + ex.Message);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException)
        {
            ReportSaveFailure("flush", ex);
        }
    }

    /// <summary>
    /// Stops the day clock and releases the global hotkey, the tray icon and the Claude bridge (once). These are UI-thread
    /// objects, so a caller that awaits something before <see cref="DisposeAsync"/> (and may resume on a pool thread)
    /// calls this first, on the UI thread.
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
        Platform.Bridge.Dispose();
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
                Log("close: " + ex.Message);
            }
        }
    }

    /// <summary>The owner-facing sentence for a failed write in <paramref name="folder"/>.</summary>
    public static string DescribeSaveFailure(string folder, Exception error)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(error);
        return "Some changes could not be saved to " + folder + ": " + error.Message +
            ". They are still shown here but will be missing after a restart. Details are in " + DataLog.FileName + " in that folder.";
    }

    private void OnWriteFailed(object? sender, StorageWriteFailedEventArgs e) => ReportSaveFailure("write", e.Error);

    private void ReportSaveFailure(string what, Exception error)
    {
        Log(what + " failed: " + error.GetType().Name + ": " + error.Message);
        if (Database is { } db)
        {
            SaveFailed?.Invoke(this, DescribeSaveFailure(db.Location.Folder, error));
        }
    }

    /// <summary>Writes a storage problem to Trace and, with a database, to <see cref="DataLog"/> in the data folder (any
    /// thread; never throws).</summary>
    internal void LogProblem(string message) => Log(message);

    private void Log(string message)
    {
        Trace.WriteLine("Doba: " + message);
        if (Database is { } db)
        {
            DataLog.Append(db.Location.Folder, Clock.GetLocalNow(), message);
        }
    }
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
