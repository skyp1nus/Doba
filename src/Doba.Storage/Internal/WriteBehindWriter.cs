using System.Threading.Channels;
using Doba.Core.Model;
using Microsoft.Data.Sqlite;

namespace Doba.Storage.Internal;

internal abstract class WriteOp;

internal sealed class UpsertOp(TaskItem item) : WriteOp
{
    public TaskItem Item { get; } = item;
}

internal sealed class DeleteOp(long id) : WriteOp
{
    public long Id { get; } = id;
}

internal sealed class TagsOp(string[] tags) : WriteOp
{
    public string[] Tags { get; } = tags;
}

internal sealed class ReplaceAllOp(TaskItem[] tasks, string[] tags) : WriteOp
{
    public TaskItem[] Tasks { get; } = tasks;

    public string[] Tags { get; } = tags;
}

internal sealed class SettingsOp(AppSettings settings) : WriteOp
{
    public AppSettings Settings { get; } = settings;
}

/// <summary>
/// Completes once every operation queued before it is committed. With an <see cref="Action"/>, runs it on the writer
/// connection at that point (e.g. VACUUM INTO). Flush barriers (<see cref="ReportsWriteErrors"/>) fault with a
/// <see cref="StorageWriteException"/> when writes failed since the previous flush.
/// </summary>
internal sealed class BarrierOp(Action<SqliteConnection>? action, bool reportsWriteErrors, CancellationToken cancellationToken) : WriteOp
{
    public Action<SqliteConnection>? Action { get; } = action;

    public bool ReportsWriteErrors { get; } = reportsWriteErrors;

    /// <summary>The caller's token: a cancelled barrier neither runs its action nor consumes pending write errors.</summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;

    public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// The single background writer: owns the write connection and its prepared commands, drains a single-reader channel
/// and commits each burst of queued operations in one transaction.
/// </summary>
internal sealed class WriteBehindWriter : IAsyncDisposable
{
    /// <summary>Upper bound of operations per transaction, so a never-ending stream still commits regularly.</summary>
    private const int MaxOperationsPerTransaction = 4096;

    private readonly Channel<WriteOp> _channel = Channel.CreateUnbounded<WriteOp>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });

    private readonly SqliteConnection _connection;
    private readonly Action<Exception> _onError;
    private readonly SqliteCommand _upsertTask;
    private readonly SqliteCommand _deleteTask;
    private readonly SqliteCommand _deleteAllTasks;
    private readonly SqliteCommand _deleteAllTags;
    private readonly SqliteCommand _insertTag;
    private readonly SqliteCommand _upsertSetting;
    private readonly SqliteCommand[] _commands;
    private readonly Task _loop;

    // Writer-thread state.
    private int _failedSinceFlush;
    private Exception? _lastUnreported;

    private Exception? _lastError;
    private int _disposed;

    public WriteBehindWriter(SqliteConnection connection, Action<Exception> onError)
    {
        _connection = connection;
        _onError = onError;

        _upsertTask = Prepare("""
            INSERT INTO tasks (id, title, notes, date, end_date, time, duration, priority, tag, done)
            VALUES ($id, $title, $notes, $date, $end, $time, $duration, $priority, $tag, $done)
            ON CONFLICT (id) DO UPDATE SET
                title = excluded.title, notes = excluded.notes, date = excluded.date, end_date = excluded.end_date,
                time = excluded.time, duration = excluded.duration, priority = excluded.priority, tag = excluded.tag,
                done = excluded.done;
            """,
            ("$id", SqliteType.Integer), ("$title", SqliteType.Text), ("$notes", SqliteType.Text),
            ("$date", SqliteType.Text), ("$end", SqliteType.Text), ("$time", SqliteType.Text),
            ("$duration", SqliteType.Integer), ("$priority", SqliteType.Integer), ("$tag", SqliteType.Text),
            ("$done", SqliteType.Integer));
        _deleteTask = Prepare("DELETE FROM tasks WHERE id = $id;", ("$id", SqliteType.Integer));
        _deleteAllTasks = Prepare("DELETE FROM tasks;");
        _deleteAllTags = Prepare("DELETE FROM tags;");
        _insertTag = Prepare("INSERT INTO tags (name, position) VALUES ($name, $position);",
            ("$name", SqliteType.Text), ("$position", SqliteType.Integer));
        _upsertSetting = Prepare("""
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT (key) DO UPDATE SET value = excluded.value;
            """,
            ("$key", SqliteType.Text), ("$value", SqliteType.Text));
        _commands = [_upsertTask, _deleteTask, _deleteAllTasks, _deleteAllTags, _insertTag, _upsertSetting];

        _loop = Task.Run(RunAsync);
    }

    /// <summary>The most recent write failure (kept even after a flush has reported it).</summary>
    public Exception? LastError => Volatile.Read(ref _lastError);

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Enqueue(WriteOp op)
    {
        if (!_channel.Writer.TryWrite(op))
        {
            throw new ObjectDisposedException(nameof(DobaDatabase), "The database is closed; the write was not queued.");
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        var barrier = new BarrierOp(action: null, reportsWriteErrors: true, cancellationToken);
        if (!_channel.Writer.TryWrite(barrier))
        {
            // Closed: DisposeAsync drains everything (and reports failures itself); wait for that drain.
            return _loop.WaitAsync(cancellationToken);
        }

        return barrier.Completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Runs <paramref name="action"/> on the writer connection after everything queued before it is committed.</summary>
    public Task ExecuteAsync(Action<SqliteConnection> action, CancellationToken cancellationToken)
    {
        var barrier = new BarrierOp(action, reportsWriteErrors: false, cancellationToken);
        Enqueue(barrier);
        return barrier.Completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Drains the queue, closes the connection and throws <see cref="StorageWriteException"/> for unreported failures.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        await _loop.ConfigureAwait(false);

        foreach (var command in _commands)
        {
            command.Dispose();
        }

        // Closing the last connection checkpoints the WAL into the main file and removes doba.db-wal.
        await _connection.DisposeAsync().ConfigureAwait(false);

        if (_failedSinceFlush > 0 && _lastUnreported is not null)
        {
            var error = new StorageWriteException(_failedSinceFlush, _lastUnreported);
            _failedSinceFlush = 0;
            _lastUnreported = null;
            throw error;
        }
    }

    private async Task RunAsync()
    {
        var reader = _channel.Reader;
        var segment = new List<WriteOp>();
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            // Take the whole burst that is already queued; it becomes one transaction (split only at barriers).
            while (reader.TryRead(out var op))
            {
                if (op is BarrierOp barrier)
                {
                    Commit(segment);
                    Complete(barrier);
                }
                else
                {
                    segment.Add(op);
                    if (segment.Count >= MaxOperationsPerTransaction)
                    {
                        Commit(segment);
                    }
                }
            }

            Commit(segment);
        }
    }

    private void Complete(BarrierOp barrier)
    {
        if (barrier.CancellationToken.IsCancellationRequested)
        {
            // Nobody waits for this one any more; keep errors for the next flush that is awaited.
            barrier.Completion.TrySetCanceled(barrier.CancellationToken);
            return;
        }

        if (barrier.Action is { } action)
        {
            try
            {
                action(_connection);
                barrier.Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                barrier.Completion.TrySetException(ex);
            }

            return;
        }

        if (barrier.ReportsWriteErrors && _failedSinceFlush > 0 && _lastUnreported is not null)
        {
            barrier.Completion.TrySetException(new StorageWriteException(_failedSinceFlush, _lastUnreported));
            _failedSinceFlush = 0;
            _lastUnreported = null;
            return;
        }

        barrier.Completion.TrySetResult();
    }

    /// <summary>Commits <paramref name="segment"/> in one transaction and clears it. Never throws.</summary>
    private void Commit(List<WriteOp> segment)
    {
        if (segment.Count == 0)
        {
            return;
        }

        var effective = Coalesce(segment);
        segment.Clear();
        try
        {
            ApplyInTransaction(effective);
        }
        catch (Exception batchError)
        {
            if (effective.Count == 1)
            {
                RecordFailure(batchError);
                return;
            }

            // Something in the batch failed and the transaction was rolled back. Replay the operations one by one so
            // that a single bad operation does not take the rest of the burst with it.
            foreach (var op in effective)
            {
                try
                {
                    ApplyInTransaction([op]);
                }
                catch (Exception opError)
                {
                    RecordFailure(opError);
                }
            }
        }
    }

    /// <summary>
    /// Drops operations a later one in the same burst makes irrelevant: everything on tasks/tags before the last
    /// ReplaceAll, tag lists before the last tag list, settings before the last settings.
    /// </summary>
    private static List<WriteOp> Coalesce(List<WriteOp> segment)
    {
        int lastReplace = segment.FindLastIndex(static op => op is ReplaceAllOp);
        int lastTags = segment.FindLastIndex(static op => op is TagsOp);
        int lastSettings = segment.FindLastIndex(static op => op is SettingsOp);
        int start = Math.Max(lastReplace, 0);
        var effective = new List<WriteOp>(segment.Count - start + 1);
        for (int i = 0; i < segment.Count; i++)
        {
            var op = segment[i];
            bool keep = op switch
            {
                SettingsOp => i == lastSettings,
                TagsOp => i == lastTags && i >= start,
                _ => i >= start,
            };
            if (keep)
            {
                effective.Add(op);
            }
        }

        return effective;
    }

    private void ApplyInTransaction(List<WriteOp> ops)
    {
        using var transaction = _connection.BeginTransaction(deferred: false);
        foreach (var command in _commands)
        {
            command.Transaction = transaction;
        }

        foreach (var op in ops)
        {
            Apply(op);
        }

        transaction.Commit();
    }

    private void Apply(WriteOp op)
    {
        switch (op)
        {
            case UpsertOp upsert:
                UpsertTask(upsert.Item);
                break;
            case DeleteOp delete:
                _deleteTask.Parameters[0].Value = delete.Id;
                _deleteTask.ExecuteNonQuery();
                break;
            case TagsOp tags:
                WriteTags(tags.Tags);
                break;
            case ReplaceAllOp replace:
                _deleteAllTasks.ExecuteNonQuery();
                foreach (var task in replace.Tasks)
                {
                    UpsertTask(task);
                }

                WriteTags(replace.Tags);
                break;
            case SettingsOp settings:
                foreach (var (key, value) in SettingsMapping.ToPairs(settings.Settings))
                {
                    _upsertSetting.Parameters[0].Value = key;
                    _upsertSetting.Parameters[1].Value = value;
                    _upsertSetting.ExecuteNonQuery();
                }

                break;
            default:
                throw new InvalidOperationException($"Unknown write operation {op.GetType().Name}.");
        }
    }

    private void UpsertTask(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var p = _upsertTask.Parameters;
        p[0].Value = task.Id;
        p[1].Value = task.Title ?? throw new ArgumentException($"Task {task.Id} has no title.", nameof(task));
        p[2].Value = task.Notes ?? string.Empty;
        p[3].Value = task.Date is { } date ? Formats.FormatDate(date) : DBNull.Value;
        p[4].Value = task.End is { } end ? Formats.FormatDate(end) : DBNull.Value;
        p[5].Value = task.Time is { } time ? Formats.FormatTime(time) : DBNull.Value;
        p[6].Value = task.DurationMinutes;
        p[7].Value = (int)task.Priority;
        p[8].Value = task.Tag is { } tag ? tag : DBNull.Value;
        p[9].Value = task.Done ? 1 : 0;
        _upsertTask.ExecuteNonQuery();
    }

    private void WriteTags(string[] tags)
    {
        _deleteAllTags.ExecuteNonQuery();
        for (int i = 0; i < tags.Length; i++)
        {
            _insertTag.Parameters[0].Value = tags[i] ?? throw new ArgumentException($"Tag at position {i} is null.", nameof(tags));
            _insertTag.Parameters[1].Value = i;
            _insertTag.ExecuteNonQuery();
        }
    }

    private void RecordFailure(Exception error)
    {
        _failedSinceFlush++;
        _lastUnreported = error;
        Volatile.Write(ref _lastError, error);
        _onError(error);
    }

    private SqliteCommand Prepare(string sql, params (string Name, SqliteType Type)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, type) in parameters)
        {
            command.Parameters.Add(name, type);
        }

        command.Prepare();
        return command;
    }
}
