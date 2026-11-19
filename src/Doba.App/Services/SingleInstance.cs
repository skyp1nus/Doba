using System.Security.Cryptography;
using System.Text;

namespace Doba.App.Services;

/// <summary>
/// One Doba process per data folder. Two processes on the same doba.db would each keep their own in-memory store, hand
/// out the same new ids and overwrite each other's tasks, tags and settings, so <c>Program.Main</c> claims the folder
/// before the database is opened. A second launch on a claimed folder asks the running one to show its window
/// (<see cref="SignalPrimary"/>) and exits without touching the data.
/// </summary>
/// <remarks>
/// The claim is a per-session named mutex keyed on the full, case-folded folder path (<c>Local\Doba-&lt;hash&gt;</c>),
/// so <c>--data</c> folders never collide with %LOCALAPPDATA%\Doba or with each other. The mutex is owned by the
/// thread that claimed it (<c>Main</c>, the UI thread) and released in <see cref="Dispose"/> on that thread; a process
/// that dies leaves it abandoned, which the next claim takes over. The "show yourself" signal is a named auto-reset event
/// (Windows only: .NET has no named events elsewhere, where a second launch just exits).
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private const string Prefix = @"Local\Doba-";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _showRequest;
    private RegisteredWaitHandle? _listener;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle? showRequest, bool isPrimary)
    {
        _mutex = mutex;
        _showRequest = showRequest;
        IsPrimary = isPrimary;
    }

    /// <summary>True when this process owns the folder; false when another Doba process already runs on it.</summary>
    public bool IsPrimary { get; }

    /// <summary>The name part shared by the mutex and the event of <paramref name="dataFolder"/>: a hash of the full,
    /// upper-cased path without a trailing separator (Windows paths are case-insensitive).</summary>
    public static string KeyFor(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataFolder)).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)), 0, 16);
    }

    /// <summary>Claims <paramref name="dataFolder"/> for this process (see <see cref="IsPrimary"/>). Never blocks.</summary>
    public static SingleInstance Claim(string dataFolder)
    {
        var key = KeyFor(dataFolder);
        // The event first: a launch that loses the race for the mutex can always signal, even while the winner is
        // still between its claim and its first window.
        EventWaitHandle? showRequest = OperatingSystem.IsWindows()
            ? new EventWaitHandle(false, EventResetMode.AutoReset, Prefix + key + "-show")
            : null;
        var mutex = new Mutex(false, Prefix + key);
        bool owned;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // the previous owner died without releasing it: the folder is free, and now ours
        }

        return new SingleInstance(mutex, showRequest, owned);
    }

    /// <summary>Second launch: asks the process that owns the folder to show and activate its window.</summary>
    public void SignalPrimary()
    {
        if (!IsPrimary && !_disposed)
        {
            _showRequest?.Set();
        }
    }

    /// <summary>
    /// Owner: calls <paramref name="onShowRequested"/> (on a thread-pool thread) each time another launch on the same
    /// folder asks for the window. A request made before this call is delivered at once.
    /// </summary>
    public void ListenForShowRequests(Action onShowRequested)
    {
        ArgumentNullException.ThrowIfNull(onShowRequested);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsPrimary || _showRequest is null || _listener is not null)
        {
            return;
        }

        _listener = ThreadPool.RegisterWaitForSingleObject(_showRequest, (_, _) => onShowRequested(), null,
            Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Stops listening and gives the folder up. Call on the thread that claimed it.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener?.Unregister(null);
        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Called on another thread than the claim: the OS releases the mutex when this process ends.
            }
        }

        _mutex.Dispose();
        _showRequest?.Dispose();
    }
}
