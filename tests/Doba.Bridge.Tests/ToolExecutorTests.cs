using System.Text.Json;
using Doba.Bridge.Wire;
using Doba.Core.Model;
using Doba.Core.Store;
using Doba.Core.Time;

namespace Doba.Bridge.Tests;

/// <summary>ToolExecutor in-process: arguments, access, results. The stdio end-to-end tests repeat the essentials
/// through Doba.Mcp.exe.</summary>
public sealed class ToolExecutorTests
{
    private static readonly DateOnly Today = TestDoba.Today;
    private static readonly TimeOnly Ten = new(10, 0);

    private readonly CountingPersistence _persistence = new();
    private readonly List<TaskStoreChangedEventArgs> _events = [];
    private readonly FixedTimeProvider _clock = new(TestDoba.Now);
    private TaskStore _store;
    private AppSettings _settings = AppSettings.Default with { ClaudeAccess = true, ClaudeAccessLevel = ClaudeAccessLevel.ReadWrite };
    private long? _openEditId;

    public ToolExecutorTests() => _store = NewStore([], ["work", "home"]);

    private TaskStore NewStore(IEnumerable<TaskItem> tasks, IEnumerable<string> tags)
    {
        var store = new TaskStore(tasks, tags, _persistence);
        store.Changed += (_, e) => _events.Add(e);
        return store;
    }

    private void Seed(params TaskItem[] tasks) => _store = NewStore(tasks, ["work", "home"]);

    private static TaskItem T(long id, string title = "Task", DateOnly? date = null, DateOnly? end = null, TimeOnly? time = null,
        string notes = "", Priority prio = Priority.None, string? tag = null, bool done = false, int duration = 0) =>
        new(id, title, notes, date, end, time, duration, prio, tag, done);

    private BridgeResponse Run(string tool, string argsJson = "{}", int protocol = BridgeNames.Protocol)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var request = new BridgeRequest { Protocol = protocol, Client = "tests", Tool = tool, Args = args };
        return ToolExecutor.Execute(request, new ToolContext(_store, _settings, _clock, _openEditId));
    }

    private JsonElement Ok(string tool, string argsJson = "{}")
    {
        var r = Run(tool, argsJson);
        Assert.True(r.Ok, r.Error?.Code + ": " + r.Error?.Message);
        var result = r.Result!.Value;
        Assert.Equal("2026-11-19", result.GetProperty("today").GetString());
        return result;
    }

    private BridgeError Fails(string tool, string argsJson, string code)
    {
        var r = Run(tool, argsJson);
        Assert.False(r.Ok, "expected " + code + ", got " + r.Result?.GetRawText());
        Assert.Null(r.Result);
        Assert.Equal(code, r.Error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(r.Error.Message));
        return r.Error;
    }

    private static long[] Ids(JsonElement result, string name = "tasks") =>
        result.GetProperty(name).EnumerateArray().Select(t => t.GetProperty("id").GetInt64()).ToArray();

    // ---------- protocol, tool, access ----------

    [Fact]
    public void Protocol_mismatch_is_refused()
    {
        var r = Run(ToolCatalog.GetToday, protocol: 2);
        Assert.Equal(BridgeErrorCodes.ProtocolMismatch, r.Error!.Code);
        Assert.Contains("version", r.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_tool_is_invalid_args() =>
        Assert.Contains("due_date", Fails("due_date", "{}", BridgeErrorCodes.InvalidArgs).Message, StringComparison.Ordinal);

    [Fact]
    public void Access_off_in_settings_refuses_everything()
    {
        _settings = _settings with { ClaudeAccess = false };
        Assert.Equal(ToolExecutor.AccessOffMessage, Fails(ToolCatalog.GetToday, "{}", BridgeErrorCodes.Closing).Message);
        Assert.Contains("Settings", ToolExecutor.AccessOffMessage, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> WriteCalls => new()
    {
        { ToolCatalog.CreateTask, """{"title":"x"}""" },
        { ToolCatalog.UpdateTask, """{"id":1,"title":"x"}""" },
        { ToolCatalog.SetTaskDone, """{"id":1,"done":true}""" },
        { ToolCatalog.MoveTask, """{"id":1,"date":"inbox"}""" },
        { ToolCatalog.DeleteTask, """{"id":1}""" },
    };

    [Theory]
    [MemberData(nameof(WriteCalls))]
    public void Read_level_refuses_every_change_and_changes_nothing(string tool, string args)
    {
        Seed(T(1, date: Today));
        _settings = _settings with { ClaudeAccessLevel = ClaudeAccessLevel.Read };
        var error = Fails(tool, args, BridgeErrorCodes.ReadOnly);
        Assert.Equal(
            "Doba lets Claude read tasks only. To allow changes: Doba → Settings → Data → Claude can → Read & edit.",
            error.Message);
        Assert.Empty(_events);
        Assert.Equal(0, _persistence.Upserts + _persistence.Deletes);
        Assert.Equal(T(1, date: Today), _store.Get(1));
    }

    [Fact]
    public void Read_level_still_reads()
    {
        _settings = _settings with { ClaudeAccessLevel = ClaudeAccessLevel.Read };
        foreach (var tool in ToolCatalog.All.Where(t => t.ReadOnly))
        {
            var args = tool.Name switch
            {
                ToolCatalog.ListTasks => """{"from":"2026-11-19"}""",
                ToolCatalog.SearchTasks => """{"query":"a"}""",
                ToolCatalog.GetTask => """{"id":1}""",
                _ => "{}",
            };
            var r = Run(tool.Name, args);
            Assert.True(r.Ok || r.Error!.Code == BridgeErrorCodes.NotFound, tool.Name + ": " + r.Error?.Message);
        }
    }

    // ---------- arguments ----------

    [Fact]
    public void Unknown_argument_lists_the_allowed_ones()
    {
        var message = Fails(ToolCatalog.CreateTask, """{"title":"x","due_date":"2026-11-20"}""", BridgeErrorCodes.InvalidArgs).Message;
        Assert.Contains("'due_date'", message, StringComparison.Ordinal);
        Assert.Contains("title, notes, date, days, time, duration_minutes, priority, tag", message, StringComparison.Ordinal);
        Assert.Contains("takes no arguments", Fails(ToolCatalog.GetToday, """{"x":1}""", BridgeErrorCodes.InvalidArgs).Message, StringComparison.Ordinal);
        Assert.Empty(_events);
    }

    [Fact]
    public void Null_is_refused_and_names_the_field()
    {
        Seed(T(1, "Old", notes: "keep"));
        var message = Fails(ToolCatalog.UpdateTask, """{"id":1,"title":"New","notes":null}""", BridgeErrorCodes.InvalidArgs).Message;
        Assert.Contains("'notes' must not be null", message, StringComparison.Ordinal);
        Assert.Contains("clear: [\"notes\"]", message, StringComparison.Ordinal);
        Assert.Equal(T(1, "Old", notes: "keep"), _store.Get(1));
        Assert.Empty(_events);

        Assert.Contains("'title' must not be null", Fails(ToolCatalog.CreateTask, """{"title":null}""", BridgeErrorCodes.InvalidArgs).Message, StringComparison.Ordinal);
        Assert.Contains("'limit' must not be null", Fails(ToolCatalog.ListInbox, """{"limit":null}""", BridgeErrorCodes.InvalidArgs).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ToolCatalog.CreateTask, """{}""", "'title' is required")]
    [InlineData(ToolCatalog.CreateTask, """{"title":5}""", "'title' must be a string")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","date":"20.11.2026"}""", "'date' must be a date as YYYY-MM-DD")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","date":"2026-02-30"}""", "'date' must be a date")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","date":"2026-11-20T10:00"}""", "'date' must be a date")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","date":"2026-11-20","time":"9:30"}""", "'time' must be a time as HH:mm")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","date":"2026-11-20","time":"24:00"}""", "'time' must be a time")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","date":"2026-11-20","time":"09:30:00"}""", "'time' must be a time")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","days":"3"}""", "'days' must be a whole number from 1 to 14")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","days":2.5}""", "'days' must be a whole number")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","date":"2026-11-20","days":15}""", "'days' must be from 1 to 14; got 15")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","duration_minutes":1441}""", "'duration_minutes' must be from 0 to 1440")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","priority":"urgent"}""", "'priority' must be one of none, low, medium, high, asap")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","priority":"High"}""", "'priority' must be one of")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","tag":["work"]}""", "'tag' must be a string")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"   "}""", "title must not be empty")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","time":"10:00"}""", "time needs a date")]
    [InlineData(ToolCatalog.CreateTask, """{"title":"x","date":"2026-11-20","days":2,"time":"10:00"}""", "time is only for a single-day task")]
    [InlineData(ToolCatalog.ListTasks, """{}""", "'from' is required")]
    [InlineData(ToolCatalog.ListTasks, """{"from":"2026-11-19","to":"2026-11-18"}""", "'to' must not be before 'from'")]
    [InlineData(ToolCatalog.ListTasks, """{"from":"2026-11-01","to":"2027-02-02"}""", "at most 92 days")]
    [InlineData(ToolCatalog.ListTasks, """{"from":"2026-11-19","include_done":"yes"}""", "'include_done' must be true or false")]
    [InlineData(ToolCatalog.ListTasks, """{"from":"2026-11-19","limit":0}""", "'limit' must be from 1 to 500")]
    [InlineData(ToolCatalog.ListTasks, """{"from":"2026-11-19","limit":501}""", "'limit' must be from 1 to 500")]
    [InlineData(ToolCatalog.ListInbox, """{"offset":-1}""", "'offset' must be")]
    [InlineData(ToolCatalog.SearchTasks, """{"query":"  "}""", "'query' must contain some text")]
    [InlineData(ToolCatalog.SearchTasks, """{"query":"a","limit":51}""", "'limit' must be from 1 to 50")]
    [InlineData(ToolCatalog.GetTask, """{"id":"1"}""", "'id' must be a whole number")]
    [InlineData(ToolCatalog.GetTask, """{"id":0}""", "'id' must be at least 1")]
    [InlineData(ToolCatalog.GetTask, """{}""", "'id' is required")]
    [InlineData(ToolCatalog.UpdateTask, """{"id":1,"clear":"notes"}""", "'clear' must be an array")]
    [InlineData(ToolCatalog.UpdateTask, """{"id":1,"clear":["title"]}""", "'clear' may only list notes, time, tag and date")]
    [InlineData(ToolCatalog.SetTaskDone, """{"id":1}""", "'done' is required")]
    [InlineData(ToolCatalog.SetTaskDone, """{"id":1,"done":1}""", "'done' must be true or false")]
    [InlineData(ToolCatalog.MoveTask, """{"id":1,"date":"tomorrow"}""", "or \"inbox\"")]
    [InlineData(ToolCatalog.MoveTask, """{"id":1,"date":"Inbox"}""", "or \"inbox\"")]
    [InlineData(ToolCatalog.MoveTask, """{"id":1}""", "'date' is required")]
    public void Invalid_arguments_say_what_to_fix(string tool, string args, string expected)
    {
        Seed(T(1, date: Today));
        Assert.Contains(expected, Fails(tool, args, BridgeErrorCodes.InvalidArgs).Message, StringComparison.Ordinal);
        Assert.Empty(_events);
        Assert.Equal(0, _persistence.Upserts);
    }

    [Theory]
    [InlineData(ToolCatalog.GetTask, """{"id":99}""")]
    [InlineData(ToolCatalog.UpdateTask, """{"id":99,"title":"x"}""")]
    [InlineData(ToolCatalog.SetTaskDone, """{"id":99,"done":true}""")]
    [InlineData(ToolCatalog.MoveTask, """{"id":99,"date":"inbox"}""")]
    [InlineData(ToolCatalog.DeleteTask, """{"id":99}""")]
    public void Missing_id_is_not_found(string tool, string args)
    {
        Seed(T(1));
        Assert.Contains("No task with id 99", Fails(tool, args, BridgeErrorCodes.NotFound).Message, StringComparison.Ordinal);
        Assert.Empty(_events);
    }

    [Theory]
    [InlineData(ToolCatalog.UpdateTask, """{"id":5,"title":"x"}""")]
    [InlineData(ToolCatalog.SetTaskDone, """{"id":5,"done":true}""")]
    [InlineData(ToolCatalog.MoveTask, """{"id":5,"date":"inbox"}""")]
    [InlineData(ToolCatalog.DeleteTask, """{"id":5}""")]
    public void A_task_open_in_the_editor_is_a_conflict(string tool, string args)
    {
        Seed(T(5, date: Today), T(6, date: Today));
        _openEditId = 5;
        Assert.Contains("open in Doba's editor", Fails(tool, args, BridgeErrorCodes.Conflict).Message, StringComparison.Ordinal);
        Assert.Empty(_events);
        Assert.NotNull(_store.Get(5));

        // Reading it is fine, and other tasks can change.
        Ok(ToolCatalog.GetTask, """{"id":5}""");
        Assert.True(Run(tool, args.Replace("\"id\":5", "\"id\":6", StringComparison.Ordinal)).Ok);
    }

    // ---------- reads ----------

    [Fact]
    public void Get_today_has_the_clock_and_the_today_groups()
    {
        Seed(
            T(1, "Overdue", date: Today.AddDays(-2)),
            T(2, "Trip", date: Today.AddDays(-1), end: Today.AddDays(1)),
            T(3, "Call", date: Today, time: new TimeOnly(14, 0), duration: 30),
            T(4, "Standup", date: Today, time: new TimeOnly(9, 0)),
            T(5, "Read", date: Today),
            T(6, "Done", date: Today, done: true),
            T(7, "Tomorrow", date: Today.AddDays(1)),
            T(8, "Inbox"));
        var r = Ok(ToolCatalog.GetToday);
        Assert.Equal("Thursday", r.GetProperty("weekday").GetString());
        Assert.Equal("10:26", r.GetProperty("now").GetString());
        Assert.Equal("+01:00", r.GetProperty("utc_offset").GetString());
        Assert.Equal("Monday", r.GetProperty("week_start").GetString());
        Assert.Equal([1L], Ids(r, "overdue"));
        Assert.Equal([2L], Ids(r, "in_progress"));
        Assert.Equal([4L, 3], Ids(r, "scheduled"));
        Assert.Equal([5L], Ids(r, "anytime"));
        Assert.Equal([6L], Ids(r, "done"));
        Assert.Empty(_events);
    }

    [Fact]
    public void Get_today_follows_week_start_and_a_negative_offset()
    {
        _settings = _settings with { WeekStart = DayOfWeek.Sunday };
        _clock.Set(new DateTimeOffset(2026, 11, 19, 23, 5, 0, TimeSpan.FromHours(-5.5)));
        var r = Ok(ToolCatalog.GetToday);
        Assert.Equal("Sunday", r.GetProperty("week_start").GetString());
        Assert.Equal("-05:30", r.GetProperty("utc_offset").GetString());
        Assert.Equal("23:05", r.GetProperty("now").GetString());
        Assert.Empty(r.GetProperty("anytime").EnumerateArray());
    }

    [Fact]
    public void Task_shape_is_the_documented_one()
    {
        Seed(
            T(1, "Timed", date: Today, time: new TimeOnly(9, 5), duration: 45, notes: "n", prio: Priority.Asap, tag: "work"),
            T(2, "Trip", date: Today, end: Today.AddDays(2), done: true),
            T(3, "Inbox", prio: Priority.Medium));
        var timed = Ok(ToolCatalog.GetTask, """{"id":1}""").GetProperty("task");
        Assert.Equal(
            """{"id":1,"title":"Timed","notes":"n","date":"2026-11-19","end":null,"days":1,"time":"09:05","duration_minutes":45,"priority":"asap","tag":"work","done":false}""",
            timed.GetRawText());
        var trip = Ok(ToolCatalog.GetTask, """{"id":2}""").GetProperty("task");
        Assert.Equal(
            """{"id":2,"title":"Trip","notes":"","date":"2026-11-19","end":"2026-11-21","days":3,"time":null,"duration_minutes":0,"priority":"none","tag":null,"done":true}""",
            trip.GetRawText());
        var inbox = Ok(ToolCatalog.GetTask, """{"id":3}""").GetProperty("task");
        Assert.Equal(JsonValueKind.Null, inbox.GetProperty("date").ValueKind);
        Assert.Equal("medium", inbox.GetProperty("priority").GetString());
    }

    [Fact]
    public void Lists_cut_notes_and_get_task_returns_them_whole()
    {
        var longNotes = new string('a', 199) + "😀" + new string('b', 50);
        Seed(T(1, date: Today, notes: longNotes), T(2, date: Today, notes: new string('c', 200)));
        var listed = Ok(ToolCatalog.ListTasks, """{"from":"2026-11-19"}""").GetProperty("tasks");
        var first = listed[0];
        Assert.Equal(new string('a', 199), first.GetProperty("notes").GetString()); // the emoji's pair is not split
        Assert.True(first.GetProperty("notes_truncated").GetBoolean());
        var second = listed[1];
        Assert.Equal(200, second.GetProperty("notes").GetString()!.Length);
        Assert.False(second.TryGetProperty("notes_truncated", out _));

        Assert.Equal(longNotes, Ok(ToolCatalog.GetTask, """{"id":1}""").GetProperty("task").GetProperty("notes").GetString());
    }

    [Fact]
    public void List_tasks_pages_and_reports_the_total()
    {
        Seed([.. Enumerable.Range(1, 7).Select(i => T(i, date: Today.AddDays(i % 3)))]);
        var all = Ok(ToolCatalog.ListTasks, """{"from":"2026-11-19","to":"2026-11-21"}""");
        Assert.Equal(7, all.GetProperty("total").GetInt32());
        Assert.False(all.GetProperty("truncated").GetBoolean());
        Assert.Equal("2026-11-19", all.GetProperty("from").GetString());
        Assert.Equal("2026-11-21", all.GetProperty("to").GetString());
        Assert.Equal([3L, 6, 1, 4, 7, 2, 5], Ids(all));

        var page = Ok(ToolCatalog.ListTasks, """{"from":"2026-11-19","to":"2026-11-21","limit":3,"offset":2}""");
        Assert.Equal([1L, 4, 7], Ids(page));
        Assert.Equal((7, 2, true), (page.GetProperty("total").GetInt32(), page.GetProperty("offset").GetInt32(), page.GetProperty("truncated").GetBoolean()));
        var last = Ok(ToolCatalog.ListTasks, """{"from":"2026-11-19","to":"2026-11-21","limit":3,"offset":6}""");
        Assert.Equal([5L], Ids(last));
        Assert.False(last.GetProperty("truncated").GetBoolean());
        Assert.Empty(Ids(Ok(ToolCatalog.ListTasks, """{"from":"2026-11-19","to":"2026-11-21","offset":50}""")));
    }

    [Fact]
    public void List_tasks_defaults_to_one_day_and_hides_done_unless_asked()
    {
        Seed(T(1, date: Today), T(2, date: Today, done: true), T(3, date: Today.AddDays(1)), T(4, date: Today.AddDays(-5), end: Today));
        Assert.Equal([4L, 1], Ids(Ok(ToolCatalog.ListTasks, """{"from":"2026-11-19"}""")));
        Assert.Equal([4L, 1, 2], Ids(Ok(ToolCatalog.ListTasks, """{"from":"2026-11-19","include_done":true}""")));
        Assert.NotEmpty(Ids(Ok(ToolCatalog.ListTasks, """{"from":"2026-11-01","to":"2027-02-01"}"""))); // exactly 92 days
    }

    [Fact]
    public void List_inbox_is_in_inbox_order()
    {
        Seed(T(1), T(2, prio: Priority.Asap), T(3, done: true), T(4, prio: Priority.High), T(5, date: Today));
        Assert.Equal([2L, 4, 1], Ids(Ok(ToolCatalog.ListInbox)));
        var withDone = Ok(ToolCatalog.ListInbox, """{"include_done":true,"limit":2}""");
        Assert.Equal([2L, 4], Ids(withDone));
        Assert.Equal(4, withDone.GetProperty("total").GetInt32());
        Assert.True(withDone.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void Search_finds_titles_open_first_and_says_when_cut()
    {
        Seed(T(1, "Buy bread", date: Today, done: true), T(2, "bread for toast"), T(3, "Bake BREAD", date: Today.AddDays(1)), T(4, "Milk", notes: "bread"));
        var r = Ok(ToolCatalog.SearchTasks, """{"query":" bread "}""");
        Assert.Equal([3L, 2, 1], Ids(r));
        Assert.Equal("bread", r.GetProperty("query").GetString());
        Assert.False(r.GetProperty("truncated").GetBoolean());
        var cut = Ok(ToolCatalog.SearchTasks, """{"query":"bread","limit":2}""");
        Assert.Equal([3L, 2], Ids(cut));
        Assert.True(cut.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void List_tags_counts_open_tasks()
    {
        Seed(T(1, tag: "work"), T(2, tag: "work", done: true), T(3, tag: "work", date: Today), T(4, tag: "home", done: true));
        var tags = Ok(ToolCatalog.ListTags).GetProperty("tags");
        Assert.Equal("""[{"name":"work","open_tasks":2},{"name":"home","open_tasks":0}]""", tags.GetRawText());
    }

    [Fact]
    public void Non_ascii_text_is_written_as_is()
    {
        Seed(T(1, "Купити хліб", date: Today, notes: "line1\nline2 \"q\""));
        var raw = Ok(ToolCatalog.GetTask, """{"id":1}""").GetRawText();
        Assert.Contains("Купити хліб", raw, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', raw); // one line on the pipe
        Assert.Contains("line1\\nline2", raw, StringComparison.Ordinal);
    }

    // ---------- writes ----------

    [Fact]
    public void Create_adds_one_task_with_the_default_priority()
    {
        _settings = _settings with { DefaultPriority = Priority.High };
        var r = Ok(ToolCatalog.CreateTask, """{"title":" Buy bread ","date":"2026-11-20","time":"18:30","tag":"#Home"}""");
        var task = r.GetProperty("task");
        Assert.True(r.GetProperty("changed").GetBoolean());
        Assert.Equal(1, task.GetProperty("id").GetInt64());
        Assert.Equal(new TaskItem(1, "Buy bread", "", Today.AddDays(1), null, new TimeOnly(18, 30), 0, Priority.High, "home", false), _store.Get(1));
        Assert.Single(_events);
        Assert.Equal(TaskChangeKind.Added, _events[0].Kind);
        Assert.Equal(1, _persistence.Upserts);
    }

    [Fact]
    public void Create_without_a_date_goes_to_the_inbox_and_spans_with_days()
    {
        Assert.Equal(JsonValueKind.Null, Ok(ToolCatalog.CreateTask, """{"title":"Someday"}""").GetProperty("task").GetProperty("date").ValueKind);
        var trip = Ok(ToolCatalog.CreateTask, """{"title":"Trip","date":"2026-11-20","days":3,"priority":"asap"}""").GetProperty("task");
        Assert.Equal(("2026-11-22", 3, "asap"), (trip.GetProperty("end").GetString(), trip.GetProperty("days").GetInt32(), trip.GetProperty("priority").GetString()));
        Assert.Equal(2, _events.Count);
    }

    [Fact]
    public void Unknown_tag_lists_the_existing_tags_and_creates_nothing()
    {
        var message = Fails(ToolCatalog.CreateTask, """{"title":"x","tag":"errands"}""", BridgeErrorCodes.InvalidArgs).Message;
        Assert.Contains("Existing tags: work, home", message, StringComparison.Ordinal);
        Assert.Equal(0, _store.Count);
        Assert.Equal(["work", "home"], _store.Tags);
    }

    [Fact]
    public void Update_changes_only_supplied_fields_in_one_write()
    {
        Seed(T(1, "Old", date: Today, time: Ten, duration: 0, notes: "keep", tag: "work", prio: Priority.Low));
        var r = Ok(ToolCatalog.UpdateTask, """{"id":1,"title":"New","priority":"high"}""");
        Assert.True(r.GetProperty("changed").GetBoolean());
        Assert.Equal(T(1, "New", date: Today, time: Ten, duration: 0, notes: "keep", tag: "work", prio: Priority.High), _store.Get(1));
        Assert.Single(_events);
        Assert.Equal(1, _persistence.Upserts);
    }

    [Fact]
    public void Update_with_nothing_new_writes_nothing()
    {
        Seed(T(1, "Same", date: Today));
        var r = Ok(ToolCatalog.UpdateTask, """{"id":1,"title":"Same"}""");
        Assert.False(r.GetProperty("changed").GetBoolean());
        Assert.Empty(_events);
        Assert.Equal(0, _persistence.Upserts);
    }

    [Fact]
    public void Update_clears_fields_and_moves_to_the_inbox()
    {
        Seed(T(1, date: Today, time: Ten, notes: "n", tag: "work"));
        Ok(ToolCatalog.UpdateTask, """{"id":1,"clear":["notes","tag"]}""");
        Assert.Equal(T(1, date: Today, time: Ten), _store.Get(1));
        Ok(ToolCatalog.UpdateTask, """{"id":1,"clear":["date"]}""");
        Assert.Equal(T(1), _store.Get(1));
        Assert.Equal(2, _events.Count);
    }

    [Fact]
    public void Update_refuses_a_rule_break_and_keeps_the_task()
    {
        Seed(T(1, date: Today, time: Ten));
        Assert.Contains("clear: [\"time\"]", Fails(ToolCatalog.UpdateTask, """{"id":1,"days":3}""", BridgeErrorCodes.InvalidArgs).Message, StringComparison.Ordinal);
        Assert.Contains("Existing tags", Fails(ToolCatalog.UpdateTask, """{"id":1,"tag":"nope"}""", BridgeErrorCodes.InvalidArgs).Message, StringComparison.Ordinal);
        Assert.Equal(T(1, date: Today, time: Ten), _store.Get(1));
        Assert.Empty(_events);
    }

    [Fact]
    public void Set_done_is_one_write_and_a_no_op_when_already_so()
    {
        Seed(T(1, date: Today));
        Assert.True(Ok(ToolCatalog.SetTaskDone, """{"id":1,"done":true}""").GetProperty("changed").GetBoolean());
        Assert.True(_store.Get(1)!.Done);
        Assert.False(Ok(ToolCatalog.SetTaskDone, """{"id":1,"done":true}""").GetProperty("changed").GetBoolean());
        Assert.Single(_events);
        Ok(ToolCatalog.SetTaskDone, """{"id":1,"done":false}""");
        Assert.False(_store.Get(1)!.Done);
        Assert.Equal(2, _persistence.Upserts);
    }

    [Fact]
    public void Move_shifts_spans_whole_and_inbox_drops_time()
    {
        Seed(T(1, date: Today, end: Today.AddDays(2)), T(2, date: Today, time: Ten));
        var moved = Ok(ToolCatalog.MoveTask, """{"id":1,"date":"2026-12-01"}""").GetProperty("task");
        Assert.Equal(("2026-12-01", "2026-12-03"), (moved.GetProperty("date").GetString(), moved.GetProperty("end").GetString()));
        Ok(ToolCatalog.MoveTask, """{"id":2,"date":"inbox"}""");
        Assert.Equal(T(2), _store.Get(2));
        Assert.False(Ok(ToolCatalog.MoveTask, """{"id":2,"date":"inbox"}""").GetProperty("changed").GetBoolean());
        Assert.Equal(2, _events.Count);
    }

    [Fact]
    public void Delete_removes_one_task_and_returns_it()
    {
        Seed(T(1, "Gone", date: Today), T(2));
        var r = Ok(ToolCatalog.DeleteTask, """{"id":1}""");
        Assert.Equal("Gone", r.GetProperty("deleted").GetProperty("title").GetString());
        Assert.Null(_store.Get(1));
        Assert.Single(_events);
        Assert.Equal(1, _persistence.Deletes);
        Fails(ToolCatalog.DeleteTask, """{"id":1}""", BridgeErrorCodes.NotFound);
    }

    // ---------- numbers written as 3.0, far dates, size limits ----------

    [Fact]
    public void Whole_numbers_written_with_a_fraction_or_exponent_are_accepted()
    {
        Seed(T(1, "One", date: Today));
        var created = Ok(ToolCatalog.CreateTask, """{"title":"Trip","date":"2026-11-20","days":3.0,"priority":"low"}""");
        Assert.Equal(3, created.GetProperty("task").GetProperty("days").GetInt32());
        Assert.Equal(Today.AddDays(3), _store.Get(created.GetProperty("task").GetProperty("id").GetInt64())!.End);

        Assert.Equal([1L], Ids(Ok(ToolCatalog.ListTasks, """{"from":"2026-11-19","limit":1e2,"offset":0.0}""")));
        Assert.Equal("One", Ok(ToolCatalog.GetTask, """{"id":1.00}""").GetProperty("task").GetProperty("title").GetString());
        Assert.Equal(1, Ok(ToolCatalog.SearchTasks, """{"query":"one","limit":5E0}""").GetProperty("tasks").GetArrayLength());
    }

    [Theory]
    [InlineData("""{"title":"x","date":"2026-11-20","days":2.5}""", "'days' must be a whole number")]
    [InlineData("""{"title":"x","date":"2026-11-20","days":1e400}""", "'days' must be a whole number")]
    [InlineData("""{"title":"x","date":"2026-11-20","days":1.5e1}""", "'days' must be from 1 to 14; got 15")]
    [InlineData("""{"title":"x","date":"2026-11-20","duration_minutes":-0.5}""", "'duration_minutes' must be a whole number")]
    public void Fractions_and_out_of_range_numbers_are_still_refused(string args, string expected)
    {
        Assert.Contains(expected, Fails(ToolCatalog.CreateTask, args, BridgeErrorCodes.InvalidArgs).Message, StringComparison.Ordinal);
        Assert.Empty(_events);
    }

    [Fact]
    public void List_tasks_from_the_first_day_of_the_calendar_is_an_empty_list()
    {
        Seed(T(1, "Trip", date: Today, end: Today.AddDays(2)));
        var r = Ok(ToolCatalog.ListTasks, """{"from":"0001-01-01","to":"0001-01-05","include_done":true}""");
        Assert.Empty(Ids(r));
        Assert.Equal(0, r.GetProperty("total").GetInt32());
    }

    [Fact]
    public void Get_today_cuts_each_list_at_fifty_and_says_how_many_there_are()
    {
        var overdue = Enumerable.Range(1, ToolCatalog.TodayGroupLimit + 10).Select(i => T(i, "Old " + i, date: Today.AddDays(-1)));
        Seed([.. overdue, T(100, "Read", date: Today)]);
        var r = Ok(ToolCatalog.GetToday);

        Assert.Equal(ToolCatalog.TodayGroupLimit, r.GetProperty("overdue").GetArrayLength());
        Assert.True(r.GetProperty("overdue_truncated").GetBoolean());
        Assert.Equal(ToolCatalog.TodayGroupLimit + 10, r.GetProperty("overdue_total").GetInt32());
        Assert.Equal([100L], Ids(r, "anytime"));
        Assert.False(r.TryGetProperty("anytime_truncated", out _));
        Assert.False(r.TryGetProperty("anytime_total", out _));
        // The clock comes first, before any list.
        Assert.Equal(["today", "weekday", "now", "utc_offset", "week_start"], r.EnumerateObject().Take(5).Select(p => p.Name));
    }

    [Fact]
    public void A_change_to_a_task_with_huge_notes_answers_with_cut_notes_and_get_task_cuts_too()
    {
        var huge = new string('n', BridgeHost.MaxMessageBytes + 1000);
        Seed(T(1, "Big", date: Today, notes: huge));

        var updated = Ok(ToolCatalog.UpdateTask, """{"id":1,"priority":"high"}""").GetProperty("task");
        Assert.Equal(ToolCatalog.ListNotesLength, updated.GetProperty("notes").GetString()!.Length);
        Assert.True(updated.GetProperty("notes_truncated").GetBoolean());
        Assert.Equal(Priority.High, _store.Get(1)!.Priority);
        Assert.Equal(huge, _store.Get(1)!.Notes); // only the answer is cut

        foreach (var (tool, args) in new[]
        {
            (ToolCatalog.SetTaskDone, """{"id":1,"done":true}"""),
            (ToolCatalog.MoveTask, """{"id":1,"date":"2026-11-25"}"""),
        })
        {
            var task = Ok(tool, args).GetProperty("task");
            Assert.True(task.GetProperty("notes_truncated").GetBoolean());
        }

        var got = Ok(ToolCatalog.GetTask, """{"id":1}""").GetProperty("task");
        Assert.Equal(ToolCatalog.TaskNotesLength, got.GetProperty("notes").GetString()!.Length);
        Assert.True(got.GetProperty("notes_truncated").GetBoolean());

        var deleted = Ok(ToolCatalog.DeleteTask, """{"id":1}""").GetProperty("deleted");
        Assert.Equal(ToolCatalog.ListNotesLength, deleted.GetProperty("notes").GetString()!.Length);
        Assert.Null(_store.Get(1));
    }

    [Fact]
    public void A_change_whose_task_is_too_large_to_send_is_still_a_success()
    {
        var title = new string('t', BridgeHost.MaxMessageBytes);
        Seed(T(1, title, date: Today));

        var r = Ok(ToolCatalog.SetTaskDone, """{"id":1,"done":true}""");

        Assert.True(r.GetProperty("changed").GetBoolean());
        Assert.True(r.GetProperty("task_too_large").GetBoolean());
        Assert.Equal(["id"], r.GetProperty("task").EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, r.GetProperty("task").GetProperty("id").GetInt64());
        Assert.True(_store.Get(1)!.Done);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(r.GetRawText()) < BridgeHost.MaxMessageBytes);
    }
}
