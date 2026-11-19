using Doba.Bridge.Wire;
using Doba.Core.Model;
using Doba.Core.Store;
using Doba.Core.Time;

namespace Doba.Bridge.Tests;

/// <summary>Counts persistence calls (thread-safe; the store calls it under <see cref="TestDoba.Gate"/>).</summary>
internal sealed class CountingPersistence(ITaskPersistence? inner = null) : ITaskPersistence
{
    private readonly ITaskPersistence _inner = inner ?? NullTaskPersistence.Instance;
    private int _upserts;
    private int _deletes;

    public int Upserts => Volatile.Read(ref _upserts);

    public int Deletes => Volatile.Read(ref _deletes);

    public void Upsert(TaskItem item)
    {
        Interlocked.Increment(ref _upserts);
        _inner.Upsert(item);
    }

    public void Delete(long id)
    {
        Interlocked.Increment(ref _deletes);
        _inner.Delete(id);
    }

    public void SaveTags(IReadOnlyList<string> tags) => _inner.SaveTags(tags);

    public void ReplaceAll(IReadOnlyList<TaskItem> tasks, IReadOnlyList<string> tags) => _inner.ReplaceAll(tasks, tags);

    public Task FlushAsync(CancellationToken cancellationToken = default) => _inner.FlushAsync(cancellationToken);
}

/// <summary>A fresh folder under %TEMP%\doba-bridge-tests; deleted on dispose. Its path gives the pipe and mutex names,
/// so every test talks to its own Doba.</summary>
internal sealed class TempDataFolder : IDisposable
{
    public TempDataFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "doba-bridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(50); // a just-closed SQLite file or child process can hold the folder for a moment
            }
        }
    }
}

/// <summary>
/// A stand-in for a running Doba on a temp data folder: the single-instance mutex (optional), a real
/// <see cref="TaskStore"/> and a real <see cref="BridgeHost"/> whose handler runs <see cref="ToolExecutor"/> under one
/// lock, as Doba runs it on its UI thread. Never touches the real data folder.
/// </summary>
internal sealed class TestDoba : IDisposable
{
    private readonly TempDataFolder? _ownFolder;
    private Mutex? _mutex;
    private int _changed;

    public TestDoba(bool claimMutex = true, bool startHost = true, IEnumerable<TaskItem>? tasks = null,
        IEnumerable<string>? tags = null, ITaskPersistence? persistence = null, string? folder = null)
    {
        if (folder is null)
        {
            _ownFolder = new TempDataFolder();
            folder = _ownFolder.Path;
        }

        Folder = folder;
        Persistence = new CountingPersistence(persistence);
        Store = new TaskStore(tasks ?? [], tags ?? ["work", "home"], Persistence);
        Store.Changed += (_, _) => Interlocked.Increment(ref _changed);
        Host = new BridgeHost(BridgeNames.PipeName(folder), HandleAsync);
        if (claimMutex)
        {
            ClaimMutex();
        }

        if (startHost)
        {
            Host.Start();
        }
    }

    /// <summary>2026-11-19 (a Thursday) 10:26 at UTC+1.</summary>
    public static DateTimeOffset Now { get; } = new(2026, 11, 19, 10, 26, 0, TimeSpan.FromHours(1));

    public static DateOnly Today { get; } = new(2026, 11, 19);

    public string Folder { get; }

    public CountingPersistence Persistence { get; }

    public TaskStore Store { get; }

    public BridgeHost Host { get; }

    /// <summary>The "UI thread": every request and every test read of the store runs under it.</summary>
    public Lock Gate { get; } = new();

    public FixedTimeProvider Clock { get; } = new(Now);

    public AppSettings Settings { get; set; } =
        AppSettings.Default with { ClaudeAccess = true, ClaudeAccessLevel = ClaudeAccessLevel.ReadWrite };

    public long? OpenEditId { get; set; }

    /// <summary>Awaited before each request runs (to hold requests "in the UI queue").</summary>
    public Func<Task>? BeforeExecute { get; set; }

    public int Requests => Volatile.Read(ref _requests);

    public int ChangedEvents => Volatile.Read(ref _changed);

    private int _requests;

    public void ClaimMutex() => _mutex ??= new Mutex(false, BridgeNames.MutexName(Folder));

    public void ReleaseMutex()
    {
        _mutex?.Dispose();
        _mutex = null;
    }

    public T Read<T>(Func<TaskStore, T> read)
    {
        lock (Gate)
        {
            return read(Store);
        }
    }

    public void Dispose()
    {
        Host.Dispose();
        ReleaseMutex();
        _ownFolder?.Dispose();
    }

    private async Task<BridgeResponse> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        if (BeforeExecute is { } before)
        {
            await before().ConfigureAwait(false);
        }

        lock (Gate)
        {
            return ToolExecutor.Execute(request, new ToolContext(Store, Settings, Clock, OpenEditId));
        }
    }
}
