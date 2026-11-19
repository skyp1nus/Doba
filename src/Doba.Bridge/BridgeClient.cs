using System.Text.Json;
using Doba.Bridge.Wire;

namespace Doba.Bridge;

/// <summary>
/// Doba.Mcp's side of the bridge: one pipe connection per call (current-user-only), so parallel calls never share a
/// stream. Every failure (Doba not running, access off, timeout, bad answer) comes back as a
/// <see cref="BridgeResponse"/> failure with a <see cref="BridgeErrorCodes"/> code; only cancellation throws.
/// </summary>
public sealed class BridgeClient
{
    /// <summary>How long one call waits for Doba's answer after sending.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public BridgeClient(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        DataFolder = dataFolder;
    }

    /// <summary>The data folder of the Doba this client talks to.</summary>
    public string DataFolder { get; }

    public Task<BridgeResponse> CallAsync(string tool, IDictionary<string, JsonElement>? args,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
