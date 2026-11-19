using System.Text.Json;
using Doba.Core.Model;
using Doba.Core.Store;
using Doba.Storage;

namespace Doba.Bridge.Tests;

/// <summary>
/// Doba.Mcp.exe as Claude starts it (a child process over stdio, <c>--data &lt;temp folder&gt;</c>) against a
/// <see cref="TestDoba"/> on the same folder. Run against the published NativeAOT exe with <c>DOBA_MCP_EXE</c>.
/// </summary>
public sealed class McpEndToEndTests
{
    private static readonly DateOnly Today = TestDoba.Today;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Log(string text) => TestContext.Current.TestOutputHelper?.WriteLine(text);

    private static TaskItem T(long id, string title = "Task", DateOnly? date = null, string notes = "", string? tag = null,
        TimeOnly? time = null) =>
        new(id, title, notes, date, null, time, 0, Priority.None, tag, false);

    /// <summary>A started, initialised session (2025-11-25 or 2026-07-28).</summary>
    private static async Task<McpProcess> SessionAsync(string folder, bool modern = false)
    {
        var mcp = McpProcess.Start(folder);
        if (modern)
        {
            await mcp.DiscoverAsync();
        }
        else
        {
            await mcp.InitializeAsync();
        }

        return mcp;
    }

    private static void AssertOk(ToolResult r)
    {
        Assert.False(r.IsError, r.Text);
        Assert.Equal("2026-11-19", r.Json.GetProperty("today").GetString());
        using var text = JsonDocument.Parse(r.Text); // the text block is the same JSON (escaped differently)
        Assert.True(JsonElement.DeepEquals(r.Json, text.RootElement), r.Text);
    }

    private static void AssertError(ToolResult r, string expected)
    {
        Assert.True(r.IsError, "expected an error, got " + r.Text);
        Assert.Null(r.Structured);
        Assert.Contains(expected, r.Text, StringComparison.Ordinal);
    }

    // ---------- protocol ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_protocol_eras_give_server_info_instructions_and_the_eleven_tools(bool modern)
    {
        using var doba = new TestDoba();
        await using var mcp = McpProcess.Start(doba.Folder);
        var start = modern ? await mcp.DiscoverAsync() : await mcp.InitializeAsync();

        Assert.Equal(ToolCatalog.ServerInstructions, start.GetProperty("instructions").GetString());
        var info = modern
            ? start.GetProperty("_meta").GetProperty("io.modelcontextprotocol/serverInfo")
            : start.GetProperty("serverInfo");
        Assert.Equal("doba", info.GetProperty("name").GetString());
        Assert.Equal("Doba", info.GetProperty("title").GetString());
        Assert.False(string.IsNullOrEmpty(info.GetProperty("version").GetString()));
        if (modern)
        {
            Assert.Contains(McpProcess.ModernVersion, start.GetProperty("supportedVersions").EnumerateArray().Select(v => v.GetString()));
        }
        else
        {
            Assert.Equal(McpProcess.LegacyVersion, start.GetProperty("protocolVersion").GetString());
        }

        var tools = (await mcp.ListToolsAsync()).GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(11, tools.Count);
        Assert.Equal(ToolCatalog.All.Select(t => t.Name), tools.Select(t => t.GetProperty("name").GetString()));
        foreach (var tool in tools)
        {
            var definition = ToolCatalog.Find(tool.GetProperty("name").GetString()!)!;
            Assert.Equal(definition.Title, tool.GetProperty("title").GetString());
            Assert.Equal(definition.ReadOnly, tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
            Assert.False(tool.GetProperty("annotations").GetProperty("openWorldHint").GetBoolean());
            Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());
        }

        // A tool call in the same era (with _meta on every request for 2026-07-28).
        AssertOk(await mcp.CallToolAsync(ToolCatalog.GetToday));
        Assert.Equal(1, doba.Requests);
    }

    [Fact]
    public async Task An_unknown_tool_is_a_protocol_error_and_the_session_goes_on()
    {
        using var doba = new TestDoba();
        await using var mcp = await SessionAsync(doba.Folder);
        var response = await mcp.RequestAsync("tools/call", new System.Text.Json.Nodes.JsonObject { ["name"] = "due_date", ["arguments"] = new System.Text.Json.Nodes.JsonObject() });
        Assert.Equal(-32602, response.GetProperty("error").GetProperty("code").GetInt32());
        AssertOk(await mcp.CallToolAsync(ToolCatalog.GetToday));
        Assert.Equal(1, doba.Requests);
    }

    // ---------- every tool ----------

    /// <summary>tool, good arguments, bad arguments (and the text the error must contain).</summary>
    public static TheoryData<string, string, string, string> Tools => new()
    {
        { ToolCatalog.GetToday, "{}", """{"date":"2026-11-19"}""", "takes no arguments" },
        { ToolCatalog.ListTasks, """{"from":"2026-11-19","to":"2026-11-25"}""", """{"from":"19.11.2026"}""", "YYYY-MM-DD" },
        { ToolCatalog.ListInbox, """{"include_done":true}""", """{"limit":0}""", "'limit' must be from 1 to 500" },
        { ToolCatalog.SearchTasks, """{"query":"bread"}""", """{"query":5}""", "'query' must be a string" },
        { ToolCatalog.ListTags, "{}", """{"all":true}""", "takes no arguments" },
        { ToolCatalog.GetTask, """{"id":1}""", """{"id":"one"}""", "'id' must be a whole number" },
        { ToolCatalog.CreateTask, """{"title":"Milk","date":"2026-11-20","time":"08:15"}""", """{"title":"x","date":"2026-11-20","days":3,"time":"08:15"}""", "single-day" },
        { ToolCatalog.UpdateTask, """{"id":1,"title":"Buy bread and milk"}""", """{"id":1,"duration_minutes":2000}""", "'duration_minutes' must be from 0 to 1440" },
        { ToolCatalog.SetTaskDone, """{"id":1,"done":true}""", """{"id":1,"done":"yes"}""", "'done' must be true or false" },
        { ToolCatalog.MoveTask, """{"id":1,"date":"inbox"}""", """{"id":1,"date":"next monday"}""", "\"inbox\"" },
        { ToolCatalog.DeleteTask, """{"id":1}""", """{"id":-4}""", "'id' must be at least 1" },
    };

    [Theory]
    [MemberData(nameof(Tools))]
    public async Task Every_tool_succeeds_refuses_bad_and_unknown_arguments_and_missing_ids(string tool, string good, string bad, string badText)
    {
        using var doba = new TestDoba(tasks: [T(1, "Buy bread", Today, notes: "rye", tag: "home")]);
        await using var mcp = await SessionAsync(doba.Folder, modern: tool.Length % 2 == 0); // both eras across the set

        // Invalid and unknown arguments first, while task 1 is still there: nothing may change.
        AssertError(await mcp.CallToolAsync(tool, bad), badText);
        var unknown = await mcp.CallToolAsync(tool, """{"id":1,"due_date":"2026-11-20"}""");
        AssertError(unknown, "'due_date'");
        if (ToolCatalog.Find(tool)!.InputSchemaJson.Contains("\"id\"", StringComparison.Ordinal))
        {
            var missing = good.Replace("\"id\":1", "\"id\":999", StringComparison.Ordinal);
            AssertError(await mcp.CallToolAsync(tool, missing), "No task with id 999");
        }

        Assert.Equal(0, doba.ChangedEvents);
        Assert.Equal(T(1, "Buy bread", Today, notes: "rye", tag: "home"), doba.Read(s => s.Get(1)));

        var r = await mcp.CallToolAsync(tool, good);
        AssertOk(r);
        Assert.Equal(ToolCatalog.Find(tool)!.ReadOnly ? 0 : 1, doba.ChangedEvents);
        switch (tool)
        {
            case ToolCatalog.GetToday:
                Assert.Equal(1, r.Json.GetProperty("anytime")[0].GetProperty("id").GetInt64());
                break;
            case ToolCatalog.ListTasks or ToolCatalog.SearchTasks:
                Assert.Equal("rye", r.Json.GetProperty("tasks")[0].GetProperty("notes").GetString());
                break;
            case ToolCatalog.ListInbox:
                Assert.Equal(0, r.Json.GetProperty("total").GetInt32());
                break;
            case ToolCatalog.ListTags:
                Assert.Equal("home", r.Json.GetProperty("tags")[1].GetProperty("name").GetString());
                break;
            case ToolCatalog.GetTask:
                Assert.Equal("Buy bread", r.Json.GetProperty("task").GetProperty("title").GetString());
                break;
            case ToolCatalog.CreateTask:
                Assert.Equal(new TimeOnly(8, 15), doba.Read(s => s.Get(2)!.Time));
                break;
            case ToolCatalog.UpdateTask:
                Assert.Equal(T(1, "Buy bread and milk", Today, notes: "rye", tag: "home"), doba.Read(s => s.Get(1)));
                break;
            case ToolCatalog.SetTaskDone:
                Assert.True(doba.Read(s => s.Get(1)!.Done));
                break;
            case ToolCatalog.MoveTask:
                Assert.Null(doba.Read(s => s.Get(1)!.Date));
                break;
            case ToolCatalog.DeleteTask:
                Assert.Null(doba.Read(s => s.Get(1)));
                break;
        }
    }

    [Fact]
    public async Task Update_with_null_notes_is_an_error_and_the_task_is_unchanged()
    {
        using var doba = new TestDoba(tasks: [T(1, "Old", Today, notes: "keep")]);
        await using var mcp = await SessionAsync(doba.Folder);
        AssertError(await mcp.CallToolAsync(ToolCatalog.UpdateTask, """{"id":1,"title":"New","notes":null}"""), "'notes' must not be null");
        Assert.Equal(T(1, "Old", Today, notes: "keep"), doba.Read(s => s.Get(1)));
        Assert.Equal(0, doba.ChangedEvents);
    }

    [Fact]
    public async Task An_unknown_tag_is_an_error_listing_the_tags()
    {
        using var doba = new TestDoba(tags: ["work", "home", "deep-work"]);
        await using var mcp = await SessionAsync(doba.Folder, modern: true);
        AssertError(await mcp.CallToolAsync(ToolCatalog.CreateTask, """{"title":"x","tag":"errands"}"""), "Existing tags: work, home, deep-work");
        Assert.Equal(0, doba.Read(s => s.Count));
    }

    // ---------- Doba not there ----------

    [Fact]
    public async Task Access_off_is_an_error_pointing_to_settings()
    {
        using var doba = new TestDoba(claimMutex: true, startHost: false);
        await using var mcp = await SessionAsync(doba.Folder);
        var r = await mcp.CallToolAsync(ToolCatalog.GetToday);
        AssertError(r, "Claude access is off");
        Assert.Contains("Settings → Data → Claude access", r.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_running_is_an_error_naming_the_folder()
    {
        using var folder = new TempDataFolder();
        await using var mcp = await SessionAsync(folder.Path, modern: true);
        var r = await mcp.CallToolAsync(ToolCatalog.ListInbox);
        AssertError(r, "Doba is not running");
        Assert.Contains(folder.Path, r.Text, StringComparison.Ordinal);
    }

    // ---------- writes ----------

    [Fact]
    public async Task Read_level_refuses_every_change_and_nothing_is_written()
    {
        using var doba = new TestDoba(tasks: [T(1, "Keep", Today)]);
        doba.Settings = doba.Settings with { ClaudeAccessLevel = ClaudeAccessLevel.Read };
        await using var mcp = await SessionAsync(doba.Folder);
        string[][] writes =
        [
            [ToolCatalog.CreateTask, """{"title":"x"}"""],
            [ToolCatalog.UpdateTask, """{"id":1,"title":"x"}"""],
            [ToolCatalog.SetTaskDone, """{"id":1,"done":true}"""],
            [ToolCatalog.MoveTask, """{"id":1,"date":"inbox"}"""],
            [ToolCatalog.DeleteTask, """{"id":1}"""],
        ];
        foreach (var write in writes)
        {
            AssertError(await mcp.CallToolAsync(write[0], write[1]), "Doba lets Claude read tasks only");
        }

        AssertOk(await mcp.CallToolAsync(ToolCatalog.GetTask, """{"id":1}"""));
        Assert.Equal(0, doba.ChangedEvents);
        Assert.Equal(0, doba.Persistence.Upserts + doba.Persistence.Deletes);
        Assert.Equal(T(1, "Keep", Today), doba.Read(s => s.Get(1)));
    }

    [Fact]
    public async Task One_change_is_one_changed_event_and_one_upsert()
    {
        using var doba = new TestDoba(tasks: [T(1, "Write", Today)]);
        await using var mcp = await SessionAsync(doba.Folder, modern: true);
        AssertOk(await mcp.CallToolAsync(ToolCatalog.UpdateTask, """{"id":1,"title":"Write more","priority":"high","date":"2026-11-21","clear":["notes"]}"""));
        Assert.Equal(1, doba.ChangedEvents);
        Assert.Equal(1, doba.Persistence.Upserts);

        AssertOk(await mcp.CallToolAsync(ToolCatalog.CreateTask, """{"title":"New"}"""));
        Assert.Equal(2, doba.ChangedEvents);
        Assert.Equal(2, doba.Persistence.Upserts);
    }

    [Fact]
    public async Task A_task_created_through_mcp_is_on_disk()
    {
        using var folder = new TempDataFolder();
        var location = new DataLocation(folder.Path);
        await using (var db = DobaDatabase.Open(location))
        {
            var loaded = db.Load();
            using var doba = new TestDoba(tasks: loaded.Tasks, tags: ["home"], persistence: db.Tasks, folder: folder.Path);
            await using var mcp = await SessionAsync(folder.Path);
            var r = await mcp.CallToolAsync(ToolCatalog.CreateTask, """{"title":"Купити хліб","notes":"житній","date":"2026-11-20","time":"18:00","duration_minutes":15,"priority":"medium","tag":"home"}""");
            AssertOk(r);
            Assert.Contains("Купити хліб", r.Text, StringComparison.Ordinal);
            await db.Tasks.FlushAsync(Ct);
        }

        await using (var reopened = DobaDatabase.Open(location))
        {
            var task = Assert.Single(reopened.Load().Tasks);
            Assert.Equal(new TaskItem(1, "Купити хліб", "житній", Today.AddDays(1), null, new TimeOnly(18, 0), 15, Priority.Medium, "home", false), task);
        }
    }

    // ---------- concurrency ----------

    [Fact]
    public async Task Two_servers_and_parallel_calls_get_their_own_answers_and_lose_nothing()
    {
        using var doba = new TestDoba(tasks: [.. Enumerable.Range(1, 20).Select(i => T(i, "Seed " + i, Today))]);
        await using var a = await SessionAsync(doba.Folder);
        await using var b = await SessionAsync(doba.Folder, modern: true);

        Task<ToolResult> Create(McpProcess mcp, string title) => mcp.CallToolAsync(ToolCatalog.CreateTask, $$"""{"title":"{{title}}"}""");
        var creates = Enumerable.Range(0, 50).SelectMany(i => new[] { ("a" + i, Create(a, "a" + i)), ("b" + i, Create(b, "b" + i)) }).ToList();
        var reads = Enumerable.Range(1, 20).Select(i => (i, a.CallToolAsync(ToolCatalog.GetTask, $$"""{"id":{{i}}}"""))).ToList();

        await Task.WhenAll(creates.Select(c => c.Item2).Concat(reads.Select(r => r.Item2))).WaitAsync(TimeSpan.FromMinutes(2), Ct);

        var ids = new List<long>();
        foreach (var (title, call) in creates)
        {
            var r = await call;
            AssertOk(r);
            var task = r.Json.GetProperty("task");
            Assert.Equal(title, task.GetProperty("title").GetString());
            ids.Add(task.GetProperty("id").GetInt64());
        }

        foreach (var (id, call) in reads)
        {
            var r = await call;
            AssertOk(r);
            Assert.Equal("Seed " + id, r.Json.GetProperty("task").GetProperty("title").GetString());
        }

        Assert.Equal(100, ids.Distinct().Count());
        Assert.Equal(120, doba.Read(s => s.Count));
        Assert.Equal(100, doba.ChangedEvents);
        Assert.Equal(100, doba.Persistence.Upserts);
        foreach (var (title, _) in creates)
        {
            Assert.Single(doba.Read(s => s.All.Where(t => t.Title == title).ToList()));
        }
    }

    // ---------- interruptions ----------

    [Fact]
    public async Task Host_stop_mid_session_is_a_clear_error_and_a_new_start_works()
    {
        using var doba = new TestDoba();
        await using var mcp = await SessionAsync(doba.Folder);
        AssertOk(await mcp.CallToolAsync(ToolCatalog.GetToday));

        doba.Host.Stop();
        AssertError(await mcp.CallToolAsync(ToolCatalog.GetToday), "Claude access is off");

        doba.Host.Start();
        AssertOk(await mcp.CallToolAsync(ToolCatalog.GetToday));
    }

    [Fact]
    public async Task Host_stop_during_a_call_answers_the_call()
    {
        using var doba = new TestDoba();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        doba.BeforeExecute = () => release.Task;
        await using var mcp = await SessionAsync(doba.Folder);
        var call = mcp.CallToolAsync(ToolCatalog.CreateTask, """{"title":"x"}""");
        await Wait.UntilAsync(() => doba.Requests == 1, "the call to reach Doba");
        doba.Host.Stop();
        AssertError(await call, "Doba closed the connection");
        release.SetResult();
    }

    [Fact]
    public async Task Killing_the_server_mid_request_leaves_the_host_serving()
    {
        using var doba = new TestDoba();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        doba.BeforeExecute = () => release.Task;
        await using (var doomed = await SessionAsync(doba.Folder))
        {
            var orphan = doomed.CallToolAsync(ToolCatalog.CreateTask, """{"title":"orphan"}""");
            await Wait.UntilAsync(() => doba.Requests == 1, "the call to reach Doba");
            doomed.Process.Kill(entireProcessTree: true);
            await doomed.Process.WaitForExitAsync(Ct);
            await Assert.ThrowsAnyAsync<IOException>(() => orphan);
        }

        release.SetResult(); // Doba finishes the request; its answer has nowhere to go
        doba.BeforeExecute = null;

        await using var next = await SessionAsync(doba.Folder);
        var calls = Enumerable.Range(0, BridgeHost.MaxConnections + 2)
            .Select(i => next.CallToolAsync(ToolCatalog.CreateTask, $$"""{"title":"after {{i}}"}""")).ToList();
        foreach (var r in await Task.WhenAll(calls))
        {
            AssertOk(r);
        }

        Assert.Equal(BridgeHost.MaxConnections + 3, doba.Read(s => s.Count)); // the orphan was applied too
    }

    [Fact]
    public async Task Stdin_eof_ends_the_server_at_once()
    {
        using var doba = new TestDoba();
        var mcp = await SessionAsync(doba.Folder);
        await using (mcp)
        {
            AssertOk(await mcp.CallToolAsync(ToolCatalog.GetToday));
            var took = await mcp.CloseInputAndWaitAsync(TimeSpan.FromSeconds(10));
            Log($"exit after stdin EOF: {took.TotalMilliseconds:0} ms ({TestPaths.McpExe})");
            Assert.True(took < TimeSpan.FromMilliseconds(1000), $"took {took.TotalMilliseconds:0} ms");
            Assert.Equal(0, mcp.Process.ExitCode);
        }
    }

    [Fact]
    public async Task Stdin_eof_during_a_call_still_ends_the_server_at_once()
    {
        using var doba = new TestDoba();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        doba.BeforeExecute = () => release.Task;
        var mcp = await SessionAsync(doba.Folder);
        await using (mcp)
        {
            _ = mcp.CallToolAsync(ToolCatalog.GetToday).ContinueWith(t => t.Exception, Ct, TaskContinuationOptions.None, TaskScheduler.Default);
            await Wait.UntilAsync(() => doba.Requests == 1, "the call to reach Doba");
            var took = await mcp.CloseInputAndWaitAsync(TimeSpan.FromSeconds(10));
            Log($"exit after stdin EOF with a call in flight: {took.TotalMilliseconds:0} ms");
            Assert.True(took < TimeSpan.FromMilliseconds(1000), $"took {took.TotalMilliseconds:0} ms");
        }

        release.SetResult();
    }

    // ---------- stdout hygiene ----------

    public static TheoryData<string[]> BadArguments => new()
    {
        new[] { "--data" },
        new[] { "--folder", "x" },
        new[] { "--data", "" },
        new[] { "--data", "a", "b" },
        new[] { "--demo" },
    };

    [Theory]
    [MemberData(nameof(BadArguments))]
    public async Task Bad_arguments_print_usage_on_stderr_nothing_on_stdout_and_exit_2(string[] args)
    {
        await using var mcp = McpProcess.Start(args);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        await mcp.Process.WaitForExitAsync(cts.Token);
        await Wait.UntilAsync(() => mcp.Stderr.Length > 0, "usage on stderr");
        Assert.Equal(2, mcp.Process.ExitCode);
        Assert.Empty(mcp.Lines);
        Assert.Contains("Usage: Doba.Mcp", mcp.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_writes_only_json_rpc_lines_on_stdout()
    {
        using var doba = new TestDoba(tasks: [T(1, "Line\nbreak \"quoted\" Ї", Today, notes: "a\r\nb")]);
        var mcp = await SessionAsync(doba.Folder);
        await using (mcp)
        {
            await mcp.ListToolsAsync();
            AssertOk(await mcp.CallToolAsync(ToolCatalog.GetTask, """{"id":1}"""));
            AssertError(await mcp.CallToolAsync(ToolCatalog.GetTask, """{"id":2}"""), "No task");
            await mcp.RequestAsync("tools/call", new System.Text.Json.Nodes.JsonObject { ["name"] = "nope" });
            await mcp.RequestAsync("ping");
            await mcp.CloseInputAndWaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.True(mcp.Lines.Count >= 6, string.Join("\n", mcp.Lines));
        foreach (var line in mcp.Lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        }
    }
}
