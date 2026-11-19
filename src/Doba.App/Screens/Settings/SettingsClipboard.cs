using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace Doba.App.Screens.Settings;

/// <summary>Where Data › Connect Claude puts its text. The view supplies the window's clipboard; tests record.</summary>
public interface ISettingsClipboard
{
    /// <summary>Replaces the clipboard's content with <paramref name="text"/>; false when there is no clipboard.</summary>
    Task<bool> TrySetTextAsync(string text);
}

/// <summary>The clipboard of the window that hosts the settings view (Avalonia <see cref="TopLevel.Clipboard"/>).</summary>
public sealed class TopLevelClipboard(Func<TopLevel?> topLevel) : ISettingsClipboard
{
    public async Task<bool> TrySetTextAsync(string text)
    {
        if (topLevel()?.Clipboard is not { } clipboard)
        {
            return false;
        }

        await clipboard.SetTextAsync(text).ConfigureAwait(true);
        return true;
    }
}
