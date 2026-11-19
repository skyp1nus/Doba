using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.App.Theme;
using Doba.Core.Model;
using Doba.Core.Time;
using Doba.Storage;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Lifecycle;

/// <summary>
/// The real main window and <see cref="AppLifecycle"/> on fake platform services, the way App.axaml.cs wires them:
/// Start before Show, the window shown unless the lifecycle starts hidden in the tray. The shutdown callback only
/// counts (the test plays the desktop lifetime's Exit by calling <see cref="Exit"/>).
/// </summary>
internal sealed class LifecycleHarness : IDisposable
{
    private readonly bool _animationsWere;
    private readonly FakePlatform? _platform;
    private bool _exited;

    private LifecycleHarness(AppServices app, FakePlatform? platform, LifecycleOptions options)
    {
        _animationsWere = AppMotion.SystemAnimationsEnabled;
        App = app;
        _platform = platform;
        Window = ShellComposition.CreateMainWindow(app, Application.Current!);
        Lifecycle = new AppLifecycle(app, Window, options, () => ShutdownCalls++);
        Lifecycle.Start();
        if (Lifecycle.ShowsWindowAtStart)
        {
            Window.Show();
        }

        Settle();
    }

    public AppServices App { get; }

    /// <summary>The fakes the app runs on (not available on a harness made with <see cref="OnServices"/>).</summary>
    public FakePlatform Platform =>
        _platform ?? throw new InvalidOperationException("This harness runs on given platform services, not on fakes.");

    public MainWindow Window { get; }

    public AppLifecycle Lifecycle { get; }

    public int ShutdownCalls { get; private set; }

    public MainViewModel Vm => Window.ViewModel!;

    /// <summary>A demo app (DemoData, fixed clock) on fakes.</summary>
    public static LifecycleHarness Demo(FakePlatform? platform = null, LifecycleOptions? options = null, AppSettings? settings = null)
    {
        platform ??= new FakePlatform();
        return new LifecycleHarness(AppServices.CreateDemo(settings, platform.Services), platform, options ?? new LifecycleOptions());
    }

    /// <summary>A real database in <paramref name="folder"/> with a fixed clock, on fakes; <paramref name="seed"/> runs
    /// before startup (what the database held when the app was launched).</summary>
    public static LifecycleHarness Disk(TempFolder folder, FixedTimeProvider clock, FakePlatform? platform = null,
        LifecycleOptions? options = null, Func<AppSettings, AppSettings>? settings = null, Action<AppServices>? seed = null)
    {
        platform ??= new FakePlatform();
        var app = AppServices.Open(folder.Location, clock, platform.Services);
        if (settings is not null)
        {
            app.Settings.Update(settings);
        }

        seed?.Invoke(app);

        return new LifecycleHarness(app, platform, options ?? new LifecycleOptions());
    }

    /// <summary>A real database in <paramref name="folder"/> with a fixed clock, on the given platform services (the
    /// Claude bridge tests: <see cref="Doba.App.Platform.PlatformServices.NoOp"/> with a real
    /// <see cref="Doba.App.Platform.ClaudeBridge"/>).</summary>
    public static LifecycleHarness OnServices(TempFolder folder, FixedTimeProvider clock, Doba.App.Platform.PlatformServices services,
        Func<AppSettings, AppSettings>? settings = null)
    {
        var app = AppServices.Open(folder.Location, clock, services);
        if (settings is not null)
        {
            app.Settings.Update(settings);
        }

        return new LifecycleHarness(app, null, new LifecycleOptions());
    }

    /// <summary>Runs dispatcher jobs and render ticks (the first frame, then the work posted after it).</summary>
    public void Settle()
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (Window.IsVisible)
            {
                Window.UpdateLayout();
            }

            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>What the desktop lifetime's Exit does: dispose the lifecycle (backup wait, flush, database close).</summary>
    public void Exit()
    {
        if (_exited)
        {
            return;
        }

        _exited = true;
        Lifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Exit();
        Window.Close();
        Dispatcher.UIThread.RunJobs();
        new ThemeService(Application.Current!).Apply(AppSettings.Default);
        AppMotion.SystemAnimationsEnabled = _animationsWere;
    }
}

/// <summary>Keeps every overlay exit animation running until disposed (a save waits in its exit, OnClosed not yet run).</summary>
internal sealed class HeldOverlayExit : IOverlayTransitions, IDisposable
{
    private readonly IOverlayTransitions _previous = OverlayHost.OverlayTransitions;

    public HeldOverlayExit() => OverlayHost.OverlayTransitions = this;

    /// <summary>An exit was started and its done callback has not been called.</summary>
    public bool IsRunning { get; private set; }

    public void Open(Avalonia.Controls.Control shade, Avalonia.Controls.Control panel)
    {
    }

    public void Close(Avalonia.Controls.Control shade, Avalonia.Controls.Control panel, Action done) => IsRunning = true;

    public void Dispose() => OverlayHost.OverlayTransitions = _previous;
}

/// <summary>
/// Holds the database's write lock from a second connection (<c>BEGIN IMMEDIATE</c>), so the app's background writer
/// cannot commit anything until <see cref="Release"/>: its own transaction waits on the busy lock (Microsoft.Data.Sqlite
/// retries for the command timeout, 30 s). Readers still see the last committed state (WAL). With it a test can tell
/// "the app waited for its writes" from "the writer happened to be quick".
/// </summary>
internal sealed class WriterStall : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly Lock _gate = new();
    private bool _released;

    private WriterStall(Microsoft.Data.Sqlite.SqliteConnection connection) => _connection = connection;

    public static WriterStall Hold(DataLocation location)
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = location.DatabasePath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var begin = connection.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE;";
        begin.ExecuteNonQuery();
        return new WriterStall(connection);
    }

    /// <summary>Lets the writer go on (any thread; once).</summary>
    public void Release()
    {
        lock (_gate)
        {
            if (_released)
            {
                return;
            }

            _released = true;
            using (var rollback = _connection.CreateCommand())
            {
                rollback.CommandText = "ROLLBACK;";
                rollback.ExecuteNonQuery();
            }

            _connection.Dispose();
        }
    }

    public void Dispose() => Release();
}

/// <summary>A data folder under the test's temp directory, deleted afterwards.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "doba-tests", "lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public DataLocation Location => new(Path);

    /// <summary>What a restart would load: opens the database again, reads everything and closes it.</summary>
    public LoadedData Reopen()
    {
        var db = DobaDatabase.Open(Location);
        try
        {
            return db.Load();
        }
        finally
        {
            db.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>Titles of the tasks committed on disk right now (a separate read-only connection; the app may still
    /// have the database open).</summary>
    public IReadOnlyList<string> TitlesOnDisk()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Location.DatabasePath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT title FROM tasks ORDER BY id;";
        using var reader = command.ExecuteReader();
        var titles = new List<string>();
        while (reader.Read())
        {
            titles.Add(reader.GetString(0));
        }

        return titles;
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open by a pool on a slow machine: the temp folder is cleaned by the OS later.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
