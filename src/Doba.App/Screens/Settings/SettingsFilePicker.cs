using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Doba.App.Screens.Settings;

/// <summary>Where Data › Export / Import get their streams. The view supplies the system pickers; tests inject files.</summary>
public interface ISettingsFilePicker
{
    /// <summary>Asks for a destination; null when cancelled. The caller disposes the stream.</summary>
    Task<Stream?> OpenExportStreamAsync(string suggestedFileName);

    /// <summary>Asks for a Doba export to read; null when cancelled. The caller disposes the stream.</summary>
    Task<Stream?> OpenImportStreamAsync();
}

/// <summary>The system save/open dialogs of the window that hosts the settings view (Avalonia StorageProvider).</summary>
public sealed class StorageProviderFilePicker(Func<TopLevel?> topLevel) : ISettingsFilePicker
{
    private static readonly FilePickerFileType[] s_json =
    [
        new("JSON") { Patterns = ["*.json"], MimeTypes = ["application/json"], AppleUniformTypeIdentifiers = ["public.json"] },
    ];

    public async Task<Stream?> OpenExportStreamAsync(string suggestedFileName)
    {
        if (topLevel()?.StorageProvider is not { CanSave: true } storage)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = s_json,
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);
        if (file is null)
        {
            return null;
        }

        using (file)
        {
            var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            if (stream.CanSeek)
            {
                stream.SetLength(0); // overwrite, never leave a tail of a longer previous file
            }

            return stream;
        }
    }

    public async Task<Stream?> OpenImportStreamAsync()
    {
        if (topLevel()?.StorageProvider is not { CanOpen: true } storage)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import",
            AllowMultiple = false,
            FileTypeFilter = s_json,
        }).ConfigureAwait(true);
        if (files.Count == 0)
        {
            return null;
        }

        using var file = files[0];
        return await file.OpenReadAsync().ConfigureAwait(true);
    }
}
