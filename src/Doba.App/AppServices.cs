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

    /// <summary>
    /// From command-line flags: --demo (and --selfcheck), or --data &lt;folder&gt;, else %LOCALAPPDATA%\Doba. Demo mode
    /// gets no-op autostart and hotkey; --data never touches the registry (see <see cref="PlatformServices.ForLaunch"/>).
    /// </summary>
    internal static AppServices FromStartup(StartupFlags flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        var demo = flags.Demo || flags.SelfCheck;
        var platform = PlatformServices.ForLaunch(demo, flags.DataFolder is not null);
        return demo
            ? CreateDemo(platform: platform)
            : Open(flags.DataFolder is { } f ? new DataLocation(f) : DataLocation.Default(), platform: platform);
    }

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

    /// <summary>Flushes pending writes and closes the database (once; later calls do nothing).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DayClock.Dispose();
        Platform.Hotkey.Dispose();
        Platform.Tray.Dispose();
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
