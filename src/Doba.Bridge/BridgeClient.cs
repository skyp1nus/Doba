using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Doba.Bridge.Internal;
using Doba.Bridge.Wire;

namespace Doba.Bridge;

/// <summary>
/// Doba.Mcp's side of the bridge: one pipe connection per call (current-user-only), so parallel calls never share a
/// stream. Every failure (Doba not running, access off, timeout, bad answer) comes back as a
/// <see cref="BridgeResponse"/> failure with a <see cref="BridgeErrorCodes"/> code; only cancellation throws.
/// </summary>
/// <remarks>
/// When the pipe is missing, the single-instance mutex tells "Doba runs but Claude access is off (or Doba is still
/// starting)" from "Doba is not running"; the first case is retried for <see cref="StartingWait"/>. When the pipe exists
/// but all its instances are busy, connecting is retried for up to <see cref="RequestTimeout"/>. The client never starts
/// a process.
/// </remarks>
public sealed class BridgeClient
{
    /// <summary>How long one call waits for Doba's answer after sending.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>One connect attempt to an existing pipe (all instances may be busy for a moment).</summary>
    public static readonly TimeSpan ConnectAttempt = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a running Doba without a pipe is given to open it (it is starting, or access was just turned
    /// on) before the call reports that Claude access is off.</summary>
    public static readonly TimeSpan StartingWait = TimeSpan.FromSeconds(1);

    /// <summary>Pause between checks while waiting for the pipe to appear.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);

    public const string AccessOffMessage =
        "Doba is running, but Claude access is off (or Doba is still starting). Turn it on in Doba → Settings → Data → Claude access.";

    public const string UnauthorizedMessage =
        "Doba and Claude run as different users or privilege levels (e.g. one as administrator), so they cannot talk. Run both the same way.";

    public const string TimeoutMessage =
        "Doba did not answer in time; the change may have been applied — check with get_task / search_tasks before retrying.";

    public const string ClosedMessage =
        "Doba closed the connection before answering (it may be closing, or Claude access was just turned off). If this was a change, check with get_task / search_tasks before retrying.";

    public BridgeClient(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        DataFolder = dataFolder;
        PipeName = BridgeNames.PipeName(dataFolder);
        MutexName = BridgeNames.MutexName(dataFolder);
    }

    /// <summary>The data folder of the Doba this client talks to.</summary>
    public string DataFolder { get; }

    /// <summary>Who is calling, sent as <see cref="BridgeRequest.Client"/> (e.g. "Doba.Mcp 0.2.0").</summary>
    public string ClientName { get; init; } = "Doba.Mcp";

    private string PipeName { get; }

    private string MutexName { get; }

    public async Task<BridgeResponse> CallAsync(string tool, IDictionary<string, JsonElement>? args,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var request = new BridgeRequest
        {
            Protocol = BridgeNames.Protocol,
            Client = ClientName,
            Tool = tool,
            Args = args is null ? null : new Dictionary<string, JsonElement>(args, StringComparer.Ordinal),
        };
        var line = LineIO.ToLine(request, BridgeJsonContext.Default.BridgeRequest);
        if (line.Length > BridgeHost.MaxMessageBytes + 1)
        {
            return BridgeResponse.Failure(BridgeErrorCodes.InvalidArgs,
                "The request is larger than 1 MB; send shorter text (e.g. shorter notes).");
        }

        var (stream, failure) = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            return failure!;
        }

        await using (stream.ConfigureAwait(false))
        {
            return await ExchangeAsync(stream, line, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(NamedPipeClientStream? Stream, BridgeResponse? Failure)> ConnectAsync(CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var exists = PipeExists();
            if (exists != false)
            {
                var stream = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
                    PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                try
                {
                    await stream.ConnectAsync(ConnectAttempt, ct).ConfigureAwait(false);
                    return (stream, null);
                }
                catch (UnauthorizedAccessException)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    return (null, BridgeResponse.Failure(BridgeErrorCodes.Unauthorized, UnauthorizedMessage));
                }
                catch (Exception e) when (e is TimeoutException or IOException)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                if (exists == true)
                {
                    // The pipe is there but every instance is busy: keep trying, within the request timeout.
                    if (started.Elapsed >= RequestTimeout)
                    {
                        return (null, BridgeResponse.Failure(BridgeErrorCodes.Timeout,
                            "Doba is busy and did not take the call in time; nothing was sent. Try again."));
                    }

                    continue;
                }
            }

            // No pipe: is Doba running on this folder at all?
            if (!DobaIsRunning())
            {
                return (null, BridgeResponse.Failure(BridgeErrorCodes.NotRunning,
                    $"Doba is not running (data folder: {DataFolder}). Open Doba and try again."));
            }

            if (started.Elapsed >= StartingWait)
            {
                return (null, BridgeResponse.Failure(BridgeErrorCodes.AccessOff, AccessOffMessage));
            }

            await Task.Delay(RetryInterval, ct).ConfigureAwait(false);
        }
    }

    private static async Task<BridgeResponse> ExchangeAsync(NamedPipeClientStream stream, byte[] line, CancellationToken ct)
    {
        try
        {
            await stream.WriteAsync(line, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            return BridgeResponse.Failure(BridgeErrorCodes.Internal, ClosedMessage);
        }

        (LineStatus Status, byte[] Line) answer;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(RequestTimeout);
            try
            {
                answer = await LineIO.ReadLineAsync(stream, BridgeHost.MaxMessageBytes, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return BridgeResponse.Failure(BridgeErrorCodes.Timeout, TimeoutMessage);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                return BridgeResponse.Failure(BridgeErrorCodes.Internal, ClosedMessage);
            }
        }

        switch (answer.Status)
        {
            case LineStatus.Line:
                break;
            case LineStatus.TooLong:
                return BridgeResponse.Failure(BridgeErrorCodes.Internal, "Doba's answer was larger than 1 MB.");
            default:
                return BridgeResponse.Failure(BridgeErrorCodes.Internal, ClosedMessage);
        }

        BridgeResponse? response;
        try
        {
            response = JsonSerializer.Deserialize(answer.Line, BridgeJsonContext.Default.BridgeResponse);
        }
        catch (JsonException)
        {
            response = null;
        }

        if (response is null || (response.Ok ? response.Result is null : response.Error is null))
        {
            return BridgeResponse.Failure(BridgeErrorCodes.Internal, "Doba sent an answer Doba.Mcp cannot read.");
        }

        return response;
    }

    /// <summary>True/false when the pipe is / is not there; null when that cannot be told (then connecting decides).</summary>
    /// <remarks>Lists <c>\\.\pipe\</c> instead of opening the pipe: opening it would take a server instance.</remarks>
    private bool? PipeExists()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(@"\\.\pipe\"))
            {
                if (string.Equals(Path.GetFileName(path), PipeName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A running Doba holds the single-instance mutex of its data folder (a <c>--demo</c> run does not).</summary>
    private bool DobaIsRunning()
    {
        try
        {
            if (Mutex.TryOpenExisting(MutexName, out var mutex))
            {
                mutex.Dispose();
                return true;
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // it exists, owned by someone we may not open: Doba runs (perhaps elevated)
        }
    }
}
