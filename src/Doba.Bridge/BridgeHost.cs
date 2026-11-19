using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Doba.Bridge.Internal;
using Doba.Bridge.Wire;

namespace Doba.Bridge;

/// <summary>
/// Doba's side of the bridge: the named-pipe server (current user only, remote clients denied). One connection is one
/// request line and one response line; <c>handle</c> runs the request (in Doba: on the UI thread).
/// </summary>
/// <remarks>
/// <para>The pipe's ACL allows only the current user (read/write, create instance, synchronize) and denies NETWORK, and
/// its owner is the current user (what <see cref="PipeOptions.CurrentUserOnly"/> clients check). The first instance is
/// created with <see cref="PipeOptions.FirstPipeInstance"/>, so <see cref="Start"/> fails rather than join a pipe some
/// other process created under the same name.</para>
/// <para>A free listening instance is kept while fewer than <see cref="MaxConnections"/> connections are served: the next
/// one is created before the connected one is served. At the limit, clients wait for a free instance.</para>
/// <para>Nothing a client sends can stop the accept loop: every connection's errors end that connection only.</para>
/// </remarks>
public sealed class BridgeHost : IDisposable
{
    /// <summary>Connections served at once.</summary>
    public const int MaxConnections = 8;

    /// <summary>Largest request or response line, in bytes.</summary>
    public const int MaxMessageBytes = 1 << 20;

    /// <summary>How long a connected client has to send its request line.</summary>
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long writing the response may block on a client that does not read.</summary>
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);

    /// <summary>After the response, how long the host waits for the client to close its end (closing first could drop
    /// the unread answer).</summary>
    public static readonly TimeSpan LingerTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Pause before creating a pipe instance again after creating one failed.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly string _pipeName;
    private readonly Func<BridgeRequest, CancellationToken, Task<BridgeResponse>> _handle;
    private readonly Lock _gate = new();
    private Session? _session;
    private bool _disposed;

    public BridgeHost(string pipeName, Func<BridgeRequest, CancellationToken, Task<BridgeResponse>> handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(handle);
        _pipeName = pipeName;
        _handle = handle;
    }

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool IsListening
    {
        get
        {
            lock (_gate)
            {
                return _session is not null;
            }
        }
    }

    /// <summary>Creates the pipe and starts accepting connections. Does nothing while already listening; may be called
    /// again after <see cref="Stop"/>.</summary>
    /// <exception cref="UnauthorizedAccessException">The pipe name is in use (another process created it first).</exception>
    /// <exception cref="IOException">The pipe could not be created.</exception>
    /// <exception cref="PlatformNotSupportedException">Not on Windows.</exception>
    public void Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Claude bridge uses Windows named pipes.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null)
            {
                return;
            }

            _session = StartSession();
        }
    }

    /// <summary>Synchronous: refuses new work (<see cref="BridgeErrorCodes.Closing"/>), cancels accepts and reads,
    /// closes the pipe. Does not wait for requests already queued to <c>handle</c>.</summary>
    /// <remarks>Idempotent. A request whose <c>handle</c> finishes after this gets no answer: its connection is
    /// closed, and the client reports that Doba closed the connection.</remarks>
    public void Stop()
    {
        Session? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }

        session?.Stop();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        Stop();
    }

    [SupportedOSPlatform("windows")]
    private Session StartSession()
    {
        var session = new Session();
        session.Slots.Wait(0); // the first instance's slot
        NamedPipeServerStream first;
        try
        {
            first = CreateInstance(firstInstance: true);
        }
        catch
        {
            session.Stop();
            throw;
        }

        session.Track(first);
        _ = Task.Run(() => AcceptLoopAsync(session, first));
        return session;
    }

    [SupportedOSPlatform("windows")]
    private async Task AcceptLoopAsync(Session session, NamedPipeServerStream? listening)
    {
        var ct = session.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                listening ??= await NextInstanceAsync(session).ConfigureAwait(false);
                if (listening is null)
                {
                    return; // stopped
                }

                try
                {
                    await listening.WaitForConnectionAsync(ct).ConfigureAwait(false);
                }
                catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
                {
                    // Stopped, or a client connected and vanished at once: drop this instance and make another.
                    session.Release(listening);
                    listening = null;
                    continue;
                }

                var connected = listening;
                listening = null;

                // The next listening instance exists before the connected one is served (when a slot is free; at the
                // limit the served connections keep the pipe name alive and new clients wait for a slot).
                if (session.Slots.Wait(0))
                {
                    listening = TryCreate(session);
                }

                _ = ServeAsync(session, connected);
            }
        }
        catch (Exception)
        {
            // Never reached in practice (every await above is guarded); the loop must not fault unobserved.
        }
        finally
        {
            if (listening is not null)
            {
                session.Release(listening);
            }
        }
    }

    /// <summary>Waits for a free slot and creates a listening instance; null when the session stopped.</summary>
    [SupportedOSPlatform("windows")]
    private async Task<NamedPipeServerStream?> NextInstanceAsync(Session session)
    {
        var ct = session.Token;
        while (true)
        {
            try
            {
                await session.Slots.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (TryCreate(session) is { } instance)
            {
                return instance;
            }

            try
            {
                await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
    }

    /// <summary>A new tracked instance for a slot the caller holds; on failure the slot is given back and null returned.</summary>
    [SupportedOSPlatform("windows")]
    private NamedPipeServerStream? TryCreate(Session session)
    {
        NamedPipeServerStream instance;
        try
        {
            instance = CreateInstance(firstInstance: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            session.Slots.Release();
            return null;
        }

        if (!session.Track(instance))
        {
            instance.Dispose();
            session.Slots.Release();
            return null;
        }

        return instance;
    }

    [SupportedOSPlatform("windows")]
    private NamedPipeServerStream CreateInstance(bool firstInstance) =>
        NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            MaxConnections,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None),
            inBufferSize: 0,
            outBufferSize: 0,
            CreateSecurity());

    /// <summary>Allow the current user, deny NETWORK (remote clients), owner = the current user.</summary>
    [SupportedOSPlatform("windows")]
    internal static PipeSecurity CreateSecurity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("The current user has no SID.");
        var security = new PipeSecurity();
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance | PipeAccessRights.Synchronize,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        return security;
    }

    /// <summary>One connection: read the request line, run it, write the response line, close.</summary>
    private async Task ServeAsync(Session session, NamedPipeServerStream stream)
    {
        try
        {
            var ct = session.Token;
            BridgeResponse response;
            using (var read = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                read.CancelAfter(ReadTimeout);
                var (status, line) = await LineIO.ReadLineAsync(stream, MaxMessageBytes, read.Token).ConfigureAwait(false);
                if (status == LineStatus.Empty)
                {
                    return;
                }

                response = status == LineStatus.TooLong
                    ? BridgeResponse.Failure(BridgeErrorCodes.InvalidArgs,
                        "The request is larger than 1 MB; send shorter text (e.g. shorter notes).")
                    : await RunAsync(session, line).ConfigureAwait(false);
            }

            var bytes = LineIO.ToLine(response, BridgeJsonContext.Default.BridgeResponse);
            if (bytes.Length > MaxMessageBytes + 1)
            {
                bytes = LineIO.ToLine(BridgeResponse.Failure(BridgeErrorCodes.Internal,
                    "The answer is larger than 1 MB; ask for fewer tasks (a smaller limit or a shorter range)."),
                    BridgeJsonContext.Default.BridgeResponse);
            }

            using (var write = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                write.CancelAfter(WriteTimeout);
                await stream.WriteAsync(bytes, write.Token).ConfigureAwait(false);
                await stream.FlushAsync(write.Token).ConfigureAwait(false);
            }

            // Let the client read the answer and close its end first.
            using var linger = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linger.CancelAfter(LingerTimeout);
            var scratch = new byte[64];
            while (await stream.ReadAsync(scratch, linger.Token).ConfigureAwait(false) > 0)
            {
            }
        }
        catch (Exception)
        {
            // Timeouts, a client that went away, Stop() closing the stream: this connection ends, the host goes on.
        }
        finally
        {
            session.Release(stream);
        }
    }

    private async Task<BridgeResponse> RunAsync(Session session, byte[] line)
    {
        BridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(line, BridgeJsonContext.Default.BridgeRequest);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null)
        {
            return BridgeResponse.Failure(BridgeErrorCodes.InvalidArgs, "The request is not a valid bridge message (one JSON object per line).");
        }

        if (session.Token.IsCancellationRequested)
        {
            return Closing();
        }

        try
        {
            return await _handle(request, session.Token).ConfigureAwait(false)
                ?? BridgeResponse.Failure(BridgeErrorCodes.Internal, "Doba returned no answer.");
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
            return Closing();
        }
        catch (Exception e)
        {
            return BridgeResponse.Failure(BridgeErrorCodes.Internal, $"Doba failed to run {request.Tool}: {e.Message}");
        }
    }

    private static BridgeResponse Closing() =>
        BridgeResponse.Failure(BridgeErrorCodes.Closing,
            "Doba is closing or Claude access was just turned off; nothing was changed. Try again once Doba is open with Claude access on.");

    /// <summary>One Start…Stop period: its cancellation, its pipe instances and its connection slots.</summary>
    private sealed class Session : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly HashSet<NamedPipeServerStream> _streams = [];
        private bool _stopped;

        /// <summary>One slot per pipe instance (listening or connected), <see cref="MaxConnections"/> in all.</summary>
        public SemaphoreSlim Slots { get; } = new(MaxConnections, MaxConnections);

        public CancellationToken Token { get; }

        public Session() => Token = _cts.Token;

        /// <summary>Adds an instance; false (the caller disposes it) once the session stopped.</summary>
        public bool Track(NamedPipeServerStream stream)
        {
            lock (_streams)
            {
                if (_stopped)
                {
                    return false;
                }

                _streams.Add(stream);
                return true;
            }
        }

        /// <summary>Closes an instance and gives its slot back.</summary>
        public void Release(NamedPipeServerStream stream)
        {
            bool tracked;
            lock (_streams)
            {
                tracked = _streams.Remove(stream);
            }

            DisposeQuietly(stream);
            if (tracked)
            {
                Slots.Release();
            }
        }

        public void Stop()
        {
            NamedPipeServerStream[] streams;
            lock (_streams)
            {
                if (_stopped)
                {
                    return;
                }

                _stopped = true;
                streams = [.. _streams];
                _streams.Clear();
            }

            // Callbacks run on the thread pool, not on the caller's (UI) thread; the source is disposed once they ran
            // (a token of a cancelled, disposed source still reads as cancelled and never throws).
            _ = _cts.CancelAsync().ContinueWith(static (_, cts) => ((CancellationTokenSource)cts!).Dispose(), _cts,
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            foreach (var stream in streams)
            {
                DisposeQuietly(stream);
            }
        }

        /// <summary>Same as <see cref="Stop"/> (which disposes the cancellation source once cancelled).</summary>
        public void Dispose() => Stop();

        private static void DisposeQuietly(NamedPipeServerStream stream)
        {
            try
            {
                stream.Dispose();
            }
            catch (Exception)
            {
                // Closing a broken pipe can throw; the handle is gone either way.
            }
        }
    }
}
