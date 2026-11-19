using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Doba.Storage.Internal;

/// <summary>Schema migrations keyed by <c>PRAGMA user_version</c>. Append one entry per schema change; never edit old ones.</summary>
internal static class Schema
{
    /// <summary>Ordered and contiguous: the entry with Version N migrates a database from version N-1 to N.</summary>
    internal static readonly IReadOnlyList<(int Version, string Sql)> Migrations =
    [
        (1, """
            CREATE TABLE tasks (
                id       INTEGER PRIMARY KEY,
                title    TEXT    NOT NULL,
                notes    TEXT    NOT NULL DEFAULT '',
                date     TEXT    NULL,
                end_date TEXT    NULL,
                time     TEXT    NULL,
                duration INTEGER NOT NULL DEFAULT 0,
                priority INTEGER NOT NULL DEFAULT 0,
                tag      TEXT    NULL,
                done     INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE tags (
                name     TEXT    PRIMARY KEY,
                position INTEGER NOT NULL
            );
            CREATE TABLE settings (
                key      TEXT    PRIMARY KEY,
                value    TEXT    NOT NULL
            );
            """),
    ];

    public static int LatestVersion => Migrations[^1].Version;

    public static int GetVersion(SqliteConnection connection) =>
        checked((int)SqliteSetup.ScalarInt64(connection, "PRAGMA user_version;"));

    /// <summary>Brings the database to <see cref="LatestVersion"/>; each step runs in its own transaction.</summary>
    /// <exception cref="NotSupportedException">The file was written by a newer Doba (higher schema version).</exception>
    public static void Migrate(SqliteConnection connection)
    {
        int current = GetVersion(connection);
        if (current > LatestVersion)
        {
            throw new NotSupportedException(
                $"The database was created by a newer version of Doba (schema version {current}; this version supports up to {LatestVersion}).");
        }

        foreach (var (version, sql) in Migrations)
        {
            if (version <= current)
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            SqliteSetup.Execute(connection, sql, transaction);
            // user_version lives in the file header and is committed atomically together with the DDL.
            SqliteSetup.Execute(connection, "PRAGMA user_version = " + version.ToString(CultureInfo.InvariantCulture) + ";", transaction);
            transaction.Commit();
            current = version;
        }
    }
}
