using Doba.Core.Model;

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
