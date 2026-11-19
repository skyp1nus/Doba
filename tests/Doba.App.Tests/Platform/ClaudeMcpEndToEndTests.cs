using Avalonia.Headless.XUnit;
using Doba.App.Platform;
using Doba.App.Screens.Today;
using Doba.App.Tests.Lifecycle;
using Doba.Bridge;
using Doba.Bridge.Tests;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Time;

namespace Doba.App.Tests.Platform;

/// <summary>
/// The whole path as Claude uses it: the real Doba.Mcp.exe (a child process over stdio, <c>--data &lt;temp folder&gt;</c>;
/// <c>DOBA_MCP_EXE</c> picks the published NativeAOT exe) → pipe → the app's <see cref="ClaudeBridge"/> (opened by the
/// lifecycle because Claude access is on) → the store on the UI thread → the Today screen. Everything but the bridge is
/// <see cref="PlatformServices.NoOp"/>.
/// </summary>
public class ClaudeMcpEndToEndTests
{
    private static FixedTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));

    private static ToolResult CallTool(McpProcess mcp, string tool, string argumentsJson = "{}") =>
        UiPump.Until(Task.Run(() => mcp.CallToolAsync(tool, argumentsJson)));

    [AvaloniaFact]
    public void A_task_created_through_mcp_lands_in_the_store_and_on_the_today_screen()
    {
        using var folder = new TempFolder();
        var bridge = new ClaudeBridge();
        using var h = LifecycleHarness.OnServices(folder, Clock(), PlatformServices.NoOp() with { Bridge = bridge },
            settings: s => s with { ClaudeAccess = true, ClaudeAccessLevel = ClaudeAccessLevel.ReadWrite });
        Assert.True(bridge.IsRunning);
        h.Vm.GoSection(AppView.Today);
        h.Settle();
        var today = Assert.IsType<TodayViewModel>(h.Vm.Current);
        var refreshes = today.Refreshes;
        var changes = 0;
        h.App.Store.Changed += (_, _) => changes++;
        var iso = DateMath.Iso(h.App.DayClock.Today);

        var mcp = UiPump.Until(Task.Run(async () =>
        {
            var process = McpProcess.Start(folder.Path);
            await process.InitializeAsync();
            return process;
        }));
        try
        {
            var created = CallTool(mcp, ToolCatalog.CreateTask, $$"""{"title":"Call the plumber","date":"{{iso}}","time":"10:30","duration_minutes":30}""");
            Assert.False(created.IsError, created.Text);
            var id = created.Json.GetProperty("task").GetProperty("id").GetInt64();

            // One store change, applied on the UI thread; the active Today screen re-projected and shows the row.
            Assert.Equal(1, changes);
            var task = h.App.Store.Get(id);
            Assert.NotNull(task);
            Assert.Equal(("Call the plumber", h.App.DayClock.Today, new TimeOnly(10, 30), 30), (task.Title, task.Date, task.Time, task.DurationMinutes));
            Assert.True(today.Refreshes > refreshes);
            Assert.Contains(today.Groups.SelectMany(g => g.Items), r => r.Id == id && r.Title == "Call the plumber");
            Assert.Contains(today.Schedule.Blocks, b => b.Id == id);

            // Read back through MCP, then done from Claude: the screen follows again.
            var read = CallTool(mcp, ToolCatalog.GetToday);
            Assert.False(read.IsError, read.Text);
            Assert.Contains("Call the plumber", read.Text, StringComparison.Ordinal);
            var done = CallTool(mcp, ToolCatalog.SetTaskDone, $$"""{"id":{{id}},"done":true}""");
            Assert.False(done.IsError, done.Text);
            Assert.Equal(2, changes);
            Assert.True(h.App.Store.Get(id)!.Done);
            Assert.Contains(today.Groups.Single(g => g.Key == "done").Items, r => r.Id == id);

            // Access off in Settings while Doba runs (its single-instance mutex, which the harness does not take, is
            // held here): the next call is refused with the Settings hint and nothing changes.
            h.App.Settings.Update(s => s with { ClaudeAccess = false });
            Assert.False(bridge.IsRunning);
            using (new Mutex(initiallyOwned: false, BridgeNames.MutexName(folder.Path)))
            {
                var refused = CallTool(mcp, ToolCatalog.DeleteTask, $$"""{"id":{{id}}}""");
                Assert.True(refused.IsError);
                Assert.Equal(BridgeClient.AccessOffMessage, refused.Text);
            }

            Assert.NotNull(h.App.Store.Get(id));
            Assert.Equal(2, changes);
        }
        finally
        {
            UiPump.Until(Task.Run(async () => await mcp.DisposeAsync()));
        }
    }
}
