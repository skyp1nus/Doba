using System.Text.Json;
using Doba.Bridge;
using Doba.Bridge.Wire;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Doba.Mcp;

/// <summary>
/// The MCP side of <see cref="ToolCatalog"/>: <c>tools/list</c> and <c>tools/call</c> as SDK handlers (no attributes,
/// no reflection-built tools). A tool call is forwarded to Doba; whatever goes wrong there comes back as a tool result
/// with <c>isError: true</c> and a sentence the model can act on. Only an unknown tool name is a protocol error.
/// </summary>
internal static class McpTools
{
    /// <summary>How long a client may cache <c>tools/list</c>: the list is static (it never depends on the connection
    /// or on Doba's settings; Doba checks the access level per call).</summary>
    private static readonly TimeSpan ListTimeToLive = TimeSpan.FromDays(1);

    public static McpServerOptions CreateOptions(BridgeClient client, string version)
    {
        var tools = ToolCatalog.All.Select(ToSdkTool).ToList();
        return new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "doba", Title = "Doba", Version = version },
            ServerInstructions = ToolCatalog.ServerInstructions,
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
                {
                    Tools = tools,
                    TimeToLive = ListTimeToLive,
                    CacheScope = CacheScope.Public,
                }),
                CallToolHandler = (request, cancellationToken) => CallAsync(client, request.Params, cancellationToken),
            },
        };
    }

    internal static Tool ToSdkTool(ToolDefinition definition)
    {
        using var schema = JsonDocument.Parse(definition.InputSchemaJson);
        return new Tool
        {
            Name = definition.Name,
            Title = definition.Title,
            Description = definition.Description,
            InputSchema = schema.RootElement.Clone(),
            Annotations = new ToolAnnotations
            {
                Title = definition.Title,
                ReadOnlyHint = definition.ReadOnly,
                DestructiveHint = definition.Destructive,
                IdempotentHint = definition.Idempotent,
                OpenWorldHint = false,
            },
        };
    }

    private static async ValueTask<CallToolResult> CallAsync(BridgeClient client, CallToolRequestParams? request,
        CancellationToken cancellationToken)
    {
        var name = request?.Name ?? "";
        if (ToolCatalog.Find(name) is null)
        {
            throw new McpProtocolException($"Unknown tool '{name}'. Call tools/list for Doba's tools.",
                McpErrorCode.InvalidParams);
        }

        BridgeResponse response;
        try
        {
            response = await client.CallAsync(name, request!.Arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) // the last line of defence: a tool call answers the model, never ends the session
        {
            response = BridgeResponse.Failure(BridgeErrorCodes.Internal, "Doba.Mcp failed: " + e.Message);
        }

        return ToResult(response);
    }

    internal static CallToolResult ToResult(BridgeResponse response)
    {
        if (response.Ok && response.Result is { } result)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = result.GetRawText() }],
                StructuredContent = result,
            };
        }

        var message = response.Error?.Message is { Length: > 0 } m ? m : "Doba returned no result.";
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }],
        };
    }
}
