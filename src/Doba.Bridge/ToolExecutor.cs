using System.Globalization;
using System.Text.Json;
using Doba.Bridge.Internal;
using Doba.Bridge.Wire;
using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Projections;
using Doba.Core.Time;

namespace Doba.Bridge;

/// <summary>Runs one bridge request against the store: checks the tool, its arguments and the access level, calls
/// Doba.Core and builds the JSON result. Pure logic, no threads; call it on the store's (UI) thread.</summary>
/// <remarks>
/// <para>Order of checks: protocol version, tool name, Claude access on, access level (a change while Claude may only
/// read), arguments (names, nulls, types, ranges), then the task id (<see cref="BridgeErrorCodes.NotFound"/>) and the
/// editor (<see cref="BridgeErrorCodes.Conflict"/>). A change is exactly one store mutation (Add, Update or Delete),
/// or none when it would change nothing.</para>
/// <para>Every result carries <c>today</c>; lists carry notes cut to <see cref="ToolCatalog.ListNotesLength"/>
/// characters (<c>notes_truncated: true</c>).</para>
/// </remarks>
public static class ToolExecutor
{
    /// <summary>The <see cref="BridgeErrorCodes.ReadOnly"/> message.</summary>
    public const string ReadOnlyMessage =
        "Doba lets Claude read tasks only. To allow changes: Doba → Settings → Data → Claude can → Read & edit.";

    /// <summary>The <see cref="BridgeErrorCodes.Closing"/> message when Claude access is off in the settings.</summary>
    public const string AccessOffMessage =
        "Claude access is off in Doba. To turn it on: Doba → Settings → Data → Claude access.";

    private static readonly (string Key, string Name)[] s_todayGroups =
    [
        ("over", "overdue"),
        ("ongoing", "in_progress"),
        ("sched", "scheduled"),
        ("any", "anytime"),
        ("done", "done"),
    ];

    public static BridgeResponse Execute(BridgeRequest request, ToolContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (request.Protocol != BridgeNames.Protocol)
        {
            return BridgeResponse.Failure(BridgeErrorCodes.ProtocolMismatch, string.Create(CultureInfo.InvariantCulture,
                $"Doba speaks bridge protocol {BridgeNames.Protocol} but Doba.Mcp sent {request.Protocol}: they come from different Doba versions. Reinstall Doba so both are the same version."));
        }

        if (ToolCatalog.Find(request.Tool ?? string.Empty) is not { } tool)
        {
            return Invalid($"Unknown tool '{request.Tool}'.");
        }

        if (!context.Settings.ClaudeAccess)
        {
            return BridgeResponse.Failure(BridgeErrorCodes.Closing, AccessOffMessage);
        }

        if (tool.RequiresWrite && context.Settings.ClaudeAccessLevel != ClaudeAccessLevel.ReadWrite)
        {
            return BridgeResponse.Failure(BridgeErrorCodes.ReadOnly, ReadOnlyMessage);
        }

        var args = ToolArgs.Read(tool, request.Args);
        if (args.Error is { } error)
        {
            return Invalid(error);
        }

        var call = new Call(context, args);
        return tool.Name switch
        {
            ToolCatalog.GetToday => call.GetToday(),
            ToolCatalog.ListTasks => call.ListTasks(),
            ToolCatalog.ListInbox => call.ListInbox(),
            ToolCatalog.SearchTasks => call.SearchTasks(),
            ToolCatalog.ListTags => call.ListTags(),
            ToolCatalog.GetTask => call.GetTask(),
            ToolCatalog.CreateTask => call.CreateTask(),
            ToolCatalog.UpdateTask => call.UpdateTask(),
            ToolCatalog.SetTaskDone => call.SetTaskDone(),
            ToolCatalog.MoveTask => call.MoveTask(),
            ToolCatalog.DeleteTask => call.DeleteTask(),
            _ => BridgeResponse.Failure(BridgeErrorCodes.Internal, $"Tool '{tool.Name}' has no implementation in Doba."),
        };
    }

    private static BridgeResponse Invalid(string message) => BridgeResponse.Failure(BridgeErrorCodes.InvalidArgs, message);

    /// <summary>One request: the context, its arguments and today's date.</summary>
    private sealed class Call(ToolContext context, ToolArgs args)
    {
        private readonly ToolContext _context = context;
        private readonly ToolArgs _args = args;
        private readonly DateOnly _today = context.Clock.Today();

        /// <summary>Set by <see cref="Find"/> when it returns null.</summary>
        private BridgeResponse? _failure;

        public BridgeResponse GetToday()
        {
            var local = _context.Clock.GetLocalNow();
            var now = TimeOnly.FromDateTime(local.DateTime);
            var store = _context.Store;
            var model = Projector.Today(store, new ProjectionContext(_today, now, _context.Settings.WeekStart, null));
            return Ok(w =>
            {
                w.WriteString("weekday", DateMath.WeekdayLong(_today.DayOfWeek));
                w.WriteString("now", DateMath.Hhmm(now));
                w.WriteString("utc_offset", Offset(local.Offset));
                w.WriteString("week_start", DateMath.WeekdayLong(_context.Settings.WeekStart));
                foreach (var (key, name) in s_todayGroups)
                {
                    var group = model.Groups.FirstOrDefault(g => g.Key == key);
                    var tasks = group is null ? Enumerable.Empty<TaskItem>() : group.Items.Select(r => store.Get(r.Id)).OfType<TaskItem>();
                    TaskJson.WriteTasks(w, name, tasks, ToolCatalog.ListNotesLength);
                }
            });
        }

        public BridgeResponse ListTasks()
        {
            var from = _args.GetDate("from");
            var to = _args.GetDate("to") ?? from;
            var includeDone = _args.GetBoolean("include_done") ?? false;
            var (limit, offset) = Page();
            if (_args.Error is { } error)
            {
                return Invalid(error);
            }

            if (to < from)
            {
                return Invalid("'to' must not be before 'from'.");
            }

            if (DateMath.DaysBetween(from!.Value, to!.Value) > ToolCatalog.MaxRangeDays)
            {
                return Invalid($"'to' must be at most {ToolCatalog.MaxRangeDays} days after 'from'; ask for a shorter range.");
            }

            var tasks = TaskQueries.InRange(_context.Store, from.Value, to.Value, includeDone);
            return PageResult(tasks, limit, offset, w =>
            {
                TaskJson.WriteDate(w, "from", from);
                TaskJson.WriteDate(w, "to", to);
            });
        }

        public BridgeResponse ListInbox()
        {
            var includeDone = _args.GetBoolean("include_done") ?? false;
            var (limit, offset) = Page();
            if (_args.Error is { } error)
            {
                return Invalid(error);
            }

            return PageResult(TaskQueries.Inbox(_context.Store, includeDone), limit, offset, null);
        }

        public BridgeResponse SearchTasks()
        {
            var query = _args.GetString("query");
            var limit = (int)(_args.GetInteger("limit", 1, ToolCatalog.MaxSearchLimit) ?? ToolCatalog.DefaultSearchLimit);
            if (_args.Error is { } error)
            {
                return Invalid(error);
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return Invalid("'query' must contain some text.");
            }

            // One more than asked tells whether there are more matches.
            var hits = Projector.Search(_context.Store, Projection(), query, limit + 1);
            var tasks = hits.Take(limit).Select(h => _context.Store.Get(h.Row.Id)).OfType<TaskItem>().ToList();
            return Ok(w =>
            {
                w.WriteString("query", query.Trim());
                TaskJson.WriteTasks(w, "tasks", tasks, ToolCatalog.ListNotesLength);
                w.WriteBoolean("truncated", hits.Count > limit);
            });
        }

        public BridgeResponse ListTags()
        {
            var sidebar = Projector.Sidebar(_context.Store, Projection());
            return Ok(w =>
            {
                w.WriteStartArray("tags");
                foreach (var tag in sidebar.Tags)
                {
                    w.WriteStartObject();
                    w.WriteString("name", tag.Name);
                    w.WriteNumber("open_tasks", tag.OpenCount);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            });
        }

        public BridgeResponse GetTask()
        {
            if (Find(edit: false) is not { } found)
            {
                return _failure!;
            }

            return Ok(w =>
            {
                w.WritePropertyName("task");
                TaskJson.WriteTask(w, found);
            });
        }

        public BridgeResponse CreateTask()
        {
            var patch = Patch(TaskClear.None);
            if (_args.Error is { } error)
            {
                return Invalid(error);
            }

            var edit = TaskEdits.Create(_context.Store, patch, _today, _context.Settings.DefaultPriority);
            if (edit.Error is { } rule)
            {
                return Invalid(rule);
            }

            var stored = _context.Store.Add(edit.Task!);
            return Changed(stored, true);
        }

        public BridgeResponse UpdateTask()
        {
            var patch = Patch(_args.GetClear("clear"));
            if (Find(edit: true) is not { } existing)
            {
                return _failure!;
            }

            return Store(existing, TaskEdits.Apply(_context.Store, existing, patch, _today));
        }

        public BridgeResponse SetTaskDone()
        {
            var done = _args.GetBoolean("done");
            if (Find(edit: true) is not { } existing)
            {
                return _failure!;
            }

            return Store(existing, new EditResult(existing with { Done = done!.Value }, null));
        }

        public BridgeResponse MoveTask()
        {
            var text = _args.GetString("date");
            DateOnly? date = null;
            if (text is not null && text != "inbox")
            {
                date = ToolArgs.ParseDate(text);
                if (date is null)
                {
                    return Invalid($"'date' must be a date as YYYY-MM-DD (e.g. 2026-11-20) or \"inbox\"; got '{text}'.");
                }
            }

            if (Find(edit: true) is not { } existing)
            {
                return _failure!;
            }

            return Store(existing, TaskEdits.Move(_context.Store, existing, date));
        }

        public BridgeResponse DeleteTask()
        {
            if (Find(edit: true) is not { } existing)
            {
                return _failure!;
            }

            _context.Store.Delete(existing.Id);
            return Ok(w =>
            {
                w.WritePropertyName("deleted");
                TaskJson.WriteTask(w, existing);
            });
        }

        // ---------- helpers ----------

        private ProjectionContext Projection() =>
            new(_today, _context.Clock.TimeOfDay(), _context.Settings.WeekStart, null);

        /// <summary>The task named by the <c>id</c> argument, read after every other argument: null (and
        /// <see cref="_failure"/> set) when an argument is wrong, the task does not exist or, for a change, it is open
        /// in Doba's editor.</summary>
        private TaskItem? Find(bool edit)
        {
            var id = _args.GetInteger("id", 1, long.MaxValue);
            if (_args.Error is { } error)
            {
                _failure = Invalid(error);
                return null;
            }

            if (_context.Store.Get(id!.Value) is not { } task)
            {
                _failure = BridgeResponse.Failure(BridgeErrorCodes.NotFound, string.Create(CultureInfo.InvariantCulture,
                    $"No task with id {id.Value}. Take ids from get_today, list_tasks, list_inbox or search_tasks."));
                return null;
            }

            if (edit && _context.OpenEditId == task.Id)
            {
                _failure = BridgeResponse.Failure(BridgeErrorCodes.Conflict, string.Create(CultureInfo.InvariantCulture,
                    $"Task {task.Id} is open in Doba's editor; try again after the user closes it."));
                return null;
            }

            return task;
        }

        private TaskPatch Patch(TaskClear clear) => new()
        {
            Title = _args.GetString("title"),
            Notes = _args.GetString("notes"),
            Date = _args.GetDate("date"),
            Days = (int?)_args.GetInteger("days", 1, TaskDialog.MaxDays),
            Time = _args.GetTime("time"),
            DurationMinutes = (int?)_args.GetInteger("duration_minutes", 0, TaskEdits.MaxDurationMinutes),
            Priority = _args.GetPriority("priority"),
            Tag = _args.GetString("tag"),
            Clear = clear,
        };

        private (int Limit, int Offset) Page() =>
            ((int)(_args.GetInteger("limit", 1, ToolCatalog.MaxListLimit) ?? ToolCatalog.DefaultListLimit),
             (int)(_args.GetInteger("offset", 0, int.MaxValue) ?? 0));

        /// <summary>Stores an edit of <paramref name="existing"/>: one Update, or nothing when it changes nothing.</summary>
        private BridgeResponse Store(TaskItem existing, EditResult edit)
        {
            if (edit.Error is { } rule)
            {
                return Invalid(rule);
            }

            var task = edit.Task!;
            if (task.Equals(existing))
            {
                return Changed(existing, false);
            }

            _context.Store.Update(task);
            return Changed(_context.Store.Get(existing.Id) ?? task, true);
        }

        private BridgeResponse Changed(TaskItem task, bool changed) => Ok(w =>
        {
            w.WritePropertyName("task");
            TaskJson.WriteTask(w, task);
            w.WriteBoolean("changed", changed);
        });

        private BridgeResponse PageResult(IReadOnlyList<TaskItem> all, int limit, int offset, Action<Utf8JsonWriter>? head) =>
            Ok(w =>
            {
                head?.Invoke(w);
                var page = all.Skip(offset).Take(limit).ToList();
                TaskJson.WriteTasks(w, "tasks", page, ToolCatalog.ListNotesLength);
                w.WriteNumber("total", all.Count);
                w.WriteNumber("offset", offset);
                w.WriteBoolean("truncated", (long)offset + page.Count < all.Count);
            });

        /// <summary>A success: an object that starts with <c>today</c>, then what <paramref name="body"/> writes.</summary>
        private BridgeResponse Ok(Action<Utf8JsonWriter> body) =>
            BridgeResponse.Success(TaskJson.Build(w =>
            {
                w.WriteStartObject();
                w.WriteString("today", DateMath.Iso(_today));
                body(w);
                w.WriteEndObject();
            }));

        private static string Offset(TimeSpan offset)
        {
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            var abs = offset.Duration();
            return string.Create(CultureInfo.InvariantCulture, $"{sign}{abs.Hours:00}:{abs.Minutes:00}");
        }
    }
}
