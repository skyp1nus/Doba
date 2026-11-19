using Avalonia.Threading;
using Doba.Bridge;
using Doba.Bridge.Wire;

namespace Doba.App.Platform;

/// <summary>
/// The Claude bridge on Windows: a <see cref="BridgeHost"/> on <see cref="BridgeNames.PipeName"/> of the data folder.
/// The host reads requests on pool threads; each one is queued to the UI thread (the store is UI-thread only), where a
/// closing gate is checked first: once <see cref="Stop"/> ran, a request still in the queue answers
/// <see cref="BridgeErrorCodes.Closing"/> instead of touching the store. Each <see cref="Start"/> gets its own gate,
/// so a request left over from an earlier session never runs after a quick off/on.
/// </summary>
public sealed class ClaudeBridge : IClaudeBridge
{
    /// <summary>The <see cref="BridgeErrorCodes.Closing"/> answer.</summary>
    internal const string ClosingMessage = "Doba is closing, or Claude access was just turned off.";

    private BridgeHost? _host;
    private Gate? _gate;
    private bool _disposed;

    public bool IsSupported => true;

    /// <summary>The host listens. False again once it stopped itself (its pipe name was taken meanwhile), so the next
    /// settings change starts a new one, which fails while the name stays taken.</summary>
    public bool IsRunning => _host is { IsListening: true };

    public void Start(string dataFolder, Func<BridgeRequest, BridgeResponse> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        ArgumentNullException.ThrowIfNull(execute);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
        {
            return;
        }

        Stop(); // a host that stopped itself

        var gate = new Gate();
        var host = new BridgeHost(BridgeNames.PipeName(dataFolder), (request, ct) => DispatchAsync(request, gate, execute, ct));
        try
        {
            host.Start();
        }
        catch
        {
            gate.Close();
            host.Dispose();
            throw;
        }

        _gate = gate;
        _host = host;
    }

    public void Stop()
    {
        // The gate first: whatever is already queued for the UI thread answers "closing" from now on.
        _gate?.Close();
        _gate = null;
        if (_host is not { } host)
        {
            return;
        }

        _host = null;
        try
        {
            host.Stop();
        }
        finally
        {
            host.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    /// <summary>The host's handler: queues <see cref="Run"/> to the UI thread (cancelled with the connection).</summary>
    internal static Task<BridgeResponse> DispatchAsync(BridgeRequest request, Gate gate,
        Func<BridgeRequest, BridgeResponse> execute, CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(() => Run(request, gate, execute), DispatcherPriority.Normal, cancellationToken).GetTask();

    /// <summary>On the UI thread: "closing" once the gate is closed, otherwise the request itself.</summary>
    internal static BridgeResponse Run(BridgeRequest request, Gate gate, Func<BridgeRequest, BridgeResponse> execute)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(execute);
        return gate.IsClosed ? BridgeResponse.Failure(BridgeErrorCodes.Closing, ClosingMessage) : execute(request);
    }

    /// <summary>Closed once, by <see cref="Stop"/> (any thread may read it).</summary>
    internal sealed class Gate
    {
        private volatile bool _closed;

        public bool IsClosed => _closed;

        public void Close() => _closed = true;
    }
}
