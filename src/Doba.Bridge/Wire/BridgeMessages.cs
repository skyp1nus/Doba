using System.Text.Json;
using System.Text.Json.Serialization;

namespace Doba.Bridge.Wire;

/// <summary>One call from Doba.Mcp to Doba: one UTF-8 JSON line per connection.</summary>
public sealed class BridgeRequest
{
    /// <summary><see cref="BridgeNames.Protocol"/> of the sender.</summary>
    public int Protocol { get; set; }

    /// <summary>Who is calling, for Doba's log, e.g. "Doba.Mcp 0.2.0".</summary>
    public string Client { get; set; } = "";

    /// <summary>A <see cref="ToolDefinition.Name"/> from <see cref="ToolCatalog"/>.</summary>
    public string Tool { get; set; } = "";

    /// <summary>The tool's arguments as the MCP client sent them; null when there were none.</summary>
    public Dictionary<string, JsonElement>? Args { get; set; }
}

/// <summary>Doba's answer to one <see cref="BridgeRequest"/>: <see cref="Result"/> when <see cref="Ok"/>, otherwise
/// <see cref="Error"/>.</summary>
public sealed class BridgeResponse
{
    public bool Ok { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BridgeError? Error { get; set; }

    public static BridgeResponse Success(JsonElement result) => new() { Ok = true, Result = result };

    public static BridgeResponse Failure(string code, string message) =>
        new() { Ok = false, Error = new BridgeError { Code = code, Message = message } };
}

/// <summary>Why a call failed: a <see cref="BridgeErrorCodes"/> value and a sentence the model can act on.</summary>
public sealed class BridgeError
{
    public string Code { get; set; } = "";

    public string Message { get; set; } = "";
}

/// <summary>Values of <see cref="BridgeError.Code"/>. The first group comes from Doba, the second from the client
/// side (<see cref="BridgeClient"/>).</summary>
public static class BridgeErrorCodes
{
    /// <summary>A change was asked for while Claude may only read.</summary>
    public const string ReadOnly = "read_only";

    /// <summary>Missing, unknown or invalid arguments.</summary>
    public const string InvalidArgs = "invalid_args";

    /// <summary>No task with that id.</summary>
    public const string NotFound = "not_found";

    /// <summary>The task is open in Doba's editor.</summary>
    public const string Conflict = "conflict";

    /// <summary>Doba is shutting down or Claude access was just turned off.</summary>
    public const string Closing = "closing";

    /// <summary>Doba and Doba.Mcp speak different <see cref="BridgeNames.Protocol"/> versions.</summary>
    public const string ProtocolMismatch = "protocol_mismatch";

    /// <summary>Anything unexpected.</summary>
    public const string Internal = "internal";

    /// <summary>No Doba runs on the data folder (client side).</summary>
    public const string NotRunning = "not_running";

    /// <summary>Doba runs but does not listen: Claude access is off, or Doba is still starting (client side).</summary>
    public const string AccessOff = "access_off";

    /// <summary>The pipe belongs to another user or privilege level (client side).</summary>
    public const string Unauthorized = "unauthorized";

    /// <summary>No answer within <see cref="BridgeClient.RequestTimeout"/> (client side).</summary>
    public const string Timeout = "timeout";
}

/// <summary>Source-generated JSON for the pipe messages (snake_case names; no reflection, NativeAOT-safe).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(BridgeRequest))]
[JsonSerializable(typeof(BridgeResponse))]
[JsonSerializable(typeof(BridgeError))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public sealed partial class BridgeJsonContext : JsonSerializerContext;
