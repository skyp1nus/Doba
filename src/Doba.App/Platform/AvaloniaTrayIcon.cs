using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Doba.App.Platform;

/// <summary>
/// The tray icon through Avalonia's <see cref="TrayIcon"/> (Windows: Shell_NotifyIcon): the app mark, tooltip "Doba",
/// left click opens the window, the context menu has Open Doba / New task / Quit. The native icon is created the first
/// time <see cref="IsVisible"/> becomes true (never on the startup path) and removed by <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// An Avalonia 12 <see cref="TrayIcon"/> has no native icon until it is in the application's
/// <see cref="TrayIcon.IconsProperty"/> collection (that is what attaches the platform implementation), so the icon is
/// added there. If the platform gives no native icon (headless, or the attach fails) <see cref="IsSupported"/> turns
/// false, and closing the window quits instead of hiding it where nothing could bring it back.
/// </remarks>
internal sealed class AvaloniaTrayIcon : ITrayIcon
{
    /// <summary>The multi-size app icon (16–256 px), also the window and exe icon.</summary>
    internal static readonly Uri IconUri = new("avares://Doba/Assets/doba.ico");

    private TrayIcon? _icon;
    private bool _disposed;
    private bool _unavailable;

    /// <summary>True until an attempt to create the icon found no native tray on this platform.</summary>
    public bool IsSupported => !_unavailable;

    /// <summary>True once the native icon exists (it may be hidden again).</summary>
    internal bool IsCreated => _icon is not null;

    /// <summary>The platform implementation is attached (Windows: the icon Shell_NotifyIcon works on).</summary>
    internal bool IsNative => _icon?.NativeMenuExporter is not null;

    public bool IsVisible
    {
        get => _icon?.IsVisible ?? false;
        set
        {
            if (_disposed || _unavailable || value == IsVisible)
            {
                return;
            }

            if (value && _icon is null)
            {
                _icon = Create();
                if (_icon is null)
                {
                    return;
                }
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
            _icon = null;
            icon.IsVisible = false;
            Release(icon);
        }

        OpenRequested = null;
        NewTaskRequested = null;
        QuitRequested = null;
    }

    /// <summary>Takes the icon out of the application's collection (which detaches the native icon) and disposes it.</summary>
    private static void Release(TrayIcon icon)
    {
        if (Application.Current is { } app && TrayIcon.GetIcons(app) is { } icons)
        {
            icons.Remove(icon);
        }

        icon.Dispose();
    }

    private TrayIcon? Create()
    {
        if (Application.Current is not { } app)
        {
            _unavailable = true;
            return null;
        }

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

        // Attaching is what creates the native icon: Avalonia does it for icons in the application's collection.
        try
        {
            if (TrayIcon.GetIcons(app) is { } icons)
            {
                icons.Add(icon);
            }
            else
            {
                TrayIcon.SetIcons(app, [icon]);
            }
        }
        catch (InvalidOperationException)
        {
            // No windowing platform that can host a tray icon.
        }
        catch (NotSupportedException)
        {
        }

        if (icon.NativeMenuExporter is null)
        {
            _unavailable = true;
            Release(icon);
            return null;
        }

        return icon;
    }
}
