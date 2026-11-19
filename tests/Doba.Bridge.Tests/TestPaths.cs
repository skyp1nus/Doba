using System.Reflection;

namespace Doba.Bridge.Tests;

/// <summary>Where the Doba.Mcp build output is.</summary>
internal static class TestPaths
{
    /// <summary>Doba.Mcp.exe to run: <c>DOBA_MCP_EXE</c> when set (e.g. the published NativeAOT exe), otherwise the
    /// build output of the same configuration.</summary>
    public static string McpExe =>
        Environment.GetEnvironmentVariable("DOBA_MCP_EXE") is { Length: > 0 } fromEnv ? fromEnv : BuiltMcpExe;

    /// <summary>Doba.Mcp.exe in the build output (next to its Doba.Mcp.dll).</summary>
    public static string BuiltMcpExe =>
        typeof(TestPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "DobaMcpExe").Value!;

    public static string BuiltMcpDll => Path.ChangeExtension(BuiltMcpExe, ".dll");
}
