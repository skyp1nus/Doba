namespace Doba.App.Platform;

/// <summary>
/// Builds and recognises the "Launch at startup" command line stored in <c>HKCU\...\Run\Doba</c>.
/// Pure string logic, so it is testable on every OS.
/// </summary>
internal static class AutostartCommand
{
    /// <summary>Name of the value under the Run key.</summary>
    public const string ValueName = "Doba";

    /// <summary>Flag the startup entry passes so the app starts hidden (to the tray) instead of showing the window.</summary>
    public const string MinimizedFlag = "--minimized";

    /// <summary><c>"&lt;exePath&gt;" --minimized</c> — the path is always quoted so folders with spaces work.</summary>
    public static string Build(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        if (exePath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("An executable path cannot contain a double quote.", nameof(exePath));
        }

        return $"\"{exePath}\" {MinimizedFlag}";
    }

    /// <summary>The executable part of a Run command line: the quoted prefix, or the first space-separated token.</summary>
    public static string? ExtractExePath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var s = commandLine.Trim();
        if (s[0] == '"')
        {
            var end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end] : null;
        }

        var space = s.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? s : s[..space];
    }

    /// <summary>True when <paramref name="commandLine"/> launches <paramref name="exePath"/> (Windows paths: case-insensitive).</summary>
    public static bool Matches(string? commandLine, string? exePath) =>
        exePath is not null
        && ExtractExePath(commandLine) is { } stored
        && string.Equals(stored, exePath, StringComparison.OrdinalIgnoreCase);
}
