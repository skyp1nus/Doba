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

    /// <summary>
    /// Operations that failed with a transient error (<see cref="IsTransientError"/>), in queue order. They are applied
    /// first, ahead of the next burst, flush or close, and dropped only when that later attempt fails permanently.
    /// </summary>
    private readonly List<WriteOp> _retry = [];

    /// <summary>
    /// The kept operations have failed since the last barrier (flush, close). Such a barrier only reports that failure; it
    /// does not try them once more at once, which would just wait out the busy timeout again. The next burst carries
    /// them, and so does the next barrier after that.
    /// </summary>
    private bool _retryJustFailed;

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
            _retry.Clear();
            throw error;
        }

        if (_retry.Count > 0 && LastError is { } last)
        {
            // Kept after a transient failure that a flush already reported, and still not written at close: lost now.
            var lost = new StorageWriteException(_retry.Count, last);
            _retry.Clear();
            throw lost;
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
                    // A barrier tries kept writes even with nothing new queued, unless they failed since the last
                    // barrier: then it only reports that failure (trying again at once would only wait out the busy
                    // timeout again); the next burst, or the next barrier, tries them.
                    Commit(segment, retryKept: !_retryJustFailed);
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

        // Closing with writes kept after a transient failure: one last attempt, unless the last commit has just tried
        // them (another immediate attempt would only wait out the busy timeout again). DisposeAsync reports what is left.
        if (!_retryJustFailed)
        {
            Commit(segment, retryKept: true);
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

        _retryJustFailed = false; // reported (or about to be): the next barrier tries the kept writes again

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

    /// <summary>
    /// Commits the operations kept for a retry and then <paramref name="segment"/>, in queue order, in one transaction,
    /// and clears <paramref name="segment"/>. Never throws. Operations that fail with a transient error are kept (in
    /// order) for the next attempt; those that fail permanently are dropped. Every failure is recorded.
    /// </summary>
    /// <param name="retryKept">Attempt the kept operations even when <paramref name="segment"/> is empty (barriers and
    /// close); a burst always carries them.</param>
    private void Commit(List<WriteOp> segment, bool retryKept = false)
    {
        if (segment.Count == 0 && (_retry.Count == 0 || !retryKept))
        {
            return;
        }

        List<WriteOp> effective;
        if (_retry.Count > 0)
        {
            // Kept operations go first; Coalesce then drops what a later operation makes irrelevant across both.
            _retry.AddRange(segment);
            effective = Coalesce(_retry);
            _retry.Clear();
        }
        else
        {
            effective = Coalesce(segment);
        }

        segment.Clear();
        try
        {
            ApplyInTransaction(effective);
            _retryJustFailed = false;
        }
        catch (Exception batchError)
        {
            if (effective.Count == 1 || !IsPerOperationError(batchError))
            {
                // One operation, or the database as a whole refused the write (locked by another process, disk full,
                // I/O error): every replayed operation would meet the same error, a locked one only after the full busy
                // timeout each (N × 30 s, while Exit waits for this drain). The burst fails as a whole; a transient
                // refusal keeps it for the next attempt.
                RecordFailure(batchError, effective.Count);
                if (IsTransientError(batchError))
                {
                    Keep(effective, 0);
                }

                return;
            }

            // Something in the batch failed and the transaction was rolled back. Replay the operations one by one so
            // that a single bad operation does not take the rest of the burst with it.
            for (var i = 0; i < effective.Count; i++)
            {
                try
                {
                    ApplyInTransaction([effective[i]]);
                }
                catch (Exception opError)
                {
                    if (IsTransientError(opError))
                    {
                        // The database became unavailable mid-replay: keep this and everything after it, in order
                        // (applying a later operation now could put it before this one, e.g. a delete before the
                        // upsert it follows).
                        RecordFailure(opError, effective.Count - i);
                        Keep(effective, i);
                        return;
                    }

                    RecordFailure(opError); // permanent (a constraint, a bad argument): dropped
                }
            }
        }
    }

    /// <summary>Keeps <paramref name="ops"/> from <paramref name="from"/> on for the next attempt.</summary>
    private void Keep(List<WriteOp> ops, int from)
    {
        for (var i = from; i < ops.Count; i++)
        {
            _retry.Add(ops[i]);
        }

        _retryJustFailed = true;
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

    /// <summary>
    /// True when <paramref name="error"/> comes from one operation's data (a constraint, a bad argument), so replaying
    /// the burst op by op saves the others; false for errors of the database as a whole, which every operation would
    /// hit again.
    /// </summary>
    internal static bool IsPerOperationError(Exception error) =>
        error is not SqliteException sqlite || (sqlite.SqliteErrorCode & 0xFF) switch
        {
            SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED or SQLitePCL.raw.SQLITE_NOMEM
                or SQLitePCL.raw.SQLITE_READONLY or SQLitePCL.raw.SQLITE_IOERR or SQLitePCL.raw.SQLITE_CORRUPT
                or SQLitePCL.raw.SQLITE_FULL or SQLitePCL.raw.SQLITE_CANTOPEN or SQLitePCL.raw.SQLITE_NOTADB => false,
            _ => true,
        };

    /// <summary>
    /// True when <paramref name="error"/> may well not happen on a later attempt — the database is busy or locked by
    /// another connection, an I/O error, the disk is full — so the operations are kept and retried rather than lost.
    /// Everything else (a constraint, misuse, a read-only or corrupt database, a bad argument) is permanent.
    /// </summary>
    internal static bool IsTransientError(Exception error) =>
        error is SqliteException sqlite && (sqlite.SqliteErrorCode & 0xFF) is SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED
            or SQLitePCL.raw.SQLITE_IOERR or SQLitePCL.raw.SQLITE_FULL;

    private void RecordFailure(Exception error, int failedWrites = 1)
    {
        _failedSinceFlush += failedWrites;
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
