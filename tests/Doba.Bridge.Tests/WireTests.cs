using System.Text.Json;
using Doba.Bridge.Wire;

namespace Doba.Bridge.Tests;

public sealed class WireTests
{
    [Fact]
    public void Request_round_trips_with_snake_case_names()
    {
        using var args = JsonDocument.Parse("""{"title":"Buy bread","date":"2026-11-20"}""");
        var request = new BridgeRequest
        {
            Protocol = BridgeNames.Protocol,
            Client = "Doba.Mcp 0.2.0",
            Tool = ToolCatalog.CreateTask,
            Args = args.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
        };

        var json = JsonSerializer.Serialize(request, BridgeJsonContext.Default.BridgeRequest);
        Assert.Equal("""{"protocol":1,"client":"Doba.Mcp 0.2.0","tool":"create_task","args":{"title":"Buy bread","date":"2026-11-20"}}""", json);

        var back = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeRequest)!;
        Assert.Equal("create_task", back.Tool);
        Assert.Equal("Buy bread", back.Args!["title"].GetString());
    }

    [Fact]
    public void Failure_has_no_result_and_success_has_no_error()
    {
        var failure = JsonSerializer.Serialize(BridgeResponse.Failure(BridgeErrorCodes.ReadOnly, "Read only."),
            BridgeJsonContext.Default.BridgeResponse);
        Assert.Equal("""{"ok":false,"error":{"code":"read_only","message":"Read only."}}""", failure);

        using var result = JsonDocument.Parse("""{"today":"2026-11-19"}""");
        var success = JsonSerializer.Serialize(BridgeResponse.Success(result.RootElement.Clone()),
            BridgeJsonContext.Default.BridgeResponse);
        Assert.Equal("""{"ok":true,"result":{"today":"2026-11-19"}}""", success);

        var back = JsonSerializer.Deserialize(failure, BridgeJsonContext.Default.BridgeResponse)!;
        Assert.False(back.Ok);
        Assert.Equal(BridgeErrorCodes.ReadOnly, back.Error!.Code);
    }
}
