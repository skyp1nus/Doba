using System.Globalization;
using System.Text;

namespace Doba.App.Services;

/// <summary>
/// <c>doba.log</c> next to the database: one line per storage problem (a write that failed, a flush that reported
/// failures), so a problem the app showed can still be read after it was closed. Kept small: past
/// <see cref="MaxBytes"/> the file starts again. Never throws (a log that cannot be written must not take the app down).
/// </summary>
public static class DataLog
{
    public const string FileName = "doba.log";

    /// <summary>Size at which the log is started afresh.</summary>
    public const long MaxBytes = 256 * 1024;

    private static readonly Lock s_gate = new();

    /// <summary>The log of the data folder <paramref name="folder"/>.</summary>
    public static string PathIn(string folder) => Path.Combine(folder, FileName);

    /// <summary>Appends "<paramref name="when"/> <paramref name="message"/>" as one line (any thread).</summary>
    public static void Append(string folder, DateTimeOffset when, string message)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(message);
        var line = when.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) + " " +
            message.ReplaceLineEndings(" ") + Environment.NewLine;
        lock (s_gate)
        {
            try
            {
                var path = PathIn(folder);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    info.Delete();
                }

                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The folder itself is failing (disk full, gone, read-only): the in-app notice still shows the problem.
            }
        }
    }
}
