using System.Reflection;
using Doba.Bridge;
using ModelContextProtocol.Server;

namespace Doba.Mcp;

/// <summary>
/// Doba's MCP server: Claude Desktop / Claude Code start it and talk JSON-RPC over stdin/stdout. It never opens the
/// database; every tool call goes to the Doba running on the same data folder (<see cref="BridgeClient"/>).
/// </summary>
internal static class Program
{
    private const string Usage = "Usage: Doba.Mcp [--data <folder>]";

    private static async Task<int> Main(string[] args)
    {
        // stdout belongs to the JSON-RPC transport (it writes to the raw stdout stream): anything else written through
        // Console.Out would corrupt the protocol, so it goes to stderr, which MCP clients log.
        Console.SetOut(Console.Error);

        if (!TryParse(args, out var dataFolder))
        {
            await Console.Error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        var version = Version();
        var client = new BridgeClient(dataFolder) { ClientName = "Doba.Mcp " + version };
        var options = McpTools.CreateOptions(client, version, stop.Token);

        // The stdio transport by hand: a stdin that reports its end at once (StdinStream), so a client that goes away
        // in the middle of a call does not leave the process waiting for Doba's answer.
        var transport = new StreamServerTransport(
            new StdinStream(Console.OpenStandardInput(), () => CancelQuietly(stop)),
            Console.OpenStandardOutput(),
            "doba");
        await using var server = McpServer.Create(transport, options);
        try
        {
            await server.RunAsync(stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // stdin ended (the client closed it or went away) or Ctrl+C: a normal exit.
        }
        finally
        {
            // stdin closed (the client is gone) or Ctrl+C: calls still waiting for Doba end now, so disposing the
            // server does not wait for them.
            await stop.CancelAsync().ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>Cancels <paramref name="stop"/> unless <c>Main</c> already disposed it (the end of input can be seen
    /// while the server shuts down).</summary>
    private static void CancelQuietly(CancellationTokenSource stop)
    {
        try
        {
            stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Main is past its last await: nothing is left to cancel.
        }
    }

    /// <summary>Accepts no arguments (the default data folder) or exactly <c>--data &lt;folder&gt;</c>.</summary>
    internal static bool TryParse(string[] args, out string dataFolder)
    {
        dataFolder = "";
        switch (args.Length)
        {
            case 0:
                dataFolder = BridgeNames.DefaultDataFolder();
                return true;
            case 2 when args[0] == "--data" && !string.IsNullOrWhiteSpace(args[1]):
                try
                {
                    dataFolder = Path.GetFullPath(args[1]);
                    return true;
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return false;
                }

            default:
                return false;
        }
    }

    /// <summary>The informational version without the source-revision suffix ("0.1.0", not "0.1.0+abc123").</summary>
    private static string Version()
    {
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
