using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Doba.App.Platform.Windows;

/// <summary>"Launch at startup": <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> value <c>Doba</c>.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsAutostart : IAutostart
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string? _exePath;
    private readonly string _keyPath;
    private readonly string _valueName;

    public WindowsAutostart() : this(Environment.ProcessPath) { }

    internal WindowsAutostart(string? exePath) : this(exePath, RunKeyPath, AutostartCommand.ValueName) { }

    /// <summary>Against another HKCU key / value (<c>--selfcheck</c> uses <c>Software\Doba\SelfCheck</c>, never Run).</summary>
    internal WindowsAutostart(string? exePath, string keyPath, string valueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        _exePath = exePath;
        _keyPath = keyPath;
        _valueName = valueName;
    }

    public bool IsSupported => _exePath is not null;

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: false);
                return AutostartCommand.Matches(key?.GetValue(_valueName) as string, _exePath);
            }
            catch (Exception ex) when (IsRegistryFailure(ex))
            {
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_exePath is null)
        {
            return;
        }

        try
        {
            if (enabled)
            {
                using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
                key.SetValue(_valueName, AutostartCommand.Build(_exePath), RegistryValueKind.String);
            }
            else
            {
                // Removing never creates the key.
                using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
                key?.DeleteValue(_valueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            // Policy-locked or inaccessible key: leave it; callers re-read IsEnabled to show the real state.
        }
    }

    private static bool IsRegistryFailure(Exception ex) =>
        ex is SecurityException or UnauthorizedAccessException or IOException;
}
