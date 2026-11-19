using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Doba.Bridge.Wire;

namespace Doba.Bridge.Tests;

/// <summary>BridgeHost and BridgeClient in-process, over a real pipe on a temp folder's name.</summary>
public sealed class BridgeHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<BridgeResponse> Call(string folder, string tool, string argsJson = "{}", CancellationToken? ct = null)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        return new BridgeClient(folder).CallAsync(tool, args, ct ?? Ct);
    }

    private static async Task<NamedPipeClientStream> ConnectRawAsync(string folder)
    {
        var client = new NamedPipeClientStream(".", BridgeNames.PipeName(folder), PipeDirection.InOut,
            PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
        await client.ConnectAsync(TimeSpan.FromSeconds(10), Ct);
        return client;
    }

    private static async Task<BridgeResponse> ReadResponseAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), leaveOpen: true);
        var line = await reader.ReadLineAsync(Ct);
        Assert.NotNull(line);
        return JsonSerializer.Deserialize(line, BridgeJsonContext.Default.BridgeResponse)!;
    }

    [Fact]
    public async Task A_call_goes_to_doba_and_back()
    {
        using var doba = new TestDoba();
        var r = await Call(doba.Folder, ToolCatalog.CreateTask, """{"title":"Купити хліб","date":"2026-11-20"}""");
        Assert.True(r.Ok, r.Error?.Message);
        Assert.Equal("Купити хліб", r.Result!.Value.GetProperty("task").GetProperty("title").GetString());
        Assert.Equal("Купити хліб", doba.Read(s => s.Get(1)!.Title));
        Assert.True(doba.Host.IsListening);
    }

    [Fact]
    public async Task Not_running_names_the_data_folder_and_answers_fast()
    {
        using var folder = new TempDataFolder();
        var watch = Stopwatch.StartNew();
        var r = await Call(folder.Path, ToolCatalog.GetToday);
        Assert.Equal(BridgeErrorCodes.NotRunning, r.Error!.Code);
        Assert.Contains(folder.Path, r.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Doba is not running", r.Error.Message, StringComparison.Ordinal);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), "took " + watch.Elapsed);
    }

    [Fact]
    public async Task Running_without_a_pipe_is_access_off_after_a_short_wait()
    {
        using var doba = new TestDoba(claimMutex: true, startHost: false);
        var watch = Stopwatch.StartNew();
        var r = await Call(doba.Folder, ToolCatalog.GetToday);
        Assert.Equal(BridgeErrorCodes.AccessOff, r.Error!.Code);
        Assert.Contains("Settings → Data → Claude access", r.Error.Message, StringComparison.Ordinal);
        Assert.True(watch.Elapsed >= BridgeClient.StartingWait, "gave up after " + watch.Elapsed);
        Assert.Equal(0, doba.Requests);
    }

    [Fact]
    public async Task A_pipe_that_appears_within_the_wait_is_used()
    {
        using var doba = new TestDoba(claimMutex: true, startHost: false);
        var call = Call(doba.Folder, ToolCatalog.GetToday);
        await Task.Delay(TimeSpan.FromMilliseconds(300), Ct); // Doba "still starting"
        doba.Host.Start();
        var r = await call;
        Assert.True(r.Ok, r.Error?.Message);
    }

    [Fact]
    public async Task Stop_closes_the_pipe_and_start_opens_it_again()
    {
        using var doba = new TestDoba();
        Assert.True((await Call(doba.Folder, ToolCatalog.GetToday)).Ok);

        doba.Host.Stop();
        doba.Host.Stop(); // idempotent
        Assert.False(doba.Host.IsListening);
        var off = await Call(doba.Folder, ToolCatalog.GetToday);
        Assert.Equal(BridgeErrorCodes.AccessOff, off.Error!.Code);

        doba.Host.Start();
        doba.Host.Start(); // no-op while listening
        Assert.True(doba.Host.IsListening);
        Assert.True((await Call(doba.Folder, ToolCatalog.GetToday)).Ok);
    }

    [Fact]
    public async Task Stop_during_a_queued_request_does_not_wait_for_it_and_the_client_hears_it()
    {
        using var doba = new TestDoba();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        doba.BeforeExecute = () => release.Task;
        var call = Call(doba.Folder, ToolCatalog.CreateTask, """{"title":"x"}""");
        await Wait.UntilAsync(() => doba.Requests == 1, "the request to reach Doba");

        var watch = Stopwatch.StartNew();
        doba.Host.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), "Stop blocked for " + watch.Elapsed);

        var r = await call.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(BridgeErrorCodes.Internal, r.Error!.Code);
        Assert.Contains("Doba closed the connection", r.Error.Message, StringComparison.Ordinal);
        Assert.Contains("get_task / search_tasks", r.Error.Message, StringComparison.Ordinal);

        release.SetResult(); // the queued request finishes after Stop: nothing breaks
        doba.BeforeExecute = null;
        doba.Host.Start();
        Assert.True((await Call(doba.Folder, ToolCatalog.GetToday)).Ok);
    }

    [Fact]
    public void Dispose_stops_and_start_after_dispose_throws()
    {
        var doba = new TestDoba();
        doba.Host.Dispose();
        Assert.False(doba.Host.IsListening);
        Assert.Throws<ObjectDisposedException>(doba.Host.Start);
        doba.Dispose();
    }

    [Fact]
    public void A_second_host_on_the_same_name_cannot_start()
    {
        using var doba = new TestDoba();
        using var other = new BridgeHost(BridgeNames.PipeName(doba.Folder), (_, _) => Task.FromResult(BridgeResponse.Failure("x", "x")));
        // FirstPipeInstance: never joins a pipe someone else created (Windows answers "access denied").
        var error = Record.Exception(other.Start);
        Assert.True(error is UnauthorizedAccessException or IOException, error?.ToString() ?? "no exception");
        Assert.False(other.IsListening);
    }

    [Fact]
    public async Task At_most_eight_requests_at_once_and_the_rest_wait_their_turn()
    {
        using var doba = new TestDoba();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        doba.BeforeExecute = () => release.Task;
        var calls = Enumerable.Range(0, 12).Select(i => Call(doba.Folder, ToolCatalog.CreateTask, $$"""{"title":"t{{i}}"}""")).ToList();

        await Wait.UntilAsync(() => doba.Requests == BridgeHost.MaxConnections, "eight requests in Doba");
        await Task.Delay(TimeSpan.FromMilliseconds(300), Ct); // nothing more gets in while eight are served
        Assert.Equal(BridgeHost.MaxConnections, doba.Requests);

        release.SetResult();
        var results = await Task.WhenAll(calls).WaitAsync(Wait.Long, Ct);
        Assert.All(results, r => Assert.True(r.Ok, r.Error?.Message));
        Assert.Equal(12, results.Select(r => r.Result!.Value.GetProperty("task").GetProperty("id").GetInt64()).Distinct().Count());
        Assert.Equal(12, doba.Read(s => s.Count));
    }

    [Fact]
    public async Task No_answer_in_time_is_a_timeout_that_warns_about_retrying()
    {
        using var doba = new TestDoba();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        doba.BeforeExecute = () => release.Task;
        var watch = Stopwatch.StartNew();
        var r = await Call(doba.Folder, ToolCatalog.CreateTask, """{"title":"slow"}""");
        Assert.Equal(BridgeErrorCodes.Timeout, r.Error!.Code);
        Assert.Equal(BridgeClient.TimeoutMessage, r.Error.Message);
        Assert.Contains("may have been applied", r.Error.Message, StringComparison.Ordinal);
        Assert.True(watch.Elapsed >= BridgeClient.RequestTimeout - TimeSpan.FromMilliseconds(100));
        release.SetResult();
    }

    [Fact]
    public async Task Cancelling_the_call_throws_operation_cancelled()
    {
        using var doba = new TestDoba();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        doba.BeforeExecute = () => release.Task;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var call = Call(doba.Folder, ToolCatalog.GetToday, ct: cts.Token);
        await Wait.UntilAsync(() => doba.Requests == 1, "the request to reach Doba");
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        release.SetResult();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Call(doba.Folder, ToolCatalog.GetToday, ct: cancelled.Token));
    }

    [Fact]
    public async Task A_handler_that_throws_is_an_internal_error_and_the_host_goes_on()
    {
        using var folder = new TempDataFolder();
        var calls = 0;
        using var host = new BridgeHost(BridgeNames.PipeName(folder.Path), (request, _) =>
            Interlocked.Increment(ref calls) == 1
                ? throw new InvalidOperationException("boom")
                : Task.FromResult(BridgeResponse.Success(JsonElement.Parse("""{"today":"x"}"""))));
        host.Start();
        var first = await Call(folder.Path, ToolCatalog.GetToday);
        Assert.Equal(BridgeErrorCodes.Internal, first.Error!.Code);
        Assert.Contains("boom", first.Error.Message, StringComparison.Ordinal);
        Assert.True((await Call(folder.Path, ToolCatalog.GetToday)).Ok);
    }

    [Fact]
    public async Task Bad_json_is_invalid_args()
    {
        using var doba = new TestDoba();
        await using var pipe = await ConnectRawAsync(doba.Folder);
        await pipe.WriteAsync("{not json\n"u8.ToArray(), Ct);
        var r = await ReadResponseAsync(pipe);
        Assert.Equal(BridgeErrorCodes.InvalidArgs, r.Error!.Code);
        Assert.Equal(0, doba.Requests);
    }

    [Fact]
    public async Task A_request_over_one_megabyte_is_refused_and_the_host_goes_on()
    {
        using var doba = new TestDoba();
        await using (var pipe = await ConnectRawAsync(doba.Folder))
        {
            var huge = new byte[BridgeHost.MaxMessageBytes + 4096];
            huge.AsSpan().Fill((byte)'a');
            huge[^1] = (byte)'\n';
            var write = Task.Run(async () =>
            {
                try
                {
                    await pipe.WriteAsync(huge, Ct);
                }
                catch (IOException)
                {
                    // the host may close before taking all of it
                }
            }, Ct);
            var r = await ReadResponseAsync(pipe);
            Assert.Equal(BridgeErrorCodes.InvalidArgs, r.Error!.Code);
            Assert.Contains("1 MB", r.Error.Message, StringComparison.Ordinal);
            pipe.Close();
            await write.WaitAsync(Wait.Long, Ct);
        }

        Assert.Equal(0, doba.Requests);
        Assert.True((await Call(doba.Folder, ToolCatalog.GetToday)).Ok);
    }

    [Fact]
    public async Task The_client_refuses_to_send_over_one_megabyte()
    {
        using var doba = new TestDoba();
        var notes = new string('n', BridgeHost.MaxMessageBytes);
        var r = await Call(doba.Folder, ToolCatalog.CreateTask, $$"""{"title":"x","notes":"{{notes}}"}""");
        Assert.Equal(BridgeErrorCodes.InvalidArgs, r.Error!.Code);
        Assert.Equal(0, doba.Requests);
    }

    [Fact]
    public async Task A_client_that_connects_and_leaves_does_not_hurt()
    {
        using var doba = new TestDoba();
        for (var i = 0; i < 10; i++)
        {
            await using var pipe = await ConnectRawAsync(doba.Folder);
        }

        await using (var partial = await ConnectRawAsync(doba.Folder))
        {
            await partial.WriteAsync("{\"protocol\":1,"u8.ToArray(), Ct); // and goes away mid-line
        }

        Assert.True((await Call(doba.Folder, ToolCatalog.GetToday)).Ok);
        Assert.Equal(1, doba.Requests);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Pipe_acl_allows_only_the_current_user_and_denies_network()
    {
        using var doba = new TestDoba();
        await using var pipe = await ConnectRawAsync(doba.Folder);
        CheckAcl(pipe);
    }

    [SupportedOSPlatform("windows")]
    private static void CheckAcl(PipeStream pipe)
    {
        var security = pipe.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User!;
        Assert.Equal(user, security.GetOwner(typeof(SecurityIdentifier)));

        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        var allows = rules.Where(r => r.AccessControlType == AccessControlType.Allow).ToList();
        var allow = Assert.Single(allows);
        Assert.Equal(user, allow.IdentityReference);
        var expected = PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance | PipeAccessRights.Synchronize;
        Assert.Equal(expected, allow.PipeAccessRights & expected);

        var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        var deny = Assert.Single(rules, r => r.AccessControlType == AccessControlType.Deny);
        Assert.Equal(network, deny.IdentityReference);
        Assert.Equal(PipeAccessRights.ReadWrite, deny.PipeAccessRights & PipeAccessRights.ReadWrite);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task A_pipe_the_user_may_not_open_is_reported_as_different_users()
    {
        using var folder = new TempDataFolder();
        using var server = CreateDenyingPipe(BridgeNames.PipeName(folder.Path));
        var waiting = server.WaitForConnectionAsync(Ct);
        var r = await Call(folder.Path, ToolCatalog.GetToday);
        Assert.Equal(BridgeErrorCodes.Unauthorized, r.Error!.Code);
        Assert.Equal(BridgeClient.UnauthorizedMessage, r.Error.Message);
        Assert.False(waiting.IsCompleted);
    }

    /// <summary>A pipe owned by the current user whose ACL denies the user read/write: what a pipe of Doba running as
    /// another user (or elevated with another owner) looks like to Doba.Mcp. A pipe with a foreign owner cannot be
    /// built here without elevation (Windows lets a process set only its own user as owner), so the owner check itself
    /// is .NET's <see cref="PipeOptions.CurrentUserOnly"/>, which BridgeClient always passes.</summary>
    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateDenyingPipe(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.SetOwner(identity.User!);
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.ReadWrite, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.CreateNewInstance | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security);
    }
}
