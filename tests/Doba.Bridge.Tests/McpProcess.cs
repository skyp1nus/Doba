using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Doba.Bridge.Tests;

/// <summary>A tool call's result as the MCP client sees it.</summary>
internal sealed record ToolResult(bool IsError, string Text, JsonElement? Structured)
{
    /// <summary>The structured result (the same JSON as the text) of a successful call.</summary>
    public JsonElement Json => Structured ?? throw new InvalidOperationException("No structured content: " + Text);
}

/// <summary>
/// Doba.Mcp.exe as a child process, driven like an MCP client over stdin/stdout (JSON-RPC, one message per line).
/// Always started with <c>--data &lt;temp folder&gt;</c>; killed on dispose if it is still running.
/// </summary>
internal sealed class McpProcess : IAsyncDisposable
{
    public const string LegacyVersion = "2025-11-25";
    public const string ModernVersion = "2026-07-28";

    /// <summary>Generous: a cold Debug exe, a busy machine.</summary>
    public static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly List<string> _lines = [];
    private readonly StringBuilder _stderr = new();
    private readonly Task _reader;
    private readonly Task _errorReader;
    private long _nextId;

    private McpProcess(Process process)
    {
        _process = process;
        _reader = Task.Run(ReadLoopAsync);
        _errorReader = Task.Run(async () =>
        {
            var text = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            lock (_stderr)
            {
                _stderr.Append(text);
            }
        });
    }

    /// <summary>True after <see cref="DiscoverAsync"/>: every request then carries the 2026-07-28 <c>_meta</c>.</summary>
    public bool Modern { get; private set; }

    /// <summary>Every line the server wrote to stdout so far.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    public string Stderr
    {
        get
        {
            lock (_stderr)
            {
                return _stderr.ToString();
            }
        }
    }

    public Process Process => _process;

    public static McpProcess Start(string dataFolder) => Start(["--data", dataFolder]);

    /// <summary>Starts the exe with exactly <paramref name="args"/> (tests always pass a temp folder or bad arguments).</summary>
    public static McpProcess Start(IReadOnlyList<string> args)
    {
        var exe = TestPaths.McpExe;
        Assert.True(File.Exists(exe), "Doba.Mcp.exe not found: " + exe);
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        return new McpProcess(Process.Start(info) ?? throw new InvalidOperationException("Doba.Mcp did not start."));
    }

    /// <summary>The 2025-11-25 handshake: <c>initialize</c>, then <c>notifications/initialized</c>.</summary>
    public async Task<JsonElement> InitializeAsync()
    {
        var result = await RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = LegacyVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "doba-bridge-tests", ["version"] = "1.0" },
        });
        await NotifyAsync("notifications/initialized");
        return result.GetProperty("result");
    }

    /// <summary>The 2026-07-28 start: <c>server/discover</c>; from now on every request carries <c>_meta</c>.</summary>
    public async Task<JsonElement> DiscoverAsync()
    {
        Modern = true;
        var result = await RequestAsync("server/discover", new JsonObject());
        return result.GetProperty("result");
    }

    /// <summary>Sends a request and returns the whole response message (with <c>result</c> or <c>error</c>).</summary>
    public async Task<JsonElement> RequestAsync(string method, JsonObject? parameters = null, TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        parameters ??= new JsonObject();
        if (Modern)
        {
            parameters["_meta"] = new JsonObject
            {
                ["io.modelcontextprotocol/protocolVersion"] = ModernVersion,
                ["io.modelcontextprotocol/clientInfo"] = new JsonObject { ["name"] = "doba-bridge-tests", ["version"] = "1.0" },
                ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject(),
            };
        }

        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
        await WriteLineAsync(message.ToJsonString());
        try
        {
            return await tcs.Task.WaitAsync(timeout ?? ReplyTimeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"No reply to {method} (id {id}). stderr: {Stderr}");
        }
    }

    public Task NotifyAsync(string method) =>
        WriteLineAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method }.ToJsonString());

    public async Task<JsonElement> ListToolsAsync() => (await RequestAsync("tools/list")).GetProperty("result");

    /// <summary>Calls <paramref name="name"/> with <paramref name="argumentsJson"/> (a JSON object).</summary>
    public async Task<ToolResult> CallToolAsync(string name, string argumentsJson = "{}", TimeSpan? timeout = null)
    {
        var response = await RequestAsync("tools/call", new JsonObject
        {
            ["name"] = name,
            ["arguments"] = JsonNode.Parse(argumentsJson),
        }, timeout);
        if (response.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException("tools/call failed with a protocol error: " + error.GetRawText());
        }

        var result = response.GetProperty("result");
        var isError = result.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True;
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        JsonElement? structured = result.TryGetProperty("structuredContent", out var s) && s.ValueKind == JsonValueKind.Object
            ? s.Clone()
            : null;
        return new ToolResult(isError, text, structured);
    }

    /// <summary>Closes stdin (the client went away) and waits for the exit; returns how long the exit took.</summary>
    public async Task<TimeSpan> CloseInputAndWaitAsync(TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        _process.StandardInput.Close();
        using var cts = new CancellationTokenSource(timeout);
        await _process.WaitForExitAsync(cts.Token);
        return watch.Elapsed;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    _process.StandardInput.Close();
                }
                catch (Exception e) when (e is IOException or InvalidOperationException)
                {
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }

            await Task.WhenAll(_reader, _errorReader).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            _process.Dispose();
            _writeLock.Dispose();
        }
    }

    private async Task WriteLineAsync(string line)
    {
        await _writeLock.WaitAsync();
        try
        {
            await _process.StandardInput.WriteAsync(line + "\n");
            await _process.StandardInput.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        var output = _process.StandardOutput;
        while (await output.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            lock (_lines)
            {
                _lines.Add(line);
            }

            JsonElement message;
            try
            {
                using var doc = JsonDocument.Parse(line);
                message = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue; // recorded in Lines; the hygiene test fails on it
            }

            if (message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
                && (message.TryGetProperty("result", out _) || message.TryGetProperty("error", out _))
                && _pending.TryRemove(id.GetInt64(), out var tcs))
            {
                tcs.TrySetResult(message);
            }
        }

        foreach (var tcs in _pending.Values)
        {
            tcs.TrySetException(new IOException("Doba.Mcp closed stdout. stderr: " + Stderr));
        }
    }
}
