using Doba.Core.Model;
using Doba.Core.Store;

namespace Doba.Storage;

/// <summary>Where Doba keeps its files. Default: %LOCALAPPDATA%\Doba (Environment.SpecialFolder.LocalApplicationData/Doba).</summary>
public sealed record DataLocation(string Folder)
{
    public string DatabasePath => Path.Combine(Folder, "doba.db");

    public string BackupFolder => Path.Combine(Folder, "Backups");

    public static DataLocation Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Doba"));
}

/// <summary>Everything read from disk at startup.</summary>
public sealed record LoadedData(IReadOnlyList<TaskItem> Tasks, IReadOnlyList<string> Tags, AppSettings Settings);

/// <summary>
/// The SQLite database (WAL, schema version in PRAGMA user_version, hand-written SQL).
/// Open → <see cref="Load"/> once → hand <see cref="Tasks"/> / <see cref="Settings"/> to the store → <see cref="DisposeAsync"/> flushes on exit.
/// Writes are queued and executed on a background writer; <see cref="ITaskPersistence.FlushAsync"/> waits for them.
/// </summary>
public sealed class DobaDatabase : IAsyncDisposable
{
    private DobaDatabase() => throw new NotImplementedException();

    /// <summary>Creates the folder and database if missing and migrates the schema.</summary>
    public static DobaDatabase Open(DataLocation location) => throw new NotImplementedException();

    public DataLocation Location => throw new NotImplementedException();

    public LoadedData Load() => throw new NotImplementedException();

    public ITaskPersistence Tasks => throw new NotImplementedException();

    public ISettingsPersistence Settings => throw new NotImplementedException();

    public ValueTask DisposeAsync() => throw new NotImplementedException();
}

/// <summary>Daily backup: one copy per day in Backups/doba-yyyy-MM-dd.db, keeping the newest 7.</summary>
public static class Backup
{
    /// <summary>Creates today's backup if it does not exist yet and prunes old ones. Returns the backup path, or null if one already existed.</summary>
    public static Task<string?> RunDailyAsync(DobaDatabase db, DateOnly today, int keep = 7, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

/// <summary>Export / import of all tasks, tags and settings as one JSON file (System.Text.Json source generation).</summary>
public static class JsonTransfer
{
    public static Task ExportAsync(Stream destination, IReadOnlyList<TaskItem> tasks, IReadOnlyList<string> tags, AppSettings settings, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    /// <summary>Throws <see cref="FormatException"/> for an invalid or unsupported file.</summary>
    public static Task<LoadedData> ImportAsync(Stream source, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
