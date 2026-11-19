using Doba.Bridge.Wire;

namespace Doba.Bridge;

/// <summary>
/// Doba's side of the bridge: the named-pipe server (current user only, remote clients denied). One connection is one
/// request line and one response line; <c>handle</c> runs the request (in Doba: on the UI thread).
/// </summary>
public sealed class BridgeHost : IDisposable
{
    /// <summary>Connections served at once.</summary>
    public const int MaxConnections = 8;

    /// <summary>Largest request or response line, in bytes.</summary>
    public const int MaxMessageBytes = 1 << 20;

    public BridgeHost(string pipeName, Func<BridgeRequest, CancellationToken, Task<BridgeResponse>> handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(handle);
        throw new NotImplementedException();
    }

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool IsListening => throw new NotImplementedException();

    /// <summary>Creates the pipe and starts accepting connections.</summary>
    public void Start() => throw new NotImplementedException();

    /// <summary>Synchronous: refuses new work (<see cref="BridgeErrorCodes.Closing"/>), cancels accepts and reads,
    /// closes the pipe. Does not wait for requests already queued to <c>handle</c>.</summary>
    public void Stop() => throw new NotImplementedException();

    public void Dispose() => throw new NotImplementedException();
}
