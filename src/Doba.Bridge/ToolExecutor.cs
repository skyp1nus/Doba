using Doba.Bridge.Wire;

namespace Doba.Bridge;

/// <summary>Runs one bridge request against the store: checks the tool, its arguments and the access level, calls
/// Doba.Core and builds the JSON result. Pure logic, no threads; call it on the store's (UI) thread.</summary>
public static class ToolExecutor
{
    public static BridgeResponse Execute(BridgeRequest request, ToolContext context) =>
        throw new NotImplementedException();
}
