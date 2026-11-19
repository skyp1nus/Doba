using System.Diagnostics;
using Doba.Core.Store;
using Doba.Storage;

namespace Doba.App.Services;

/// <summary>
/// The daily data chores: carry-over (Settings › Tasks › "Carry over unfinished tasks") and the daily backup
/// (Settings › Data › "Daily backup"). Both run once when <see cref="Start"/> is called (after the first frame is on
/// screen, so they never delay it) and again on every <see cref="DayClock.DayChanged"/>. Never in demo mode.
/// </summary>
/// <remarks>
/// Carry-over is a store mutation, so it runs on the UI thread (one <see cref="TaskChangeKind.Batch"/> event, one
/// re-projection). The backup runs on a background task (<c>VACUUM INTO</c> on the storage writer); a failure is written
/// to <see cref="Trace"/> and otherwise ignored (the design has no UI for it). Days are serialised: a backup requested
/// while one runs waits for it.
/// </remarks>
public sealed class DataMaintenance : IAsyncDisposable
{
    /// <summary>Backups kept next to the data ("Keeps the last 7 days of backups next to your data").</summary>
    public const int BackupsKept = 7;

    private readonly AppServices _app;
    private readonly Func<DateOnly, CancellationToken, Task>? _backup;
    private Task _pendingBackup = Task.CompletedTask;
    private bool _started;
    private bool _disposed;

    public DataMaintenance(AppServices app)
        : this(app, app is { Database: { } db } ? (day, ct) => Backup.RunDailyAsync(db, day, BackupsKept, ct) : null)
    {
    }

    /// <summary>Tests pass their own <paramref name="backup"/> (null = no database, nothing to back up).</summary>
    internal DataMaintenance(AppServices app, Func<DateOnly, CancellationToken, Task>? backup)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
        _backup = backup;
    }

    /// <summary>False in demo mode: nothing is moved or backed up.</summary>
    public bool IsEnabled => !_app.IsDemo;

    /// <summary>The backup started last (completed when none runs). Exposed for tests and shutdown.</summary>
    public Task PendingBackup => _pendingBackup;

    /// <summary>Runs both chores for today and starts following <see cref="DayClock.DayChanged"/>. Idempotent.</summary>
    public void Start()
    {
        if (_started || _disposed || !IsEnabled)
        {
            return;
        }

        _started = true;
        _app.DayClock.DayChanged += OnDayChanged;
        Run(_app.DayClock.Today);
    }

    /// <summary>Carry-over then backup for <paramref name="today"/>, each only when its setting is on.</summary>
    public void Run(DateOnly today)
    {
        if (_disposed || !IsEnabled)
        {
            return;
        }

        var settings = _app.Settings.Current;
        if (settings.CarryOver)
        {
            CarryOver.Run(_app.Store, today);
        }

        if (settings.DailyBackup && _backup is { } backup)
        {
            var previous = _pendingBackup;
            _pendingBackup = Task.Run(() => RunBackupAsync(previous, backup, today));
        }
    }

    /// <summary>Stops following the day clock and waits for a running backup to finish (a VACUUM INTO of the data, short).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _app.DayClock.DayChanged -= OnDayChanged;
        await _pendingBackup.ConfigureAwait(false);
    }

    private void OnDayChanged(object? sender, DateOnly today) => Run(today);

    private static async Task RunBackupAsync(Task previous, Func<DateOnly, CancellationToken, Task> backup, DateOnly today)
    {
        await previous.ConfigureAwait(false); // never faults: every run catches its own failure
        try
        {
            await backup(today, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failed backup must never take the app down; it is reported and skipped until the next day.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Trace.WriteLine("Doba: daily backup failed: " + ex.Message);
        }
    }
}
