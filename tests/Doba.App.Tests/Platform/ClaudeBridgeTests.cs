using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Doba.App.Platform;
using Doba.Bridge.Wire;

namespace Doba.App.Tests.Platform;

/// <summary>
/// The dispatch side of <see cref="ClaudeBridge"/> without a pipe: requests are queued to the UI thread and a closed gate
/// answers "closing" instead of running them. (Start would create a real pipe; that is Doba.Bridge.Tests' job.)
/// </summary>
public class ClaudeBridgeTests
{
    private static BridgeRequest Request(string tool = "get_today") => new() { Protocol = 1, Client = "test", Tool = tool };

    private static readonly BridgeResponse Done = BridgeResponse.Failure(BridgeErrorCodes.NotFound, "ran");

    [Fact]
    public void An_open_gate_runs_the_request()
    {
        var gate = new ClaudeBridge.Gate();
        BridgeRequest? seen = null;
        var request = Request();

        var response = ClaudeBridge.Run(request, gate, r => { seen = r; return Done; });

        Assert.Same(Done, response);
        Assert.Same(request, seen);
    }

    [Fact]
    public void A_closed_gate_answers_closing_without_running()
    {
        var gate = new ClaudeBridge.Gate();
        gate.Close();
        var ran = 0;

        var response = ClaudeBridge.Run(Request("delete_task"), gate, _ => { ran++; return Done; });

        Assert.False(response.Ok);
        Assert.Equal(BridgeErrorCodes.Closing, response.Error!.Code);
        Assert.Equal(ClaudeBridge.ClosingMessage, response.Error.Message);
        Assert.Equal(0, ran);
    }

    [AvaloniaFact]
    public void A_request_runs_on_the_ui_thread()
    {
        var gate = new ClaudeBridge.Gate();
        bool? onUiThread = null;

        // Queued from another thread, as the pipe host does.
        Task<BridgeResponse>? response = null;
        var host = new Thread(() => response = ClaudeBridge.DispatchAsync(Request(), gate, _ =>
        {
            onUiThread = Dispatcher.UIThread.CheckAccess();
            return Done;
        }, CancellationToken.None));
        host.Start();
        host.Join();
        Assert.NotNull(response);
        Assert.False(response.IsCompleted);

        Dispatcher.UIThread.RunJobs();

        Assert.True(response.IsCompletedSuccessfully);
        Assert.Same(Done, response.Result);
        Assert.True(onUiThread);
    }

    [AvaloniaFact]
    public void A_request_still_queued_when_the_gate_closes_answers_closing()
    {
        var gate = new ClaudeBridge.Gate();
        var ran = 0;

        var response = ClaudeBridge.DispatchAsync(Request("create_task"), gate, _ => { ran++; return Done; }, CancellationToken.None);
        Assert.False(response.IsCompleted); // queued, not run inline
        gate.Close(); // what Stop does first
        Dispatcher.UIThread.RunJobs();

        Assert.True(response.IsCompletedSuccessfully);
        Assert.Equal(BridgeErrorCodes.Closing, response.Result.Error!.Code);
        Assert.Equal(0, ran);
    }

    [AvaloniaFact]
    public void A_cancelled_connection_drops_its_queued_request()
    {
        var gate = new ClaudeBridge.Gate();
        var ran = 0;
        using var cancel = new CancellationTokenSource();

        var response = ClaudeBridge.DispatchAsync(Request(), gate, _ => { ran++; return Done; }, cancel.Token);
        cancel.Cancel();
        Dispatcher.UIThread.RunJobs();

        Assert.True(response.IsCanceled);
        Assert.Equal(0, ran);
    }

    [Fact]
    public void Stop_and_dispose_without_start_do_nothing()
    {
        var bridge = new ClaudeBridge();

        Assert.True(bridge.IsSupported);
        Assert.False(bridge.IsRunning);
        bridge.Stop();
        bridge.Dispose();
        bridge.Dispose();
        Assert.False(bridge.IsRunning);
        Assert.Throws<ObjectDisposedException>(() => bridge.Start(Path.GetTempPath(), _ => Done));
    }
}
