using Avalonia.Threading;
using Doba.App.Platform;
using Doba.Bridge;
using Doba.Bridge.Wire;
using Doba.Core.Model;

namespace Doba.App.Services;

/// <summary>
/// Keeps the Claude bridge (<see cref="PlatformServices.Bridge"/>) in step with Settings → Data → Claude access: the
/// pipe exists only while the setting is on, never in demo mode, and never when the launch's platform plan has no
/// bridge (a no-op one). Each request runs <see cref="ToolExecutor"/> on the UI thread against the live store, the
/// settings of that moment (the access level is checked per request) and the app's clock.
/// </summary>
/// <remarks>
/// Order (AppLifecycle): <see cref="Start"/> after the first frame, after carry-over; <see cref="Stop"/> first on
/// Exit and on Windows session end. Everything here runs on the UI thread.
/// </remarks>
public sealed class ClaudeAccessService : IDisposable
{
    private readonly AppServices _app;
    private readonly Func<long?> _openEditId;
    private bool _started;
    private bool _stopped;

    /// <param name="openEditId">Id of the task open in the edit dialog right now (null: none, or a new task).</param>
    public ClaudeAccessService(AppServices app, Func<long?> openEditId)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(openEditId);
        _app = app;
        _openEditId = openEditId;
    }

    /// <summary>The bridge pipe is open.</summary>
    public bool IsListening => Bridge.IsRunning;

    private IClaudeBridge Bridge => _app.Platform.Bridge;

    /// <summary>A launch that may listen at all: a real data folder and a platform bridge.</summary>
    private bool CanListen => !_app.IsDemo && _app.DataFolder is not null && Bridge.IsSupported;

    /// <summary>Opens the bridge when Claude access is on and starts following the setting. Once only; nothing after
    /// <see cref="Stop"/>.</summary>
    public void Start()
    {
        if (_started || _stopped)
        {
            return;
        }

        _started = true;
        _app.Settings.Changed += OnSettingsChanged;
        Apply(_app.Settings.Current);
    }

    /// <summary>
    /// Closes the bridge for good: synchronous and bounded (requests still queued for the UI thread answer "closing";
    /// nothing waits for them). Idempotent; safe before <see cref="Start"/>.
    /// </summary>
    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        if (_started)
        {
            _app.Settings.Changed -= OnSettingsChanged;
        }

        Bridge.Stop();
    }

    public void Dispose() => Stop();

    /// <summary>One bridge request, on the UI thread.</summary>
    internal BridgeResponse Execute(BridgeRequest request)
    {
        try
        {
            return ToolExecutor.Execute(request, new ToolContext(_app.Store, _app.Settings.Current, _app.Clock, _openEditId()));
        }
        catch (Exception ex)
        {
            // A bug in a tool must not take the UI thread down; Claude gets a readable error, the log the details.
            _app.LogProblem("claude: " + request.Tool + " failed: " + ex.GetType().Name + ": " + ex.Message);
            return BridgeResponse.Failure(BridgeErrorCodes.Internal, "Doba could not run this request (" + ex.GetType().Name + ").");
        }
    }

    private void Apply(AppSettings settings)
    {
        if (_stopped)
        {
            return;
        }

        if (settings.ClaudeAccess && CanListen)
        {
            if (Bridge.IsRunning)
            {
                return;
            }

            try
            {
                Bridge.Start(_app.DataFolder!, Execute);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Doba carries on without the bridge; Doba.Mcp then reports "Claude access is off".
                _app.LogProblem("claude: the bridge pipe could not be opened: " + ex.Message);
            }
        }
        else if (Bridge.IsRunning)
        {
            Bridge.Stop();
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply(settings);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Apply(_app.Settings.Current));
        }
    }
}
