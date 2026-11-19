using System.Globalization;
using Doba.Core.Model;
using Microsoft.Data.Sqlite;

namespace Doba.Storage.Tests;

/// <summary>A fresh, not yet existing folder under the temp directory; deleted on dispose.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), "doba-storage-tests", Guid.NewGuid().ToString("N"));
        Location = new DataLocation(Path.Combine(Root, "Doba"));
    }

    public string Root { get; }

    /// <summary>The data folder (does not exist until the database is opened).</summary>
    public DataLocation Location { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

public static class Raw
{
    /// <summary>A separate, non-pooled connection that sees what is committed on disk.</summary>
    public static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    public static object? Scalar(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public static long Number(string path, string sql) => Convert.ToInt64(Scalar(path, sql), CultureInfo.InvariantCulture);

    public static void Execute(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static List<long> TaskIds(string path)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM tasks ORDER BY id;";
        using var reader = command.ExecuteReader();
        var ids = new List<long>();
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }
}

public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public static class Samples
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TaskItem Item(long id, string title = "Task") =>
        new(id, title, string.Empty, null, null, null, 0, Priority.None, null, false);

    /// <summary>
    /// Every combination of the shapes a task can have: undated / single-day / timed / multi-day, with and without
    /// notes, tag and duration, every priority, done and not done, plus awkward strings.
    /// </summary>
    public static List<TaskItem> AllCombinations()
    {
        var list = new List<TaskItem>();
        long id = 1;
        var day = new DateOnly(2026, 11, 15);
        (DateOnly? Date, DateOnly? End, TimeOnly? Time)[] shapes =
        [
            (null, null, null),                                     // undated (Inbox)
            (day, null, null),                                      // single day, anytime
            (day, null, new TimeOnly(9, 30)),                       // timed
            (day, null, new TimeOnly(0, 0)),                        // timed at midnight
            (day, null, new TimeOnly(23, 59)),                      // timed late
            (day, day.AddDays(1), null),                            // two-day span
            (new DateOnly(2026, 12, 30), new DateOnly(2027, 1, 3), null), // span across a year
            (new DateOnly(2028, 2, 29), null, null),                // leap day
        ];
        string?[] tags = [null, "work", "deep-work"];
        string[] notes = ["", "Line one\nLine two — ünïcödé ✓ \"quoted\" 'single' ; DROP TABLE tasks; --"];
        foreach (var (date, end, time) in shapes)
        {
            foreach (var priority in Enum.GetValues<Priority>())
            {
                foreach (string? tag in tags)
                {
                    foreach (string note in notes)
                    {
                        foreach (bool done in new[] { false, true })
                        {
                            int duration = time is null ? 0 : (int)(id % 3) * 45; // 0, 45, 90
                            list.Add(new TaskItem(id, $"Task {id} é中\U0001F600", note, date, end, time, duration, priority, tag, done));
                            id++;
                        }
                    }
                }
            }
        }

        return list;
    }

    /// <summary>A value different from the default in every property.</summary>
    public static AppSettings NonDefaultSettings() => new()
    {
        Accent = Accent.Gray,
        WindowTransparency = 35,
        Theme = ThemeMode.System,
        Density = Density.Compact,
        WeekStart = DayOfWeek.Sunday,
        LaunchView = LaunchView.Inbox,
        DefaultPriority = Priority.High,
        CarryOver = false,
        CompletionSound = true,
        LaunchAtStartup = false,
        KeepInTray = false,
        DailyBackup = false,
    };
}
