using Doba.Core.Model;
using Doba.Core.Store;
using Doba.Storage.Internal;
using Microsoft.Data.Sqlite;

namespace Doba.Storage;

/// <summary>
/// The SQLite database (WAL, schema version in PRAGMA user_version, hand-written SQL).
/// Open → <see cref="Load"/> once → hand <see cref="Tasks"/> / <see cref="Settings"/> to the store → <see cref="DisposeAsync"/> flushes on exit.
/// Writes are queued and executed on a background writer; <see cref="ITaskPersistence.FlushAsync"/> waits for them.
/// </summary>
/// <remarks>
/// <para>Every <see cref="Tasks"/> / <see cref="Settings"/> write call only enqueues (no disk I/O, safe on the UI
/// thread). One background writer drains the queue and commits each burst in a single transaction.</para>
/// <para>A failed write is never dropped silently: it is kept in <see cref="LastWriteError"/>, raised through
/// <see cref="WriteFailed"/>, and the next <see cref="ITaskPersistence.FlushAsync"/> (or <see cref="DisposeAsync"/>)
/// throws a <see cref="StorageWriteException"/>. The queue keeps working afterwards.</para>
/// </remarks>
public sealed class DobaDatabase : IAsyncDisposable
{
    private readonly WriteBehindWriter _writer;
    private readonly TaskPersistence _tasks;
    private readonly SettingsPersistence _settings;

    private DobaDatabase(DataLocation location, SqliteConnection writeConnection)
    {
        Location = location;
        _writer = new WriteBehindWriter(writeConnection, OnWriteFailed);
        _tasks = new TaskPersistence(_writer);
        _settings = new SettingsPersistence(this, _writer);
    }

    /// <summary>Creates the folder and database if missing and migrates the schema.</summary>
    /// <exception cref="NotSupportedException">The database was written by a newer Doba with a higher schema version.</exception>
    public static DobaDatabase Open(DataLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        Directory.CreateDirectory(location.Folder);
        var connection = SqliteSetup.Open(location.DatabasePath, create: true);
        try
        {
            Schema.Migrate(connection);
            return new DobaDatabase(location, connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public DataLocation Location { get; }

    /// <summary>Schema version this build writes (PRAGMA user_version).</summary>
    public static int SchemaVersion => Schema.LatestVersion;

    /// <summary>The most recent failure of the background writer, or null. Not cleared by a flush.</summary>
    public Exception? LastWriteError => _writer.LastError;

    /// <summary>Raised on the background writer thread whenever a queued write fails. Handler exceptions are ignored.</summary>
    public event EventHandler<StorageWriteFailedEventArgs>? WriteFailed;

    /// <summary>
    /// Reads every task (by id), tag (by position) and the settings. Meant to be called once at startup, before writes
    /// are queued; it reads what is committed on disk.
    /// </summary>
    /// <exception cref="InvalidDataException">A stored date or time is malformed.</exception>
    public LoadedData Load()
    {
        ObjectDisposedException.ThrowIf(_writer.IsDisposed, this);
        using var connection = SqliteSetup.Open(Location.DatabasePath, create: false);
        using var transaction = connection.BeginTransaction(deferred: true); // one consistent snapshot
        var tasks = DatabaseReader.ReadTasks(connection);
        var tags = DatabaseReader.ReadTags(connection);
        var settings = DatabaseReader.ReadSettings(connection);
        transaction.Commit();
        _settings.Remember(settings);
        return new LoadedData(tasks, tags, settings);
    }

    public ITaskPersistence Tasks => _tasks;

    public ISettingsPersistence Settings => _settings;

    /// <summary>Waits for every queued write, then closes the database. Throws <see cref="StorageWriteException"/> (after closing) if queued writes failed and no flush reported them yet.</summary>
    public ValueTask DisposeAsync() => _writer.DisposeAsync();

    /// <summary>Runs <paramref name="action"/> on the writer connection once everything queued before it is committed.</summary>
    internal Task ExecuteOnWriterAsync(Action<SqliteConnection> action, CancellationToken cancellationToken) =>
        _writer.ExecuteAsync(action, cancellationToken);

    internal AppSettings ReadSettingsFromDisk()
    {
        using var connection = SqliteSetup.Open(Location.DatabasePath, create: false);
        return DatabaseReader.ReadSettings(connection);
    }

    private void OnWriteFailed(Exception error)
    {
        try
        {
            WriteFailed?.Invoke(this, new StorageWriteFailedEventArgs(error));
        }
        catch (Exception)
        {
            // A faulty handler must not stop the writer; the error itself is still recorded and surfaced by FlushAsync.
        }
    }

    private sealed class TaskPersistence(WriteBehindWriter writer) : ITaskPersistence
    {
        public void Upsert(TaskItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            writer.Enqueue(new UpsertOp(item));
        }

        public void Delete(long id) => writer.Enqueue(new DeleteOp(id));

        // Lists are copied: the caller may keep mutating its own list after the call returns.
        public void SaveTags(IReadOnlyList<string> tags)
        {
            ArgumentNullException.ThrowIfNull(tags);
            writer.Enqueue(new TagsOp([.. tags]));
        }

        public void ReplaceAll(IReadOnlyList<TaskItem> tasks, IReadOnlyList<string> tags)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            ArgumentNullException.ThrowIfNull(tags);
            writer.Enqueue(new ReplaceAllOp([.. tasks], [.. tags]));
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => writer.FlushAsync(cancellationToken);
    }

    private sealed class SettingsPersistence(DobaDatabase database, WriteBehindWriter writer) : ISettingsPersistence
    {
        private AppSettings? _current;

        /// <summary>The last saved (or loaded) settings; reads the database only if neither happened yet.</summary>
        public AppSettings Load()
        {
            if (Volatile.Read(ref _current) is { } current)
            {
                return current;
            }

            Remember(database.ReadSettingsFromDisk());
            return Volatile.Read(ref _current)!;
        }

        public void Save(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            writer.Enqueue(new SettingsOp(settings));
            Volatile.Write(ref _current, settings);
        }

        /// <summary>Caches what was read from disk unless a newer value was saved meanwhile.</summary>
        public void Remember(AppSettings settings) => Interlocked.CompareExchange(ref _current, settings, null);
    }
}
