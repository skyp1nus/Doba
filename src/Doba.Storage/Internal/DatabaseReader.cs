using System.Text;
using Doba.Core.Model;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Doba.Storage.Internal;

/// <summary>Reads the whole database once (startup). Hand-written column mapping by ordinal.</summary>
internal static class DatabaseReader
{
    private const string SelectTasks =
        "SELECT id, title, notes, date, end_date, time, duration, priority, tag, done FROM tasks ORDER BY id;";

    /// <summary>
    /// All tasks in id order. This is the startup hot path, so it steps the statement through SQLitePCL's raw API
    /// (Microsoft.Data.Sqlite's per-value overhead roughly doubles the time at 10,000 rows), parses dates and times
    /// straight from the UTF-8 bytes and shares one string instance per distinct tag.
    /// </summary>
    public static List<TaskItem> ReadTasks(SqliteConnection connection)
    {
        int count = checked((int)SqliteSetup.ScalarInt64(connection, "SELECT count(*) FROM tasks;"));
        var tasks = new List<TaskItem>(count);
        var db = connection.Handle ?? throw new InvalidOperationException("The connection is not open.");
        int rc = raw.sqlite3_prepare_v2(db, SelectTasks, out sqlite3_stmt statement);
        SqliteException.ThrowExceptionForRC(rc, db);
        using (statement)
        {
            var tagCache = new Dictionary<string, string>(StringComparer.Ordinal);
            var tagLookup = tagCache.GetAlternateLookup<ReadOnlySpan<char>>();
            while ((rc = raw.sqlite3_step(statement)) == raw.SQLITE_ROW)
            {
                long id = raw.sqlite3_column_int64(statement, 0);
                tasks.Add(new TaskItem(
                    Id: id,
                    Title: Text(statement, 1) ?? string.Empty,
                    Notes: Text(statement, 2) ?? string.Empty,
                    Date: Date(statement, 3, id, "date"),
                    End: Date(statement, 4, id, "end_date"),
                    Time: Time(statement, 5, id),
                    DurationMinutes: raw.sqlite3_column_int(statement, 6),
                    Priority: (Priority)raw.sqlite3_column_int(statement, 7),
                    Tag: Tag(statement, 8, tagLookup),
                    Done: raw.sqlite3_column_int64(statement, 9) != 0));
            }

            if (rc != raw.SQLITE_DONE)
            {
                SqliteException.ThrowExceptionForRC(rc, db);
            }
        }

        return tasks;
    }

    public static List<string> ReadTags(SqliteConnection connection)
    {
        var tags = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM tags ORDER BY position, rowid;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tags.Add(reader.GetString(0));
        }

        return tags;
    }

    public static AppSettings ReadSettings(SqliteConnection connection)
    {
        var pairs = new List<KeyValuePair<string, string>>(SettingsMapping.Keys.Count);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM settings;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            pairs.Add(new(reader.GetString(0), reader.GetString(1)));
        }

        return SettingsMapping.FromPairs(pairs);
    }

    private static bool IsNull(sqlite3_stmt statement, int column) =>
        raw.sqlite3_column_type(statement, column) == raw.SQLITE_NULL;

    /// <summary>The value as UTF-8 bytes (column_blob returns a TEXT value's bytes without conversion).</summary>
    private static ReadOnlySpan<byte> Utf8(sqlite3_stmt statement, int column) => raw.sqlite3_column_blob(statement, column);

    private static string? Text(sqlite3_stmt statement, int column)
    {
        if (IsNull(statement, column))
        {
            return null;
        }

        var bytes = Utf8(statement, column);
        return bytes.IsEmpty ? string.Empty : Encoding.UTF8.GetString(bytes);
    }

    private static string? Tag(sqlite3_stmt statement, int column, Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> cache)
    {
        if (IsNull(statement, column))
        {
            return null;
        }

        var bytes = Utf8(statement, column);
        if (bytes.Length > 128)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        Span<char> chars = stackalloc char[128];
        int length = Encoding.UTF8.GetChars(bytes, chars);
        var name = chars[..length];
        if (!cache.TryGetValue(name, out string? tag))
        {
            tag = new string(name);
            cache[name] = tag;
        }

        return tag;
    }

    private static DateOnly? Date(sqlite3_stmt statement, int column, long id, string columnName)
    {
        if (IsNull(statement, column))
        {
            return null;
        }

        var bytes = Utf8(statement, column);
        return TryParseDate(bytes, out var date)
            ? date
            : throw new InvalidDataException($"Task {id}: column {columnName} holds '{Encoding.UTF8.GetString(bytes)}', expected a date in the form yyyy-MM-dd.");
    }

    private static TimeOnly? Time(sqlite3_stmt statement, int column, long id)
    {
        if (IsNull(statement, column))
        {
            return null;
        }

        var bytes = Utf8(statement, column);
        return TryParseTime(bytes, out var time)
            ? time
            : throw new InvalidDataException($"Task {id}: column time holds '{Encoding.UTF8.GetString(bytes)}', expected a time in the form HH:mm.");
    }

    /// <summary>Exactly "yyyy-MM-dd" with a valid calendar date.</summary>
    internal static bool TryParseDate(ReadOnlySpan<byte> s, out DateOnly date)
    {
        date = default;
        if (s.Length != 10 || s[4] != (byte)'-' || s[7] != (byte)'-'
            || !TryDigits(s[..4], out int year) || !TryDigits(s.Slice(5, 2), out int month) || !TryDigits(s.Slice(8, 2), out int day)
            || year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    /// <summary>Exactly "HH:mm", 00:00–23:59.</summary>
    internal static bool TryParseTime(ReadOnlySpan<byte> s, out TimeOnly time)
    {
        time = default;
        if (s.Length != 5 || s[2] != (byte)':'
            || !TryDigits(s[..2], out int hour) || !TryDigits(s.Slice(3, 2), out int minute)
            || hour > 23 || minute > 59)
        {
            return false;
        }

        time = new TimeOnly(hour, minute);
        return true;
    }

    private static bool TryDigits(ReadOnlySpan<byte> s, out int value)
    {
        value = 0;
        foreach (byte b in s)
        {
            uint digit = (uint)(b - '0');
            if (digit > 9)
            {
                return false;
            }

            value = (value * 10) + (int)digit;
        }

        return true;
    }
}
