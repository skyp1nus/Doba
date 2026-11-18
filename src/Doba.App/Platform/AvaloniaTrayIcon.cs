using Avalonia.Controls;
using Avalonia.Platform;

namespace Doba.App.Platform;

/// <summary>
/// The tray icon through Avalonia's <see cref="TrayIcon"/> (Windows: Shell_NotifyIcon): the app mark, tooltip "Doba",
/// left click opens the window, the context menu has Open Doba / New task / Quit. The native icon is created the first
/// time <see cref="IsVisible"/> becomes true (never on the startup path) and removed by <see cref="Dispose"/>.
/// </summary>
internal sealed class AvaloniaTrayIcon : ITrayIcon
{
    /// <summary>The multi-size app icon (16–256 px), also the window and exe icon.</summary>
    internal static readonly Uri IconUri = new("avares://Doba/Assets/doba.ico");

    private TrayIcon? _icon;
    private bool _disposed;

    public bool IsSupported => true;

    /// <summary>True once the native icon exists (it may be hidden again).</summary>
    internal bool IsCreated => _icon is not null;

    public bool IsVisible
    {
        get => _icon?.IsVisible ?? false;
        set
        {
            if (_disposed || value == IsVisible)
            {
                return;
            }

            if (value && _icon is null)
            {
                _icon = Create();
            }

            if (_icon is not null)
            {
                _icon.IsVisible = value;
            }
        }
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? NewTaskRequested;

    public event EventHandler? QuitRequested;

    /// <summary>The window/tray icon from the app resources.</summary>
    internal static WindowIcon LoadIcon()
    {
        using var stream = AssetLoader.Open(IconUri);
        return new WindowIcon(stream);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_icon is { } icon)
        {
            icon.IsVisible = false;
            icon.Dispose();
            _icon = null;
        }

        OpenRequested = null;
        NewTaskRequested = null;
        QuitRequested = null;
    }

    private TrayIcon Create()
    {
        var open = new NativeMenuItem("Open Doba");
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        var newTask = new NativeMenuItem("New task");
        newTask.Click += (_, _) => NewTaskRequested?.Invoke(this, EventArgs.Empty);
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);

        var icon = new TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = "Doba",
            Menu = new NativeMenu { open, newTask, new NativeMenuItemSeparator(), quit },
            IsVisible = false,
        };
        icon.Clicked += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        return icon;
    }
}
