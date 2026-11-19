using System.Security.Cryptography;
using System.Text;

namespace Doba.Bridge;

/// <summary>
/// Names shared by Doba and Doba.Mcp for one data folder: the single-instance mutex Doba holds while it runs on the
/// folder, and the bridge pipe it listens on while Claude access is on.
/// </summary>
public static class BridgeNames
{
    /// <summary>Version of the pipe message format (<see cref="Wire.BridgeRequest.Protocol"/>).</summary>
    public const int Protocol = 1;

    /// <summary>The key of <paramref name="dataFolder"/>: a hash of the full, upper-cased path without a trailing
    /// separator (Windows paths are case-insensitive), 32 hex characters.</summary>
    public static string InstanceKey(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataFolder)).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)), 0, 16);
    }

    /// <summary>The per-session mutex a running Doba owns for <paramref name="dataFolder"/>.</summary>
    public static string MutexName(string dataFolder) => @"Local\Doba-" + InstanceKey(dataFolder);

    /// <summary>The bridge pipe's name (without the <c>\\.\pipe\</c> prefix) for <paramref name="dataFolder"/>.</summary>
    public static string PipeName(string dataFolder) => "Doba-" + InstanceKey(dataFolder) + "-mcp";

    /// <summary>Doba's default data folder, %LOCALAPPDATA%\Doba (the same as Doba.Storage's
    /// <c>DataLocation.Default()</c>).</summary>
    public static string DefaultDataFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Doba");
}
