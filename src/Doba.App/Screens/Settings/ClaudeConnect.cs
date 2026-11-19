using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Doba.App.Screens.Settings;

/// <summary>
/// What Data › Connect Claude copies: the <c>mcpServers</c> entry for Claude Desktop's config and the
/// <c>claude mcp add</c> command for Claude Code, both starting Doba.Mcp.exe next to Doba.exe. <c>--data</c> is added
/// only when Doba runs on a folder other than the default one (Doba.Mcp finds the default itself).
/// </summary>
public static class ClaudeConnect
{
    /// <summary>The server's name in both clients (tools appear as <c>mcp__doba__…</c> in Claude Code).</summary>
    public const string ServerName = "doba";

    /// <summary>The MCP server's file name; it is published into the same folder as Doba.exe.</summary>
    public const string McpExeName = "Doba.Mcp.exe";

    /// <summary>Full path of Doba.Mcp.exe beside this executable.</summary>
    public static string McpExePath => Path.Combine(AppContext.BaseDirectory, McpExeName);

    /// <summary>True when <paramref name="dataFolder"/> is <paramref name="defaultFolder"/> (full paths, ignoring case
    /// and a trailing separator: Windows paths are case-insensitive).</summary>
    public static bool IsDefaultFolder(string dataFolder, string defaultFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultFolder);
        return string.Equals(Normalize(dataFolder), Normalize(defaultFolder), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The JSON to merge into <c>claude_desktop_config.json</c>, pretty-printed:
    /// <c>{"mcpServers":{"doba":{"command":"&lt;exe&gt;","args":["--data","&lt;folder&gt;"]}}}</c>, with <c>args</c> only
    /// for a non-default folder. Paths are JSON-escaped (backslashes doubled).
    /// </summary>
    public static string DesktopConfig(string exePath, string dataFolder, string defaultFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        var custom = CustomFolder(dataFolder, defaultFolder);
        var options = new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            // Not HTML: keep non-ASCII path characters readable; quotes and backslashes are still escaped.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, options))
        {
            json.WriteStartObject();
            json.WriteStartObject("mcpServers");
            json.WriteStartObject(ServerName);
            json.WriteString("command", exePath);
            if (custom is not null)
            {
                json.WriteStartArray("args");
                json.WriteStringValue("--data");
                json.WriteStringValue(custom);
                json.WriteEndArray();
            }

            json.WriteEndObject();
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The Claude Code command: <c>claude mcp add --scope user doba -- "&lt;exe&gt;"</c>, plus
    /// <c> --data "&lt;folder&gt;"</c> for a non-default folder (an .exe needs no <c>cmd /c</c>).
    /// </summary>
    public static string CodeCommand(string exePath, string dataFolder, string defaultFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        var custom = CustomFolder(dataFolder, defaultFolder);
        var command = "claude mcp add --scope user " + ServerName + " -- " + Quote(exePath);
        return custom is null ? command : command + " --data " + Quote(custom);
    }

    private static string? CustomFolder(string dataFolder, string defaultFolder) =>
        IsDefaultFolder(dataFolder, defaultFolder) ? null : Normalize(dataFolder);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string Quote(string value) => "\"" + value + "\"";
}
