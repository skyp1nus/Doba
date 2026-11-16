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

    public WindowsAutostart() : this(Environment.ProcessPath) { }

    internal WindowsAutostart(string? exePath) => _exePath = exePath;

    public bool IsSupported => _exePath is not null;

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return AutostartCommand.Matches(key?.GetValue(AutostartCommand.ValueName) as string, _exePath);
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
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                key.SetValue(AutostartCommand.ValueName, AutostartCommand.Build(_exePath), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(AutostartCommand.ValueName, throwOnMissingValue: false);
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
