using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Doba.App.Platform;
using Doba.App.Services;
using Doba.App.Tests.Lifecycle;
using Doba.Bridge;
using Doba.Bridge.Wire;
using Doba.Core.Model;
using Doba.Core.Time;

namespace Doba.App.Tests.Platform;

/// <summary>
/// <see cref="ClaudeBridge"/> on a real pipe (a data folder under %TEMP%), talked to with <see cref="BridgeClient"/>
/// as Doba.Mcp does: requests reach the app's store on the UI thread, the access setting opens and closes the pipe, the
/// level is checked per request, and Exit answers a queued request "closing" without waiting for it. Everything but
/// the bridge is <see cref="PlatformServices.NoOp"/>.
/// </summary>
public class ClaudePipeTests
{
    private static readonly DateOnly Day = new(2026, 11, 15);

    private static FixedTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));

    private static TaskItem NewTask(string title, DateOnly? date = null) =>
        new(0, title, string.Empty, date, null, null, 0, Priority.None, null, false);

    /// <summary>One call as Doba.Mcp makes it, from a pool thread; the UI thread is pumped meanwhile (the bridge runs
    /// each request there).</summary>
    internal static BridgeResponse Call(BridgeClient client, string tool, string? argsJson = null)
    {
        var args = argsJson is null ? null : JsonSerializer.Deserialize(argsJson, BridgeJsonContext.Default.DictionaryStringJsonElement);
        return UiPump.Until(Task.Run(() => client.CallAsync(tool, args, CancellationToken.None)));
    }

    private static long TaskId(BridgeResponse response)
    {
        Assert.True(response.Ok, response.Error?.Message);
        return response.Result!.Value.GetProperty("task").GetProperty("id").GetInt64();
    }

    [AvaloniaFact]
    public void Start_opens_the_pipe_for_the_data_folder_and_Stop_closes_it()
    {
        using var folder = new TempFolder();
        var app = AppServices.Open(folder.Location, Clock(), PlatformServices.NoOp());
        try
        {
            var bread = app.Store.Add(NewTask("Buy bread", Day));
            var settings = AppSettings.Default with { ClaudeAccess = true };
            var onUiThread = new List<bool>();
            var client = new BridgeClient(folder.Path);
            using var bridge = new ClaudeBridge();

            bridge.Start(folder.Path, request =>
            {
                onUiThread.Add(Dispatcher.UIThread.CheckAccess());
                return ToolExecutor.Execute(request, new ToolContext(app.Store, settings, app.Clock, null));
            });
            Assert.True(bridge.IsRunning);

            var answer = Call(client, ToolCatalog.GetTask, $$"""{"id":{{bread.Id}}}""");
            Assert.True(answer.Ok, answer.Error?.Message);
            Assert.Equal("Buy bread", answer.Result!.Value.GetProperty("task").GetProperty("title").GetString());
            Assert.Equal("2026-11-15", answer.Result.Value.GetProperty("today").GetString());
            Assert.Equal([true], onUiThread);

            bridge.Stop();
            Assert.False(bridge.IsRunning);

            // No pipe, no Doba on the folder (nobody holds its mutex): "not running".
            var notRunning = Call(client, ToolCatalog.GetToday);
            Assert.Equal(BridgeErrorCodes.NotRunning, notRunning.Error!.Code);

            // No pipe, but a Doba holds the folder's mutex: "access off" (after the client's short wait for a start).
            using (new Mutex(initiallyOwned: false, BridgeNames.MutexName(folder.Path)))
            {
                var off = Call(client, ToolCatalog.GetToday);
                Assert.Equal(BridgeErrorCodes.AccessOff, off.Error!.Code);
                Assert.Equal(BridgeClient.AccessOffMessage, off.Error.Message);
            }

            Assert.Single(onUiThread); // nothing ran after Stop
        }
        finally
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void The_access_setting_opens_and_closes_the_pipe_and_the_level_is_checked_per_request()
    {
        using var folder = new TempFolder();
        var bridge = new ClaudeBridge();
        var app = AppServices.Open(folder.Location, Clock(), PlatformServices.NoOp() with { Bridge = bridge });
        var access = new ClaudeAccessService(app, () => null);
        var client = new BridgeClient(folder.Path);
        try
        {
            var seed = app.Store.Add(NewTask("Seed", Day));
            var changes = 0;
            app.Store.Changed += (_, _) => changes++;

            access.Start();
            Assert.False(bridge.IsRunning); // off by default
            Assert.Equal(BridgeErrorCodes.NotRunning, Call(client, ToolCatalog.GetToday).Error!.Code);

            // On, at the default level (Read): reads work, every change is refused and nothing is written.
            app.Settings.Update(s => s with { ClaudeAccess = true });
            Assert.True(bridge.IsRunning);
            Assert.True(access.IsListening);
            Assert.True(Call(client, ToolCatalog.GetToday).Ok);
            string[] writes =
            [
                ToolCatalog.CreateTask + """|{"title":"Nope"}""",
                ToolCatalog.UpdateTask + $$"""|{"id":{{seed.Id}},"title":"Nope"}""",
                ToolCatalog.SetTaskDone + $$"""|{"id":{{seed.Id}},"done":true}""",
                ToolCatalog.MoveTask + $$"""|{"id":{{seed.Id}},"date":"inbox"}""",
                ToolCatalog.DeleteTask + $$"""|{"id":{{seed.Id}}}""",
            ];
            foreach (var write in writes)
            {
                var parts = write.Split('|');
                var refused = Call(client, parts[0], parts[1]);
                Assert.False(refused.Ok, parts[0]);
                Assert.Equal(BridgeErrorCodes.ReadOnly, refused.Error!.Code);
            }

            Assert.Equal(0, changes);
            Assert.Equal(["Seed"], app.Store.All.Select(t => t.Title));

            // Read & edit: the next request may change the store (same pipe; the level is read per request).
            app.Settings.Update(s => s with { ClaudeAccessLevel = ClaudeAccessLevel.ReadWrite });
            Assert.True(bridge.IsRunning);
            var id = TaskId(Call(client, ToolCatalog.CreateTask, """{"title":"From Claude","date":"2026-11-15","time":"10:00"}"""));
            Assert.Equal(1, changes);
            var created = app.Store.Get(id);
            Assert.NotNull(created);
            Assert.Equal(("From Claude", Day, new TimeOnly(10, 0)), (created.Title, created.Date, created.Time));
            UiPump.Until(app.FlushAsync());
            Assert.Contains("From Claude", folder.TitlesOnDisk());

            // Off: the pipe goes; back on: it comes back.
            app.Settings.Update(s => s with { ClaudeAccess = false });
            Assert.False(bridge.IsRunning);
            Assert.Equal(BridgeErrorCodes.NotRunning, Call(client, ToolCatalog.GetToday).Error!.Code);
            app.Settings.Update(s => s with { ClaudeAccess = true });
            Assert.True(bridge.IsRunning);
            Assert.True(Call(client, ToolCatalog.GetTask, $$"""{"id":{{id}}}""").Ok);
            Assert.Equal(1, changes);
        }
        finally
        {
            access.Stop();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        Assert.False(bridge.IsRunning);
    }

    [AvaloniaFact]
    public void Exit_answers_a_request_queued_on_the_ui_thread_without_waiting_for_it()
    {
        using var folder = new TempFolder();
        var bridge = new ClaudeBridge();
        using var h = LifecycleHarness.OnServices(folder, Clock(), PlatformServices.NoOp() with { Bridge = bridge },
            settings: s => s with { ClaudeAccess = true, ClaudeAccessLevel = ClaudeAccessLevel.ReadWrite });
        Assert.True(bridge.IsRunning); // opened after the first frame
        var client = new BridgeClient(folder.Path);
        var args = JsonSerializer.Deserialize("""{"title":"Too late"}""", BridgeJsonContext.Default.DictionaryStringJsonElement);

        // The request reaches the UI thread's queue and waits there: this thread is not pumping.
        h.Settle();
        var call = Task.Run(() => client.CallAsync(ToolCatalog.CreateTask, args, CancellationToken.None));
        var waited = Stopwatch.StartNew();
        while (!Dispatcher.UIThread.HasJobsWithPriority(DispatcherPriority.Normal) && !call.IsCompleted)
        {
            Assert.True(waited.Elapsed < UiPump.Long, "The request never reached the UI thread.");
            Thread.Sleep(5);
        }

        Assert.False(call.IsCompleted, call.IsCompleted ? call.Result.Error?.Message : null);

        // Desktop Exit (App.DrainOnExit → AppLifecycle.DisposeAsync): the bridge stops first, the database closes, and
        // nothing waits for the queued request.
        var exit = Stopwatch.StartNew();
        Assert.True(global::Doba.App.App.DrainOnExit(h.Lifecycle, global::Doba.App.App.ExitDrainTimeout, h.App.LogProblem));
        exit.Stop();
        Assert.True(exit.Elapsed < TimeSpan.FromSeconds(1), $"Exit took {exit.Elapsed.TotalMilliseconds:F0} ms");
        Assert.False(bridge.IsRunning);

        // The client is told: "closing" from the gate, or the connection closed under it; the store never saw it.
        var response = UiPump.Until(call);
        Assert.False(response.Ok);
        Assert.True(response.Error!.Code == BridgeErrorCodes.Closing || response.Error.Message == BridgeClient.ClosedMessage,
            response.Error.Code + ": " + response.Error.Message);
        Assert.DoesNotContain(h.App.Store.All, t => t.Title == "Too late");
        Assert.DoesNotContain("Too late", folder.TitlesOnDisk());
    }
}

/// <summary>Waits for a task on the headless UI thread while running its dispatcher jobs (a bridge request needs them).</summary>
internal static class UiPump
{
    /// <summary>Generous for a loaded machine; a passing wait returns as soon as the task completes.</summary>
    public static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    public static void Until(Task task, TimeSpan? timeout = null)
    {
        Wait(task, timeout);
        task.GetAwaiter().GetResult();
    }

    public static T Until<T>(Task<T> task, TimeSpan? timeout = null)
    {
        Wait(task, timeout);
        return task.GetAwaiter().GetResult();
    }

    private static void Wait(Task task, TimeSpan? timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(watch.Elapsed < (timeout ?? Long), "Timed out waiting on the UI thread.");
            ((IAsyncResult)task).AsyncWaitHandle.WaitOne(5);
        }
    }
}
