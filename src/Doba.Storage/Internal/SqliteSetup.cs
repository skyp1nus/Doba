using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Doba.Storage.Internal;

/// <summary>Native provider initialisation and the connection factory.</summary>
internal static class SqliteSetup
{
    // Explicit, static (reflection-free) registration of the bundled e_sqlite3 provider. Microsoft.Data.Sqlite also
    // tries this itself through Type.GetType; doing it here first keeps trimmed/NativeAOT builds independent of that.
    // A static readonly initialiser runs exactly once and is thread-safe.
    private static readonly bool s_initialized = Initialize();

    private static bool Initialize()
    {
        SQLitePCL.Batteries_V2.Init();
        return true;
    }

    public static void EnsureInitialized()
    {
        if (!s_initialized)
        {
            throw new InvalidOperationException("SQLite provider initialisation failed.");
        }
    }

    /// <summary>
    /// Opens a connection in WAL mode with synchronous=NORMAL. Pooling is off so that a disposed database really closes
    /// its file (the WAL is checkpointed and removed, and the folder can be moved or deleted).
    /// </summary>
    public static SqliteConnection Open(string path, bool create)
    {
        EnsureInitialized();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false,
            Cache = SqliteCacheMode.Private,
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            connection.Open();
            // journal_mode is persistent in the file; synchronous is per connection. WAL + NORMAL never loses a committed
            // transaction when the app crashes (only, at worst, the latest commits on an OS crash or power loss).
            Execute(connection, "PRAGMA journal_mode = WAL;");
            Execute(connection, "PRAGMA synchronous = NORMAL;");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    public static long ScalarInt64(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
