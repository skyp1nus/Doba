using System.Globalization;
using Doba.Storage.Internal;

namespace Doba.Storage;

/// <summary>Daily backup: one copy per day in Backups/doba-yyyy-MM-dd.db, keeping the newest 7.</summary>
public static class Backup
{
    private const string Prefix = "doba-";
    private const string Extension = ".db";
    private const string TempExtension = ".tmp";

    /// <summary>Creates today's backup if it does not exist yet and prunes old ones. Returns the backup path, or null if one already existed.</summary>
    /// <remarks>
    /// The copy is made with <c>VACUUM INTO</c> on the writer connection, after every write queued before the call is
    /// committed. It is written to a temporary file and renamed, so a crash never leaves a partial file under today's
    /// name. Pruning keeps the newest <paramref name="keep"/> files named exactly doba-yyyy-MM-dd.db (newest by the date
    /// in the name), deletes temporary files named exactly doba-yyyy-MM-dd.db.tmp left by a backup of another day that
    /// was interrupted, and never touches other files.
    /// </remarks>
    public static async Task<string?> RunDailyAsync(DobaDatabase db, DateOnly today, int keep = 7, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);

        string folder = db.Location.BackupFolder;
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, FileName(today));
        bool created = false;
        if (!File.Exists(path))
        {
            await db.ExecuteOnWriterAsync(connection =>
            {
                if (File.Exists(path))
                {
                    return; // a concurrent call made it first
                }

                string temp = path + TempExtension;
                File.Delete(temp); // a leftover from an interrupted backup; VACUUM INTO refuses existing files
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "VACUUM INTO $path;";
                    command.Parameters.AddWithValue("$path", temp);
                    command.ExecuteNonQuery();
                }

                using (var stream = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Flush(flushToDisk: true); // VACUUM INTO does not sync its output
                }

                File.Move(temp, path);
                created = true;
            }, cancellationToken).ConfigureAwait(false);
        }

        Prune(folder, keep, today);
        return created ? path : null;
    }

    /// <summary>"doba-yyyy-MM-dd.db".</summary>
    public static string FileName(DateOnly day) => Prefix + Formats.FormatDate(day) + Extension;

    /// <summary>The backups in <paramref name="folder"/> (exact doba-yyyy-MM-dd.db names only), newest first.</summary>
    public static IReadOnlyList<(DateOnly Day, string Path)> List(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var backups = new List<(DateOnly Day, string Path)>();
        foreach (string file in Directory.EnumerateFiles(folder, Prefix + "*" + Extension))
        {
            if (TryParseFileName(Path.GetFileName(file), out var day))
            {
                backups.Add((day, file));
            }
        }

        backups.Sort(static (a, b) => b.Day.CompareTo(a.Day));
        return backups;
    }

    private static void Prune(string folder, int keep, DateOnly today)
    {
        var backups = List(folder);
        for (int i = keep; i < backups.Count; i++)
        {
            File.Delete(backups[i].Path);
        }

        // A crash or power loss during VACUUM INTO leaves a full-size copy under the temporary name; today's is
        // replaced by the next backup of the day, any other day's would stay for good.
        foreach (string file in Directory.EnumerateFiles(folder, Prefix + "*" + Extension + TempExtension))
        {
            if (TryParseFileName(Path.GetFileName(file), Extension + TempExtension, out var day) && day != today)
            {
                File.Delete(file);
            }
        }
    }

    private static bool TryParseFileName(string name, out DateOnly day) => TryParseFileName(name, Extension, out day);

    private static bool TryParseFileName(string name, string extension, out DateOnly day)
    {
        day = default;
        // EnumerateFiles patterns are loose (e.g. 8.3 names on Windows), so match the exact shape here.
        return name.Length == Prefix.Length + Formats.DateFormat.Length + extension.Length
            && name.StartsWith(Prefix, StringComparison.Ordinal)
            && name.EndsWith(extension, StringComparison.Ordinal)
            && DateOnly.TryParseExact(name.AsSpan(Prefix.Length, Formats.DateFormat.Length), Formats.DateFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }
}
