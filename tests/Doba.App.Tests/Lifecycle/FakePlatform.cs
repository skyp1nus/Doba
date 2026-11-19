using Avalonia.Controls;
using Doba.App.Platform;
using Doba.Bridge.Wire;

namespace Doba.App.Tests.Lifecycle;

/// <summary>Records "Launch at startup" writes; <see cref="IsEnabled"/> follows them like the registry would.</summary>
internal sealed class FakeAutostart(bool supported = true, bool enabled = false) : IAutostart
{
    public List<bool> Writes { get; } = [];

    public bool IsSupported => supported;

    public bool IsEnabled { get; set; } = enabled;

    public void SetEnabled(bool enabled)
    {
        Writes.Add(enabled);
        IsEnabled = enabled;
    }
}

/// <summary>A hotkey whose registration can be refused and whose press is raised by the test.</summary>
internal sealed class FakeHotkey(bool accepts = true) : IGlobalHotkey
{
    public int RegisterCalls { get; private set; }

    public bool IsRegistered { get; private set; }

    public bool IsDisposed { get; private set; }

    public bool IsSupported => true;

    public event EventHandler? Pressed;

    public bool Register(TopLevel owner)
    {
        RegisterCalls++;
        IsRegistered = accepts;
        return accepts;
    }

    public void Press() => Pressed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        IsDisposed = true;
        IsRegistered = false;
    }
}

/// <summary>OS preferences the test controls; <see cref="Change"/> flips "Animation effects" and raises the event.</summary>
internal sealed class FakePreferences(bool animations = true, bool backdrop = false, bool mica = false) : ISystemPreferences
{
    public bool AnimationsEnabled { get; private set; } = animations;

    public bool SupportsBackdrop => backdrop;

    public bool PrefersMica => mica;

    public int Watches { get; private set; }

    public event EventHandler? AnimationsChanged;

    public event EventHandler? ClockChanged;

    public void Watch(TopLevel owner) => Watches++;

    public void Change(bool enabled)
    {
        AnimationsEnabled = enabled;
        AnimationsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What WM_TIMECHANGE does: the system time or time zone was changed.</summary>
    public void ChangeClock() => ClockChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>A tray icon whose menu items the test "clicks".</summary>
internal sealed class FakeTray(bool supported = true) : ITrayIcon
{
    private bool _visible;

    public bool IsSupported => supported;

    public bool IsVisible
    {
        get => _visible;
        set
        {
            _visible = value;
            VisibilityChanges++;
        }
    }

    public int VisibilityChanges { get; private set; }

    public bool IsDisposed { get; private set; }

    public event EventHandler? OpenRequested;

    public event EventHandler? NewTaskRequested;

    public event EventHandler? QuitRequested;

    public void ClickIcon() => OpenRequested?.Invoke(this, EventArgs.Empty);

    public void ClickNewTask() => NewTaskRequested?.Invoke(this, EventArgs.Empty);

    public void ClickQuit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        IsDisposed = true;
        _visible = false;
    }
}

/// <summary>
/// A Claude bridge that only records: no pipe. <see cref="Execute"/> is what the app handed to the last
/// <see cref="Start"/>; <see cref="OnStart"/> / <see cref="OnStop"/> let a test look at the app at that moment.
/// </summary>
internal sealed class FakeBridge(bool supported = true) : IClaudeBridge
{
    public List<string> Starts { get; } = [];

    public int Stops { get; private set; }

    public bool IsDisposed { get; private set; }

    public Func<BridgeRequest, BridgeResponse>? Execute { get; private set; }

    public Action? OnStart { get; set; }

    public Action? OnStop { get; set; }

    public bool IsSupported => supported;

    public bool IsRunning { get; private set; }

    public void Start(string dataFolder, Func<BridgeRequest, BridgeResponse> execute)
    {
        if (IsRunning)
        {
            return;
        }

        OnStart?.Invoke(); // may throw, like a pipe that cannot be created
        Starts.Add(dataFolder);
        Execute = execute;
        IsRunning = true;
    }

    public void Stop()
    {
        Stops++;
        if (IsRunning)
        {
            OnStop?.Invoke();
        }

        IsRunning = false;
    }

    public void Dispose()
    {
        IsDisposed = true;
        IsRunning = false;
    }
}

/// <summary>The five fakes bundled as <see cref="PlatformServices"/> (the shell integration stays a no-op).</summary>
internal sealed class FakePlatform
{
    public FakeAutostart Autostart { get; init; } = new();

    public FakeHotkey Hotkey { get; init; } = new();

    public FakePreferences Preferences { get; init; } = new();

    public FakeTray Tray { get; init; } = new();

    public FakeBridge Bridge { get; init; } = new();

    public PlatformServices Services => PlatformServices.NoOp() with
    {
        Autostart = Autostart,
        Hotkey = Hotkey,
        Preferences = Preferences,
        Tray = Tray,
        Bridge = Bridge,
    };
}
